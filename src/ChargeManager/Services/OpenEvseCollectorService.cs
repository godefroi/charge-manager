using System.Threading.Channels;
using ChargeManager.Telemetry;
using HiveMQtt.Client;
using HiveMQtt.Client.Events;
using HiveMQtt.MQTT5.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChargeManager.Services;

public class OpenEvseCollectorService : BaseCollectorService, IHostedService, IDisposable
{
	private readonly IHiveMQClient _mqttClient;
	private readonly MetricsService _metricsService;
	private readonly OpenEvseConfiguration _options;
	private readonly Channel<EnergyRecord> _evChannel;
	private readonly ILogger _logger;
	private List<Subscription> _subscriptions = [];
	private double _lastEnergy = 0.0;
	private DateTime _lastEnergyTimestamp = DateTime.MinValue;
	private readonly Dictionary<string, string> _messageBatch = [];
	private readonly Lock _batchLock = new();
	private readonly Timer _debounceTimer;
	private const int DebounceDelayMs = 250; // Wait 100ms after last message before processing batch

	public OpenEvseCollectorService([FromKeyedServices("openevse")]IHiveMQClient mqttClient, MetricsService metricsService, IOptions<OpenEvseConfiguration> options, [FromKeyedServices("ev_energy")]Channel<EnergyRecord> evChannel, ILogger<OpenEvseCollectorService> logger)
	{
		_mqttClient     = mqttClient;
		_metricsService = metricsService;
		_options        = options.Value;
		_evChannel      = evChannel;
		_logger         = logger;
		_debounceTimer  = new(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try {
			_logger.LogInformation("Starting OpenEVSE collector service...");

			await ConnectMqttClient(_mqttClient, _logger);

			var topicPrefix = _options.TopicPrefix.TrimEnd('/');
			var subResult = await _mqttClient.SubscribeAsync(new SubscribeOptionsBuilder()
				.WithSubscriptions([
					// openevse/amp is "amps in 1/10 A increments"
					new($"{topicPrefix}/amp"),
					// openevse/power is "power in watts"
					new($"{topicPrefix}/power"),
					// openevse/session_energy is "energy in wH for this session"
					new($"{topicPrefix}/session_energy"),
					// openevse/vehicle is "1 if car connected, 0 otherwise"
					new($"{topicPrefix}/vehicle"),
					// openevse/session_elapsed is "session time in seconds"
					new($"{topicPrefix}/session_elapsed")
				])
				.Build());

			_logger.LogInformation("Subscribed to {count} OpenEVSE topics", subResult.Subscriptions.Count);

			_subscriptions = subResult.Subscriptions;

			foreach (var sub in _subscriptions) {
				sub.MessageReceivedHandler = HandleMessage;
				_logger.LogDebug("Registered message handler for topic: {Topic}", sub.TopicFilter.Topic);
			}

			_logger.LogInformation("OpenEVSE collector service started successfully");
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to start OpenEVSE collector service");
			throw;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		try {
			_logger.LogInformation("Stopping OpenEVSE collector service...");

			// stop the debounce timer and process any remaining batch
			lock (_batchLock) {
				_debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
			}

			if (_subscriptions.Count > 0) {
				await _mqttClient.UnsubscribeAsync(_subscriptions);
				_logger.LogInformation("Unsubscribed from {Count} OpenEVSE topics", _subscriptions.Count);
				_subscriptions.Clear();
			}

			_logger.LogInformation("OpenEVSE collector service stopped successfully");
		} catch (Exception ex) {
			_logger.LogError(ex, "Error stopping OpenEVSE collector service");
		}
	}

	public void Dispose()
	{
		_debounceTimer.Dispose();

		GC.SuppressFinalize(this);
	}

	private void HandleMessage(object? sender, OnMessageReceivedEventArgs args)
	{
		try {
			var topic = args.PublishMessage.Topic;
			var payload = args.PublishMessage.PayloadAsString;

			if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(payload)) {
				_logger.LogWarning("Received message with null or empty topic/payload");
				return;
			}

			_logger.LogDebug("Received message on topic {Topic}: {Payload}", topic, payload);

			// add/replace message in the batch and reset debounce timer
			lock (_batchLock) {
				_messageBatch[topic] = payload;

				// reset the debounce timer
				_debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error handling MQTT message for topic {Topic}", args.PublishMessage.Topic);
		}
	}

	private void OnDebounceTimerElapsed(object? state)
	{
		try {
			lock (_batchLock) {
				if (_messageBatch.Count > 0) {
					_logger.LogDebug("Debounce timer elapsed. Processing batch with {Count} messages", _messageBatch.Count);
					ProcessBatch();
				}
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error processing message batch");
		}
	}

	private void ProcessBatch()
	{
		var topicPrefix = _options.TopicPrefix.TrimEnd('/');

		try {
			// create a snapshot of the current batch
			_logger.LogDebug("Processing batch with {Count} messages: [{Topics}]", 
				_messageBatch.Count, 
				string.Join(", ", _messageBatch.Keys.Select(k => k.Replace(topicPrefix, ""))));

			double? sessionEnergy = _messageBatch.TryGetValue($"{topicPrefix}/session_energy", out var energyStr) && double.TryParse(energyStr, out var energyDbl) ? energyDbl : null;
			int? sessionElapsed = _messageBatch.TryGetValue($"{topicPrefix}/session_elapsed", out var elapsedStr) && int.TryParse(elapsedStr, out var elapsedInt) ? elapsedInt : null;

			if (_messageBatch.TryGetValue($"{topicPrefix}/amp", out var ampStr) && double.TryParse(ampStr, out var amps)) {
				_metricsService.RecordEvAmps(amps);
			}

			if (_messageBatch.TryGetValue($"{topicPrefix}/power", out var powerStr) && double.TryParse(powerStr, out var power)) {
				_metricsService.RecordEvPower(power);
			}

			if (sessionEnergy.HasValue) {
				_metricsService.RecordEvSessionEnergy(sessionEnergy.Value);

				var now = DateTime.UtcNow;
				var delta = sessionEnergy.Value >= _lastEnergy 
					? sessionEnergy.Value - _lastEnergy 
					: sessionEnergy.Value; // session reset detected

				if (sessionEnergy.Value < _lastEnergy) {
					_logger.LogInformation("New charging session detected. Session energy reset from {Previous}Wh to {Current}Wh", _lastEnergy, sessionEnergy);
					// Reset timestamp on new session
					_lastEnergyTimestamp = now;
				}

				if (delta > 0 && _lastEnergyTimestamp != DateTime.MinValue) {
					_metricsService.RecordEvConsumed(delta);
					_evChannel.Writer.TryWrite(new EnergyRecord(_lastEnergyTimestamp, now, delta));
					_logger.LogDebug("EV energy delta: {Delta:F2} Wh over {Duration:F1}s", delta, (now - _lastEnergyTimestamp).TotalSeconds);
				} else if (_lastEnergyTimestamp == DateTime.MinValue) {
					_logger.LogDebug("Skipping first EV energy measurement - establishing baseline");
				}

				_lastEnergy = sessionEnergy.Value;
				_lastEnergyTimestamp = now;
			}

			if (_messageBatch.TryGetValue($"{topicPrefix}/vehicle", out var vehicleStr)) {
				_metricsService.RecordVehicleConnected(int.TryParse(vehicleStr, out var vehicleInt) && vehicleInt == 1);
			}

			if (sessionElapsed.HasValue) {
				_metricsService.RecordSessionElapsed(sessionElapsed.Value);
			}

			_logger.LogDebug("Batch processing completed successfully");
		} catch (Exception ex) {
			_logger.LogError(ex, "Error during batch processing");
		} finally {
			_messageBatch.Clear();
		}
	}
}
