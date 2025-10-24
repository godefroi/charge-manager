using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace ChargeManager.Telemetry;

public class MetricsService : IDisposable
{
	public const string MeterName = "ChargeManager";

	private readonly Meter _meter;
	private readonly ILogger<MetricsService> _logger;
	private readonly Gauge<double> _production;
	private readonly Gauge<double> _consumption;
	private readonly Gauge<double> _import;
	private readonly Counter<double> _evConsumed;
	private readonly Counter<double> _evGridEnergyConsumed;
	private readonly Gauge<double> _evSessionEnergy;
	private readonly Gauge<double> _evPower;
	private readonly Gauge<double> _evAmps;
	private readonly Gauge<int> _vehicleConnected;
	private readonly Gauge<int> _sessionElapsed;

	public MetricsService(ILogger<MetricsService> logger)
	{
		var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

		_logger = logger;
		_meter  = new Meter(MeterName, version);

		_production = _meter.CreateGauge<double>(
			"envoy.production",
			"W",
			"Current solar production in watts");

		_consumption = _meter.CreateGauge<double>(
			"envoy.consumption", 
			"W",
			"Current energy consumption in watts");

		_import = _meter.CreateGauge<double>(
			"envoy.import",
			"W", 
			"Net energy import in watts");

		_evSessionEnergy = _meter.CreateGauge<double>(
			"openevse.session_energy",
			"Wh",
			"Current charging session energy in watt-hours");

		_evPower = _meter.CreateGauge<double>(
			"openevse.power",
			"W",
			"Current EV charging power in watts");

		_evAmps = _meter.CreateGauge<double>(
			"openevse.amps",
			"A",
			"Current EV charging current in amps");

		_vehicleConnected = _meter.CreateGauge<int>(
			"openevse.vehicle_connected",
			"boolean",
			"Vehicle connection status (1=connected, 0=disconnected)");

		_sessionElapsed = _meter.CreateGauge<int>(
			"openevse.session_elapsed",
			"s",
			"Current charging session elapsed time in seconds");

		_evConsumed = _meter.CreateCounter<double>(
			"chargemanager.ev_consumed",
			"Wh",
			"Total energy consumed by EV in watt-hours (always increasing)");

		_evGridEnergyConsumed = _meter.CreateCounter<double>(
			"chargemanager.ev_grid_energy_consumed",
			"Wh",
			"Total grid energy consumed by EV charging in watt-hours (always increasing)");
	}

	public void RecordProduction(double watts) => _production.Record(watts);

	public void RecordConsumption(double watts) => _consumption.Record(watts);

	public void RecordImport(double watts) => _import.Record(watts);

	public void RecordEvConsumed(double watthours) => _evConsumed.Add(watthours);

	public void RecordEvGridEnergyConsumption(double wattHours) => _evGridEnergyConsumed.Add(wattHours);

	public void RecordEvSessionEnergy(double wattHours) => _evSessionEnergy.Record(wattHours);

	public void RecordEvPower(double watts) => _evPower.Record(watts);

	public void RecordEvAmps(double amps) => _evAmps.Record(amps);

	public void RecordVehicleConnected(bool connected) => _vehicleConnected.Record(connected ? 1 : 0);

	public void RecordSessionElapsed(int seconds) => _sessionElapsed.Record(seconds);

	public void Dispose()
	{
		_meter.Dispose();
		GC.SuppressFinalize(this);
	}
}
