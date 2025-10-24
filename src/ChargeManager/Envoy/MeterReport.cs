using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChargeManager.Envoy;

public record MeterReport(
	/// <summary>
	/// When the report was created
	/// </summary>
	[property: JsonPropertyName("createdAt")]
	[property: JsonConverter(typeof(UnixTimestampConverter))]
	DateTimeOffset CreatedAt,

	/// <summary>
	/// Current active power reading in watts.
	/// </summary>
	[property: JsonPropertyName("reportType")]
	ReportType ReportType,

	/// <summary>
	/// Current active power reading in watts.
	/// </summary>
	[property: JsonPropertyName("cumulative")]
	MeterReading Cumulative,

	/// <summary>
	/// Current active power reading in watts.
	/// </summary>
	[property: JsonPropertyName("lines")]
	MeterReading[] Lines
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportType
{
	[JsonStringEnumMemberName("production")]
	Production,

	[JsonStringEnumMemberName("net-consumption")]
	NetConsumption,

	[JsonStringEnumMemberName("total-consumption")]
	TotalConsumption,

	[JsonStringEnumMemberName("storage")]
	Storage,
}

public class UnixTimestampConverter : JsonConverter<DateTimeOffset>
{
	public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Number ? DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64()) : throw new JsonException("Expected number for Unix timestamp");

	public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(value.ToUnixTimeSeconds());
}
