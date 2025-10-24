using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChargeManager.Envoy;

[JsonConverter(typeof(ReportsConverter))]
public class MeterReports
{
	private readonly Lazy<MeterReport?> _production;
	private readonly Lazy<MeterReport?> _netConsumption;
	private readonly Lazy<MeterReport?> _totalConsumption;

	private MeterReports(List<MeterReport> reports)
	{
		Reports           = reports;
		_production       = new(() => Reports.FirstOrDefault(r => r.ReportType == ReportType.Production));
		_netConsumption   = new(() => Reports.FirstOrDefault(r => r.ReportType == ReportType.NetConsumption));
		_totalConsumption = new(() => Reports.FirstOrDefault(r => r.ReportType == ReportType.TotalConsumption));
	}

	public IReadOnlyList<MeterReport> Reports { get; private set; }

	public MeterReport? Production => _production.Value;

	public MeterReport? NetConsumption => _netConsumption.Value;

	public MeterReport? TotalConsumption => _totalConsumption.Value;

	public double CurrentProductionWatts => Production?.Cumulative.CurrentWatts ?? 0;

	public double CurrentNetConsumptionWatts => NetConsumption?.Cumulative.CurrentWatts ?? 0;

	public double CumulativeReceivedWh => NetConsumption?.Cumulative.CumulativeReceivedWh ?? 0;

	public double CumulativeDeliveredWh => NetConsumption?.Cumulative.CumulativeDeliveredWh ?? 0;

	public double CurrentTotalConsumptionWatts => TotalConsumption?.Cumulative.CurrentWatts ?? 0;

	internal class ReportsConverter : JsonConverter<MeterReports>
	{
		public override MeterReports? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			new(JsonSerializer.Deserialize<List<MeterReport>>(ref reader, options) ?? []);

		public override void Write(Utf8JsonWriter writer, MeterReports value, JsonSerializerOptions options)
		{
			ArgumentNullException.ThrowIfNull(writer);

			if (value == null) {
				writer.WriteNullValue();
			} else {
				JsonSerializer.Serialize(writer, value.Reports, options);
			}
		}
	}
}
