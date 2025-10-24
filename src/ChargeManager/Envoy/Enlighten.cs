using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChargeManager.Envoy;

internal static class Enlighten
{
	private readonly static HttpClient _httpClient = new(new SocketsHttpHandler() {
		PooledConnectionLifetime = TimeSpan.FromMinutes(15),
	});

	private readonly static JsonSerializerOptions _jsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly static Uri _loginUri = new("https://enlighten.enphaseenergy.com/login/login.json");

	public static async Task<LoginResponse> Login(string username, string password, CancellationToken cancellationToken = default)
	{
		using var content = new FormUrlEncodedContent([
			new("user[email]",    username),
			new("user[password]", password),
		]);

		using var response = await _httpClient.PostAsync(_loginUri, content, cancellationToken);

		response.EnsureSuccessStatusCode();

		var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions, cancellationToken);

		return loginResponse ?? throw new InvalidOperationException("Failed to deserialize login response");
	}
}

internal record LoginResponse
(
	[property: JsonPropertyName("message")]
	string Message,

	[property: JsonPropertyName("session_id")]
	string SessionId,

	[property: JsonPropertyName("manager_token")]
	string ManagerToken,

	[property: JsonPropertyName("is_consumer")]
	bool IsConsumer
);
