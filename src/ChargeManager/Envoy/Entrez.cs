using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChargeManager.Envoy;

internal static class Entrez
{
	private readonly static HttpClient _httpClient = new(new SocketsHttpHandler() {
		PooledConnectionLifetime = TimeSpan.FromMinutes(15),
	});
	private readonly static JsonWriterOptions _jsonOptions = new() {
		Indented = false,
	};
	private readonly static Uri _entrezTokensUri = new("https://entrez.enphaseenergy.com/tokens");
	private readonly static MediaTypeHeaderValue _jsonType = new("application/json");

	public static async Task<string> RequestToken(string sessionId, string serialNumber, string username, CancellationToken cancellationToken = default)
	{
		var jsonLen = sessionId.Length + serialNumber.Length + username.Length + 100; // 47 minimum, 100 for a little extra space
		var buffer  = new ArrayBufferWriter<byte>(jsonLen);
		using var writer = new Utf8JsonWriter(buffer, _jsonOptions);

		writer.WriteStartObject();
		writer.WriteString("session_id", sessionId);
		writer.WriteString("serial_num", serialNumber);
		writer.WriteString("username", username);
		writer.WriteEndObject();
		writer.Flush();

		using var content = new ReadOnlyMemoryContent(buffer.WrittenMemory) {
			Headers = { ContentType = _jsonType }
		};

		using var response = await _httpClient.PostAsync(_entrezTokensUri, content, cancellationToken);

		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsStringAsync(cancellationToken);
	}
}
