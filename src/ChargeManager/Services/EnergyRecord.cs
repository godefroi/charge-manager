namespace ChargeManager.Services;

/// <summary>
/// Represents an energy delta (change in energy) over a specific time interval.
/// </summary>
/// <param name="StartTime">The start of the measurement interval (UTC)</param>
/// <param name="EndTime">The end of the measurement interval (UTC)</param>
/// <param name="WattHours">The energy consumed/produced during the interval (positive for consumption/import, negative for export)</param>
public record EnergyRecord(DateTime StartTime, DateTime EndTime, double WattHours);
