using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChargeManager.Envoy;

public class EnvoyClient : IDisposable
{
	private readonly Uri _envoyUrl;
	private readonly Uri _infoXmlUri;
	private readonly Uri _meterReportsUri;
	private readonly HttpClient _infoXmlClient;
	private readonly HttpClient _httpClient;
	private readonly ILogger<EnvoyClient> _logger;

	public EnvoyClient(IOptions<EnvoyConfiguration> configuration, [FromKeyedServices(nameof(infoXmlClient))]HttpClient infoXmlClient, [FromKeyedServices(nameof(envoyClient))]HttpClient envoyClient, ILogger<EnvoyClient> logger)
	{
		_envoyUrl        = configuration.Value.EnvoyUrl;
		_infoXmlUri      = new(_envoyUrl, "/info.xml");
		_meterReportsUri = new(_envoyUrl, "/ivp/meters/reports/");
		_infoXmlClient   = infoXmlClient;
		_httpClient      = envoyClient;
		_logger          = logger;
	}

	public async Task<EnvoyInfo> GetEnvoyInfo(CancellationToken cancellationToken = default)
	{
		using var response = await _infoXmlClient.GetAsync(_infoXmlUri, cancellationToken);

		response.EnsureSuccessStatusCode();

		using var xmlStream = await response.Content.ReadAsStreamAsync(cancellationToken);

		return EnvoyInfo.Parse(xmlStream);
	}

	public async Task<MeterReports> GetMeterReports(CancellationToken cancellationToken = default)
	{
		using var response = await _httpClient.GetAsync(_meterReportsUri, cancellationToken);

		response.EnsureSuccessStatusCode();

		var meterReports = await response.Content.ReadFromJsonAsync<MeterReports>(JsonSerializerOptions.Default, cancellationToken);

		return meterReports ?? throw new InvalidOperationException("Failed to deserialize meter reports response");
	}

	public void Dispose()
	{
		_httpClient?.Dispose();
	}
}
