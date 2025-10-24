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

	/// <summary>
	/// Find all grid records relevant to the provided EV record.
	/// </summary>
	/// <remarks>
	/// Returns <c>true</c> if and only if a "bookending" set of grid records exists such that
	/// at least one grid record starts at or before the EV record's start (or overlaps it) and
	/// at least one grid record ends at or after the EV record's end (or overlaps it).
	/// When such bookends exist the method returns the contiguous slice of sorted grid records
	/// between those bookends (inclusive) in the <paramref name="records"/> out parameter.
	/// Otherwise the method returns <c>false</c> and <paramref name="records"/> will be
	/// an empty enumerable.
	/// </remarks>
	/// <param name="evRecord">The EV energy record to match against grid history.</param>
	/// <param name="records">Outputs the contiguous set of grid records covering the EV period when found, otherwise empty.</param>
	/// <returns><c>true</c> when booking coverage exists; otherwise <c>false</c>.</returns>
	private bool TryFindRelevantRecords(EnergyRecord evRecord, out IEnumerable<EnergyRecord> records)
	{
		records = [];

		if (_gridHistory.Count == 0) {
			return false;
		}

		// sort by start time to make indexing simple; we could instead use a sorted list...
		var sorted = _gridHistory.OrderBy(g => g.StartTime).ToList();

		// find the records that bookend the interval we're analyzing
		var leftIndex  = sorted.FindLastIndex(g => g.StartTime <= evRecord.StartTime);
		var rightIndex = sorted.FindIndex(g => g.EndTime >= evRecord.EndTime);

		// check that we have valid indices
		if (leftIndex == -1 || rightIndex == -1 || leftIndex > rightIndex) {
			return false;
		}

		records = sorted.Skip(leftIndex).Take(rightIndex - leftIndex + 1);

		return true;
	}

	private bool ProcessEvRecord(EnergyRecord evRecord)
	{
		// if the EV used no energy, we can be done here already
		if (evRecord.WattHours <= 0) {
			_metricsService.RecordEvGridEnergyConsumption(0);
			return true;
		}

		// otherwise, find the relevant grid records that bookend the EV period
		// (or fail if that data is not yet available)
		if (!TryFindRelevantRecords(evRecord, out var gridRecords)) {
			return false;
		}

		gridRecords = [.. gridRecords];
		_logger.LogTrace("Processing EV record for {start} to {end} using {gridCount} relevant grid records representing {totalImport} total import",
			evRecord.StartTime.ToString("HH:mm:ss.f"),
			evRecord.EndTime.ToString("HH:mm:ss.f"),
			gridRecords.Count(),
			gridRecords.Sum(r => r.WattHours));

		var (evGridImport, totalGridImport) = gridRecords.Aggregate((EvGridImport: 0d, TotalGridImport: 0d), (cur, gridRecord) => {
			// calculate the overlap for this grid record
			var overlapStart    = gridRecord.StartTime > evRecord.StartTime ? gridRecord.StartTime : evRecord.StartTime;
			var overlapEnd      = gridRecord.EndTime < evRecord.EndTime ? gridRecord.EndTime : evRecord.EndTime;
			var overlapDuration = (overlapEnd - overlapStart).TotalSeconds;
			var gridDuration    = (gridRecord.EndTime - gridRecord.StartTime).TotalSeconds;

			// if there's no overlap, or the grid record represents no actual
			// time, go on to the next one
			if (overlapDuration <= 0 || gridDuration <= 0) {
				return cur;
			}

			// add this record's data
			return (cur.EvGridImport + (gridRecord.WattHours * (overlapDuration / gridDuration)), cur.TotalGridImport + gridRecord.WattHours);
		});

		// // calculate the total grid energy imported during the EV consumption period
		// var totalGridImport = gridRecords.Sum(gridRecord => {
		// 	// calculate the overlap for this grid record
		// 	var overlapStart    = gridRecord.StartTime > evRecord.StartTime ? gridRecord.StartTime : evRecord.StartTime;
		// 	var overlapEnd      = gridRecord.EndTime < evRecord.EndTime ? gridRecord.EndTime : evRecord.EndTime;
		// 	var overlapDuration = (overlapEnd - overlapStart).TotalSeconds;
		// 	var gridDuration    = (gridRecord.EndTime - gridRecord.StartTime).TotalSeconds;

		// 	// if there's no overlap, or the grid record represents no actual time, go on to the next one
		// 	if (overlapDuration <= 0 || gridDuration <= 0) {
		// 		return 0; // No actual overlap
		// 	}

		// 	// return the proportion of this grid record that applies to this period
		// 	return gridRecord.WattHours * (overlapDuration / gridDuration);
		// });

		// Attribution: grid energy used for EV is the minimum of:
		// 1. Energy actually consumed by EV
		// 2. Energy imported from grid during that period
		var gridEnergyForEv = Math.Min(evRecord.WattHours, evGridImport);

		_logger.LogInformation("EV consumed {EvEnergy:F2} Wh between {StartTime:HH:mm:ss.f} and {EndTime:HH:mm:ss.f}, " +
			"of which {gridEnergyForEv:F2} Wh was from grid import (of {totalGridImport:F2} total grid import)",
			evRecord.WattHours, evRecord.StartTime, evRecord.EndTime, gridEnergyForEv, totalGridImport);

		// if (gridEnergyForEv < evRecord.WattHours) {
		// 	using var fs = File.AppendText("bad_data.txt");
		// 	fs.WriteLine($"evRecord.WattHours -> {evRecord.WattHours}");
		// 	fs.WriteLine($"evRecord.StartTime -> {evRecord.StartTime}");
		// 	fs.WriteLine($"evRecord.EndTime -> {evRecord.EndTime}");
		// 	fs.WriteLine($"gridEnergyForEv -> {gridEnergyForEv}");
		// 	fs.WriteLine($"evGridImport -> {evGridImport}");
		// 	fs.WriteLine($"totalGridImport -> {totalGridImport}");
		// 	foreach (var (idx, gr) in gridRecords.Index()) {
		// 		fs.WriteLine($"gr[{idx}].StartTime -> {gr.StartTime}");
		// 		fs.WriteLine($"gr[{idx}].EndTime -> {gr.EndTime}");
		// 		fs.WriteLine($"gr[{idx}].WattHours -> {gr.WattHours}");
		// 	}
		// 	fs.WriteLine("---");
		// }

		_metricsService.RecordEvGridEnergyConsumption(gridEnergyForEv);

		return true;
	}
}
