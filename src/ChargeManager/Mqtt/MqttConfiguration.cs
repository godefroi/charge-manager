namespace ChargeManager.Mqtt;

public class MqttConfiguration
{
	public const string SectionName = "Mqtt";

	public string Host { get; set; } = "core-mosquitto";

	public int Port { get; set; } = 1883;

	public string ClientId { get; set; } = "charge-manager";

	public string? Username { get; set; }

	public string? Password { get; set; }
}
