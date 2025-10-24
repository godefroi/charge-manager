using OpenTelemetry.Exporter;

namespace ChargeManager.Telemetry;

public class OtlpConfiguration
{
	public const string SectionName = "Otlp";

	/// <summary>
	/// The OTLP endpoint URL (e.g., http://localhost:4317 for gRPC or http://localhost:4318/v1/metrics for HTTP)
	/// OTLP export is enabled when this is configured and not empty.
	/// </summary>
	public string? Endpoint { get; set; }

	/// <summary>
	/// Optional headers to include with OTLP requests (e.g., for authentication)
	/// </summary>
	public Dictionary<string, string> Headers { get; set; } = [];

	/// <summary>
	/// Export interval in seconds (default: 30 seconds)
	/// </summary>
	public int ExportIntervalSeconds { get; set; } = 30;

	/// <summary>
	/// Optional: the protocol to use for the OTLP exporter. If unspecified, the Program will
	/// default to Grpc when the endpoint port is 4317, otherwise Http.
	/// </summary>
	public OtlpExportProtocol? Protocol { get; set; }
}
