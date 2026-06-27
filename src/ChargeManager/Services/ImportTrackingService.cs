using System.Threading.Channels;
using ChargeManager.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChargeManager.Services;

public class ImportTrackingService([FromKeyedServices("grid_energy")]Channel<EnergyRecord> gridChannel, [FromKeyedServices("ev_energy")]Channel<EnergyRecord> evChannel, MetricsService metricsService, ILogger<ImportTrackingService> logger) : IHostedService
{
	private readonly Channel<EnergyRecord> _gridChannel = gridChannel;
	private readonly Channel<EnergyRecord> _evChannel = evChannel;
	private readonly MetricsService _metricsService = metricsService;
	private readonly ILogger _logger = logger;
	private readonly CancellationTokenSource _cancelSource = new();
	private readonly List<EnergyRecord> _gridHistory = [];
	private readonly List<EnergyRecord> _pendingEvRecords = [];
	private readonly Lock _historyLock = new();
	private readonly TimeSpan _trackingWindow = TimeSpan.FromMinutes(5); // Track energy for 5 minutes
	private Task? _gridTask;
	private Task? _evTask;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_gridTask = Task.Factory.StartNew(ReadGridEnergy, TaskCreationOptions.LongRunning);
		_evTask   = Task.Factory.StartNew(ReadEvEnergy, TaskCreationOptions.LongRunning);

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_cancelSource.Cancel();
		_gridTask?.Wait(cancellationToken);
		_evTask?.Wait(cancellationToken);
		return Task.CompletedTask;
	}

	private async Task ReadGridEnergy()
	{
		while (!_cancelSource.IsCancellationRequested) {
			try {
				var importRecord = await _gridChannel.Reader.ReadAsync(_cancelSource.Token);

				if (importRecord == null) {
					continue;
				}

				lock (_historyLock) {
					// Add grid record to history
					_gridHistory.Add(importRecord);

					// Try to process any pending EV records that now have complete coverage
					ProcessPendingEvRecords();

					// Clean old records
					PurgeRecords();
				}
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				_logger.LogError(e, "An error occurred while reading grid energy record.");
			}
		}
	}

	private async Task ReadEvEnergy()
	{
		while (!_cancelSource.IsCancellationRequested) {
			try {
				var evEnergyRecord = await _evChannel.Reader.ReadAsync(_cancelSource.Token);

				if (evEnergyRecord == null) {
					continue;
				}

				lock (_historyLock) {
					_pendingEvRecords.Add(evEnergyRecord);
				}
			} catch (OperationCanceledException) {
				break;
			} catch (Exception e) {
				_logger.LogError(e, "An error occurred reading EV grid energy usage.");
			}
		}
	}

	private void PurgeRecords()
	{
		var cutoffTime       = DateTime.UtcNow - _trackingWindow;
		var removedGridCount = _gridHistory.RemoveAll(g => g.EndTime < cutoffTime);
		var removedEvCount   = _pendingEvRecords.RemoveAll(ev => ev.EndTime < cutoffTime);

		if (removedEvCount > 0) {
			_logger.LogWarning("Dropped {expiredCount} expired EV records (never received complete grid coverage)", removedEvCount);
		}
	}

	private void ProcessPendingEvRecords()
	{
		// Must be called within lock(_historyLock)

		if (_pendingEvRecords.Count == 0) {
			return;
		}

		var processed = _pendingEvRecords.RemoveAll(ProcessEvRecord);

		if (processed > 0) {
			_logger.LogDebug("Processed {ProcessedCount} pending EV records, {RemainingCount} still pending", processed, _pendingEvRecords.Count);
		}
	}

	private bool ProcessEvRecord(EnergyRecord evRecord)
	{
		if (evRecord.WattHours <= 0) {
			_metricsService.RecordEvGridEnergyConsumption(0);
			return true;
		}

		if (!GridAttributionCalculator.TryFindRelevantRecords(_gridHistory, evRecord, out var gridRecords)) {
			return false;
		}

		var materializedRecords = gridRecords.ToArray();
		_logger.LogTrace("Processing EV record for {start} to {end} using {gridCount} relevant grid records representing {totalImport} total import",
			evRecord.StartTime.ToString("HH:mm:ss.f"),
			evRecord.EndTime.ToString("HH:mm:ss.f"),
			materializedRecords.Length,
			materializedRecords.Sum(r => r.WattHours));

		var (gridEnergyForEv, totalGridImport) = GridAttributionCalculator.CalculateGridEnergyForEv(evRecord, materializedRecords);

		_logger.LogInformation("EV consumed {EvEnergy:F2} Wh between {StartTime:HH:mm:ss.f} and {EndTime:HH:mm:ss.f}, " +
			"of which {gridEnergyForEv:F2} Wh was from grid import (of {totalGridImport:F2} total grid import)",
			evRecord.WattHours, evRecord.StartTime, evRecord.EndTime, gridEnergyForEv, totalGridImport);

		_metricsService.RecordEvGridEnergyConsumption(gridEnergyForEv);

		return true;
	}
}
