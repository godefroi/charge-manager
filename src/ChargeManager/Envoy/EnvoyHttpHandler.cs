using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChargeManager.Envoy;

public class EnvoyTlsHandler : HttpClientHandler
{
	private readonly string _deviceSerial;
	private readonly ILogger _logger;

	public EnvoyTlsHandler(IOptions<EnvoyConfiguration> config, ILogger logger)
	{
		_deviceSerial = config.Value.DeviceSerial;
		_logger       = logger;

		ServerCertificateCustomValidationCallback = (_, cert, _, _) => {
			if (cert == null) {
				_logger.LogError("No certificate provided for Envoy TLS connection.");
				return false;
			}

			var commonName = cert.SubjectName
				.EnumerateRelativeDistinguishedNames()
				.Where(rdn => rdn.GetSingleElementType().FriendlyName == "CN")
				.Select(rdn => rdn.GetSingleElementValue())
				.FirstOrDefault();

			if (commonName == null) {
				_logger.LogError("No common name found in certificate");
				return false;
			}

			if (!commonName.EndsWith(_deviceSerial)) {
				_logger.LogError("Certificate common name '{commonName}' does not match device serial '{deviceSerial}'", commonName, _deviceSerial);
				return false;
			}

			// validate the chain maybe somehow?

			return true;
		};
	}
}

internal class EnvoyHttpHandler : EnvoyTlsHandler
{
	private readonly string _username;
	private readonly string _password;
	private readonly TimeSpan _sessionTimeout;
	private readonly Uri _authCheckUrl;
	private readonly string _deviceSerial;
	private readonly ILogger _logger;

	private string? _enlightenSession;
	private JwtToken? _jwtToken;
	private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

	public EnvoyHttpHandler(IOptions<EnvoyConfiguration> configuration, ILogger<EnvoyHttpHandler> logger) : base(configuration, logger)
	{
		_username        = configuration.Value.Username;
		_password        = configuration.Value.Password;
		_sessionTimeout  = configuration.Value.SessionTimeout;
		_authCheckUrl    = new Uri(configuration.Value.EnvoyUrl, "/auth/check_jwt");
		_deviceSerial    = configuration.Value.DeviceSerial;
		_logger          = logger;

		CookieContainer = new CookieContainer();
		UseCookies      = true;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		// ensure we have a valid Enlighten session
		if (string.IsNullOrEmpty(_enlightenSession)) {
			_logger.LogInformation("No Enlighten session information present. Logging in with username/password.");

			var loginResponse   = await Enlighten.Login(_username, _password, cancellationToken);

			if (loginResponse?.Message != "success") {
				throw new Exception($"Login failed: {loginResponse?.Message}");
			}

			_enlightenSession = loginResponse.SessionId;
			_jwtToken		  = null; // invalidate the JWT token when we get a new enlighten session
			_lastRequest	  = DateTimeOffset.MinValue;
		}

		// ensure we have a valid JWT token
		if (_jwtToken?.IsValid != true) {
			_logger.LogInformation("JWT token invalid or not present. Renewing token from Enlighten session information.");

			_jwtToken    = new JwtToken(await Entrez.RequestToken(_enlightenSession, _deviceSerial, _username, cancellationToken));
			_lastRequest = DateTimeOffset.MinValue;
		}

		// ensure we have a valid device session
		if (DateTimeOffset.Now - _lastRequest > _sessionTimeout) {
			_logger.LogInformation("Renewing Envoy session using JWT token (last request was at {lastRequest})", _lastRequest);

			var authrequest = new HttpRequestMessage(HttpMethod.Post, _authCheckUrl);

			authrequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken.Token);

			(await base.SendAsync(authrequest, cancellationToken).ConfigureAwait(false)).EnsureSuccessStatusCode();
		}

		_lastRequest = DateTimeOffset.Now;

		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

		if (response.StatusCode == HttpStatusCode.Unauthorized) {
			_logger.LogError("Envoy request returned unauthorized; resetting last request marker.");
			_lastRequest = DateTimeOffset.MinValue;
		}

		return response;
	}

	internal record JwtToken
	{
		[SetsRequiredMembers]
		public JwtToken(string token)
		{
			Token = token;
			(ValidFrom, ValidTo) = DecodeValidityClaims(token);
		}

		public required string Token { get; init; }

		public required DateTimeOffset ValidFrom { get; init; }

		public required DateTimeOffset ValidTo { get; init; }

		public bool IsValid => DateTimeOffset.UtcNow >= ValidFrom && DateTimeOffset.UtcNow < ValidTo;

		private static (DateTimeOffset ValidFrom, DateTimeOffset ValidTo) DecodeValidityClaims(string input)
		{
			// JWT format: header.payload.signature
			// Find the second dot to isolate the payload
			var jwtSpan  = input.AsSpan();
			var firstDot = jwtSpan.IndexOf('.');

			if (firstDot == -1) {
				throw new ArgumentException("Invalid JWT format", nameof(input));
			}

			var secondDot = jwtSpan[(firstDot + 1)..].IndexOf('.');

			if (secondDot == -1) {
				throw new ArgumentException("Invalid JWT format", nameof(input));
			}

			// Extract payload (between first and second dot)
			var payloadSpan = jwtSpan.Slice(firstDot + 1, secondDot);

			// Calculate the required buffer size for Base64Url decoding
			var padding = (4 - (payloadSpan.Length % 4)) % 4;
			var bufferSize = payloadSpan.Length + padding;

			// Use stack allocation for small payloads (typical JWT payloads are small)
			Span<char> base64Buffer = bufferSize <= 1024 
				? stackalloc char[bufferSize] 
				: new char[bufferSize];

			// Copy payload and add Base64 padding if needed
			payloadSpan.CopyTo(base64Buffer);

			// Replace Base64Url characters with standard Base64
			base64Buffer.Replace('_', '/');
			base64Buffer.Replace('-', '+');
			base64Buffer[^padding..].Fill('=');

			// Decode Base64 to bytes
			var maxByteCount = Encoding.UTF8.GetMaxByteCount(bufferSize);
			Span<byte> jsonBytes = maxByteCount <= 1024 
				? stackalloc byte[maxByteCount] 
				: new byte[maxByteCount];

			if (!Convert.TryFromBase64Chars(base64Buffer, jsonBytes, out int bytesWritten)) {
				throw new ArgumentException("Invalid Base64 encoding in JWT payload", nameof(input));
			}

			// Parse JSON payload
			var jsonPayload = jsonBytes[..bytesWritten];
			var reader = new Utf8JsonReader(jsonPayload);
			using var doc = JsonDocument.ParseValue(ref reader);

			// Extract 'iat' (issued at) and 'exp' (expiration) claims
			var validFrom = doc.RootElement.TryGetProperty("iat", out var iatElement) && iatElement.GetInt64() is var iat and > 0
				? DateTimeOffset.FromUnixTimeSeconds(iat)
				: DateTimeOffset.MinValue;

			var validTo = doc.RootElement.TryGetProperty("exp", out var expElement) && expElement.GetInt64() is var exp and > 0
				? DateTimeOffset.FromUnixTimeSeconds(exp)
				: DateTimeOffset.MaxValue;

			return (validFrom, validTo);
		}
	}
}
