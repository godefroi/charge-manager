using System.Diagnostics;
using System.Threading.Channels;
using ChargeManager.Envoy;
using ChargeManager.Telemetry;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChargeManager.Services;

public class EnvoyCollectorService(EnvoyClient envoyClient, [FromKeyedServices("envoy")]IHiveMQClient mqttClient, IOptions<EnvoyConfiguration> options, MetricsService metricsService, [FromKeyedServices("grid_energy")]Channel<EnergyRecord> importChannel, ILogger<EnvoyCollectorService> logger) : BaseCollectorService, IHostedService
{
	private readonly static TimeSpan _interval = TimeSpan.FromSeconds(10);

	private readonly EnvoyClient _envoyClient = envoyClient;
	private readonly IHiveMQClient _mqttClient = mqttClient;
	private readonly EnvoyConfiguration _options = options.Value;
	private readonly MetricsService _metricsService = metricsService;
	private readonly ILogger _logger = logger;
	private readonly CancellationTokenSource _cancelSource = new();
	private readonly Channel<EnergyRecord> _importChannel = importChannel;
	private double _lastCumulativeImport = 0.0;
	private DateTime _lastImportTimestamp = DateTime.MinValue;
	private Task? _collectTask;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(_options.TopicPrefix)) {
			await ConnectMqttClient(_mqttClient, _logger);
		}

		_collectTask = Task.Factory.StartNew(DoCollectorThings, TaskCreationOptions.LongRunning);
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_cancelSource.Cancel();
		_collectTask?.Wait(cancellationToken);
		return Task.CompletedTask;
	}

	private async Task DoCollectorThings()
	{
		var topicPrefix = _options.TopicPrefix?.TrimEnd('/');
		var lastRun     = Stopwatch.GetTimestamp();

		while (!_cancelSource.IsCancellationRequested) {
			try {
				// wait however much time we need to to get to the next scheduled report
				var wait = _interval - Stopwatch.GetElapsedTime(lastRun);

				if (wait > TimeSpan.Zero) {
					await Task.Delay(wait, _cancelSource.Token);
				}

				lastRun = Stopwatch.GetTimestamp();

				if (_cancelSource.IsCancellationRequested) {
					break;
				}

				// get a meter report
				var mr = await _envoyClient.GetMeterReports(_cancelSource.Token);

				if (mr == null) {
					continue;
				}

			// publish metrics to mqtt to enable OpenEVSE to manage the PV divert (eco mode)
			if (!string.IsNullOrWhiteSpace(topicPrefix)) {
				await PublishMetric($"{topicPrefix}/production", mr.CurrentProductionWatts);
				await PublishMetric($"{topicPrefix}/consumption", mr.CurrentTotalConsumptionWatts);
				await PublishMetric($"{topicPrefix}/import", mr.CurrentNetConsumptionWatts);
			}

			// record metrics for otel export
				_metricsService.RecordProduction(mr.CurrentProductionWatts);
				_metricsService.RecordConsumption(mr.CurrentTotalConsumptionWatts);
				_metricsService.RecordImport(mr.CurrentNetConsumptionWatts);

				// write grid import delta to channel for ImportTrackingService
				// use the actual measurement timestamp from the Envoy
				if (mr.NetConsumption != null) {
					var currentTimestamp  = mr.NetConsumption.CreatedAt.UtcDateTime;
					var currentCumulative = mr.CumulativeDeliveredWh;

					if (_lastImportTimestamp != DateTime.MinValue) {
						var delta = currentCumulative - _lastCumulativeImport; // TODO: validate that this is showing what we think it is (i.e. the total imported from the grid)

						_logger.LogDebug("Solar data: duration: {duration:F1} delta: {delta:F1}, instantaneous: {instantaneous:F1} estimated: {estimated:F1}",
							(currentTimestamp - _lastImportTimestamp).TotalSeconds,
							delta,
							mr.CurrentNetConsumptionWatts,
							mr.CurrentNetConsumptionWatts * (currentTimestamp - _lastImportTimestamp).TotalSeconds / 3600d);

						// Write record for positive deltas (actual import) or zero (no import)
						// This ensures the tracking service knows what intervals have been covered
						if (delta >= 0) {
							await _importChannel.Writer.WriteAsync(new EnergyRecord(_lastImportTimestamp, currentTimestamp, delta), _cancelSource.Token);
							_logger.LogDebug("Grid import delta: {Delta:F2} Wh over {Duration:F1}s", delta, (currentTimestamp - _lastImportTimestamp).TotalSeconds);
						} else {
							// Negative delta indicates net export - write a zero delta record
							// so the tracking service knows this interval has been covered
							await _importChannel.Writer.WriteAsync(new EnergyRecord(_lastImportTimestamp, currentTimestamp, 0.0), _cancelSource.Token);
							_logger.LogDebug("Grid import delta: {Delta:F2} Wh over {Duration:F1}s (writing zero import record for interval coverage)", delta, (currentTimestamp - _lastImportTimestamp).TotalSeconds);
						}
					} else {
						_logger.LogDebug("Skipping first grid import measurement - establishing baseline");
					}

					_lastCumulativeImport = currentCumulative;
					_lastImportTimestamp = currentTimestamp;
				}

				// log some diagnostic info
				logger.LogDebug("Solar data: [production: {production} consumption: {consumption} import: {import}]", mr?.CurrentProductionWatts, mr?.CurrentTotalConsumptionWatts, mr?.CurrentNetConsumptionWatts);
			} catch (TaskCanceledException) {
				// swallow task cancelled exception and exit the loop
				break;
			} catch (Exception e) {
				logger.LogError(e, "An error occurred.");
			}
		}
	}

	private async Task PublishMetric(string topic, double? value)
	{
		if (!value.HasValue) {
			return;
		}

		var message = new MQTT5PublishMessage() {
			Topic = topic,
			Retain = true,
			PayloadAsString = Math.Round(value.Value, MidpointRounding.ToZero).ToString(),
			MessageExpiryInterval = 5,
			QoS = QualityOfService.AtMostOnceDelivery,
		};

		try {
			var result = await _mqttClient.PublishAsync(message, _cancelSource.Token);
		}
		catch (Exception ex) {
			_logger.LogError(ex, "Exception while publishing to {Topic}", topic);
		}
	}
}
