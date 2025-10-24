using HiveMQtt.Client;
using HiveMQtt.MQTT5.ReasonCodes;
using Microsoft.Extensions.Logging;

namespace ChargeManager.Services;

public abstract class BaseCollectorService
{
	protected static async Task ConnectMqttClient(IHiveMQClient mqttClient, ILogger logger)
	{
		while (!mqttClient.IsConnected()) {
			logger.LogInformation("Connecting to MQTT broker...");

			var result = await mqttClient.ConnectAsync();

			if (result.ReasonCode == ConnAckReasonCode.Success) {
				continue;
			} else if (result.ReasonCode == ConnAckReasonCode.ServerUnavailable) {
				logger.LogError("Connection to MQTT broker failed (server unavailable): {message}", result.ReasonString);
				throw new Exception(result.ReasonString);
			}

			logger.LogWarning("Connection to MQTT broker failed: {message}", result.ReasonString);
			await Task.Delay(10000);
		}
	}
}
