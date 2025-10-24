namespace ChargeManager.Envoy;

public class EnvoyConfiguration
{
	private const string _defaultEnvoyUrl = "https://envoy.local";

	public const string SectionName = "Envoy";

	public static readonly Uri DefaultEnvoyUrl = new(_defaultEnvoyUrl);

	public required string Username { get; set; }

	public required string Password { get; set; }

	public required string DeviceSerial { get; set; }

	public Uri EnvoyUrl { get; set; } = DefaultEnvoyUrl;

	public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(9);

	public string? TopicPrefix { get; set; }
}
