namespace ChargeManager.Services;

internal static class GridAttributionCalculator
{
	/// <summary>
	/// Finds the contiguous slice of sorted grid records that bookend the given EV record's interval.
	/// Returns true when at least one grid record starts at or before the EV start and at least one
	/// ends at or after the EV end; otherwise returns false and <paramref name="records"/> is empty.
	/// </summary>
	internal static bool TryFindRelevantRecords(IReadOnlyList<EnergyRecord> gridHistory, EnergyRecord evRecord, out IEnumerable<EnergyRecord> records)
	{
		records = [];

		if (gridHistory.Count == 0) {
			return false;
		}

		var sorted = gridHistory.OrderBy(g => g.StartTime).ToList();

		var leftIndex  = sorted.FindLastIndex(g => g.StartTime <= evRecord.StartTime);
		var rightIndex = sorted.FindIndex(g => g.EndTime >= evRecord.EndTime);

		if (leftIndex == -1 || rightIndex == -1 || leftIndex > rightIndex) {
			return false;
		}

		records = sorted.Skip(leftIndex).Take(rightIndex - leftIndex + 1);
		return true;
	}

	/// <summary>
	/// Calculates the grid energy attributable to the EV for the given interval.
	/// Returns the proportionally time-weighted grid import capped at actual EV consumption,
	/// along with the raw total grid import across the provided records.
	/// </summary>
	internal static (double GridEnergyForEv, double TotalGridImport) CalculateGridEnergyForEv(EnergyRecord evRecord, IEnumerable<EnergyRecord> gridRecords)
	{
		if (evRecord.WattHours <= 0) {
			return (0, 0);
		}

		var (evGridImport, totalGridImport) = gridRecords.Aggregate(
			(EvGridImport: 0d, TotalGridImport: 0d),
			(cur, gridRecord) => {
				var overlapStart    = gridRecord.StartTime > evRecord.StartTime ? gridRecord.StartTime : evRecord.StartTime;
				var overlapEnd      = gridRecord.EndTime < evRecord.EndTime ? gridRecord.EndTime : evRecord.EndTime;
				var overlapDuration = (overlapEnd - overlapStart).TotalSeconds;
				var gridDuration    = (gridRecord.EndTime - gridRecord.StartTime).TotalSeconds;

				if (overlapDuration <= 0 || gridDuration <= 0) {
					return cur;
				}

				return (
					cur.EvGridImport + (gridRecord.WattHours * (overlapDuration / gridDuration)),
					cur.TotalGridImport + gridRecord.WattHours
				);
			});

		return (Math.Min(evRecord.WattHours, evGridImport), totalGridImport);
	}
}
