using ChargeManager.Envoy;
using ChargeManager.Mqtt;
using ChargeManager.Services;
using ChargeManager.Telemetry;
using HiveMQtt.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using System.Reflection;
using System.Threading.Channels;
using System.Security;

namespace ChargeManager;

internal class Program
{
	private static async Task Main(string[] args)
	{
		var builder = Host.CreateApplicationBuilder(args);

		builder.Configuration.AddUserSecrets<Program>();

		builder.Services.Configure<EnvoyConfiguration>(builder.Configuration.GetSection(EnvoyConfiguration.SectionName));
		builder.Services.Configure<MqttConfiguration>(builder.Configuration.GetSection(MqttConfiguration.SectionName));
		builder.Services.Configure<OpenEvseConfiguration>(builder.Configuration.GetSection(OpenEvseConfiguration.SectionName));
		builder.Services.Configure<OtlpConfiguration>(builder.Configuration.GetSection(OtlpConfiguration.SectionName));

		builder.Services.AddKeyedSingleton("infoXmlClient", (sp, _) =>
			new HttpClient(new EnvoyTlsHandler(sp.GetRequiredService<IOptions<EnvoyConfiguration>>(), sp.GetRequiredService<ILogger<EnvoyTlsHandler>>())));
		builder.Services.AddKeyedSingleton("envoyClient", (sp, _) =>
			new HttpClient(new EnvoyHttpHandler(sp.GetRequiredService<IOptions<EnvoyConfiguration>>(), sp.GetRequiredService<ILogger<EnvoyHttpHandler>>())));

		builder.Services.AddSingleton<EnvoyClient>();
		builder.Services.AddSingleton<MetricsService>();
		builder.Services.AddKeyedSingleton("ev_energy", Channel.CreateBounded<EnergyRecord>(new BoundedChannelOptions(5) {
				SingleWriter = true,
				SingleReader = true,
				FullMode = BoundedChannelFullMode.Wait,
				AllowSynchronousContinuations = false,
		}));
		builder.Services.AddKeyedSingleton("grid_energy", Channel.CreateBounded<EnergyRecord>(new BoundedChannelOptions(5) {
				SingleWriter = true,
				SingleReader = true,
				FullMode = BoundedChannelFullMode.Wait,
				AllowSynchronousContinuations = false,
		}));

		// configure otel
		var otlpConfig = builder.Configuration.GetSection(OtlpConfiguration.SectionName).Get<OtlpConfiguration>();
		var assembly = Assembly.GetExecutingAssembly();

		builder.Services.AddOpenTelemetry()
			.ConfigureResource(resource => resource
				.AddService(assembly.GetName().Name ?? "charge-manager", assembly.GetName().Version?.ToString() ?? "1.0.0"))
			.WithMetrics(metrics => {
				var meterBuilder = metrics.AddMeter(MetricsService.MeterName);

				if (!string.IsNullOrWhiteSpace(otlpConfig?.Endpoint)) {
					meterBuilder.AddOtlpExporter(options => {
						// set endpoint
						options.Endpoint = new Uri(otlpConfig.Endpoint);

						// determine protocol: prefer explicit configuration, otherwise use port heuristic
						options.Protocol = otlpConfig.Protocol ?? options.Endpoint.Port switch {
							4317 => OtlpExportProtocol.Grpc,
							4318 => OtlpExportProtocol.HttpProtobuf,
							_ => throw new InvalidOperationException("Unable to determine OTLP export protocol."),
						};

						// headers
						options.Headers = string.Join(',', (otlpConfig.Headers ?? []).Select(kvp => $"{kvp.Key}={kvp.Value}"));

						options.ExportProcessorType = ExportProcessorType.Batch;
						options.BatchExportProcessorOptions = new() {
							ExporterTimeoutMilliseconds = 30000,
							ScheduledDelayMilliseconds = otlpConfig.ExportIntervalSeconds * 1000
						};
					});
				}
			});

		builder.Services.AddKeyedSingleton<IHiveMQClient>("envoy", (sp, key) => {
			var config  = sp.GetRequiredService<IOptions<MqttConfiguration>>().Value;
			var options = new HiveMQClientOptionsBuilder()
				.WithBroker(config.Host)
				.WithPort(config.Port)
				.WithClientId($"{config.ClientId}-{key}")
				.WithAutomaticReconnect(true)
				.Build();

			if (!string.IsNullOrEmpty(config.Username)) {
				options.UserName = config.Username;
				options.Password = MakeSecureString(config.Password);
			}

			return new HiveMQClient(options);
		});

		builder.Services.AddKeyedSingleton<IHiveMQClient>("openevse", (sp, key) => {
			var config  = sp.GetRequiredService<IOptions<MqttConfiguration>>().Value;
			var options = new HiveMQClientOptionsBuilder()
				.WithBroker(config.Host)
				.WithPort(config.Port)
				.WithClientId($"{config.ClientId}-{key}")
				.WithAutomaticReconnect(true)
				.Build();

			if (!string.IsNullOrEmpty(config.Username)) {
				options.UserName = config.Username;
				options.Password = MakeSecureString(config.Password);
			}

			return new HiveMQClient(options);
		});

		builder.Services.AddHostedService<OpenEvseCollectorService>();
		builder.Services.AddHostedService<EnvoyCollectorService>();
		builder.Services.AddHostedService<ImportTrackingService>();

		var host = builder.Build();

		await host.RunAsync();
	}

	private static SecureString? MakeSecureString(string? str)
	{
		if (str == null) {
			return null;
		}

		var ret = new SecureString();

		foreach (var c in str) {
			ret.AppendChar(c);
		}

		ret.MakeReadOnly();

		return ret;
	}
}
