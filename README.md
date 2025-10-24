# Charge Manager

Charge Manager is a .NET 9 application that monitors solar energy production and consumption from an Enphase Envoy device and publishes the data via MQTT, with the aim to provide this data to an OpenEVSE device to enable "Eco" mode. It also provides optional OpenTelemetry metrics export for integration with observability platforms.


## Purpose

Charge Manager bridges solar energy monitoring with smart charging infrastructure by:

- **Solar Monitoring**: Retrieves real-time production, consumption, and grid import data from Enphase Envoy devices
- **MQTT Integration**: Publishes solar metrics to MQTT brokers for integration with home automation systems
- **EV Charging Coordination**: Subscribes to OpenEVSE charger status (amperage and power) to monitor charging activity
- **Observability**: Exports metrics via OpenTelemetry Protocol (OTLP) for monitoring and analytics

This enables smart charging scenarios where EV charging can be optimized based on solar production and home energy consumption patterns.


## Features

- Real-time solar energy monitoring via Enphase Envoy API
- MQTT publishing with configurable topics and retention
- OpenEVSE charger status monitoring
- OpenTelemetry metrics export (optional)
- Automatic reconnection and error recovery
- User secrets support for secure credential management


## Building and Running

### Prerequisites

- **.NET 9.0 SDK** or later
- **Access to an Enphase Envoy device** on your network
- **MQTT broker** (e.g., Mosquitto or similar)
- **(Optional) OpenTelemetry-compatible backend** for metrics export

### Setup Steps

1. **Configure the application**

   Copy the example configuration and customize it:
   ```bash
   cd src/ChargeManager
   cp appsettings.example.json appsettings.json
   ```

   Edit `appsettings.json` and update the required fields (see [Configuration](#configuration) section)

2. **(Recommended) Use User Secrets for sensitive data**

   Instead of storing passwords in `appsettings.json`, use .NET user secrets:

   ```bash
   cd src/ChargeManager
   dotnet user-secrets set "Envoy:Username" "your_envoy_username"
   dotnet user-secrets set "Envoy:Password" "your_envoy_password"
   dotnet user-secrets set "Envoy:DeviceSerial" "your_device_serial"
   dotnet user-secrets set "Mqtt:Username" "your_mqtt_username"
   dotnet user-secrets set "Mqtt:Password" "your_mqtt_password"
   ```

   The application automatically loads user secrets, which take precedence over values in `appsettings.json`.

3. **Build the application**

   ```bash
   dotnet build
   ```

4. **Run the application**

   ```bash
   dotnet run
   ```

   The application will start monitoring your Envoy device and publishing data to MQTT.


## Configuration

### Quick Start

1. Copy `appsettings.example.json` to `appsettings.json`:
   ```bash
   cp appsettings.example.json appsettings.json
   ```

2. Edit `appsettings.json` and update the required configuration sections below

3. For sensitive data (passwords, tokens), use .NET user secrets (recommended - see [Using User Secrets](#using-user-secrets-recommended) section)

### Configuration Sections

#### Envoy Configuration

The Envoy section is **required** and contains settings for connecting to your Enphase Envoy device.

```json
{
  "Envoy": {
    "Username": "your_envoy_username",
    "Password": "your_envoy_password",
    "DeviceSerial": "your_device_serial_number",
    "EnvoyUrl": "https://envoy.local",
    "SessionTimeout": "00:09:00",
    "TopicPrefix": "envoysolar"
  }
}
```

**Required Fields:**
- **Username**: The username for accessing your Envoy device
- **Password**: The Envoy installer password (for authentication)
- **DeviceSerial**: Your Envoy device's serial number (found on the device or in the Enphase app)

**Optional Fields:**
- **EnvoyUrl**: URL to your Envoy device (default: `https://envoy.local`)
- **SessionTimeout**: Duration before session authentication expires (default: `00:09:00`)
- **TopicPrefix**: Prefix for MQTT topics published by this application. Note that if this field is not configured, **data will not be published to MQTT**.

#### MQTT Configuration

The MQTT section is **required** for publishing solar metrics and subscribing to charger status.

```json
{
  "Mqtt": {
    "Host": "core-mosquitto",
    "Port": 1883,
    "ClientId": "charge-manager",
    "Username": "",
    "Password": ""
  }
}
```

**Fields:**
- **Host**: MQTT broker hostname or IP address (required)
- **Port**: MQTT broker port (default: `1883` for unencrypted; use `8883` for TLS)
- **ClientId**: Unique client identifier for MQTT connections (default: `charge-manager`)
- **Username**: MQTT broker username (optional - leave empty if not required)
- **Password**: MQTT broker password (optional - leave empty if not required)

**Published MQTT Topics:**

The application publishes solar metrics to the following topics under your configured prefix:

- `{TopicPrefix}/production` - Solar energy production in watts
- `{TopicPrefix}/consumption` - Total energy consumption in watts  
- `{TopicPrefix}/import` - Net energy import from grid in watts (positive = importing)

*Example with prefix `envoysolar`:*
- `envoysolar/production`
- `envoysolar/consumption`
- `envoysolar/import`

**Subscribed MQTT Topics:**

The application subscribes to OpenEVSE charger topics to monitor charging status. By default, these topics use the `openevse` prefix, which matches the default MQTT topic prefix used by OpenEVSE devices:

- `openevse/amp` - Current charging amperage (in 1/10 A increments)
- `openevse/power` - Current charging power in watts
- `openevse/session_energy` - Session energy in watt-hours
- `openevse/vehicle` - Vehicle connection status (1 = connected, 0 = disconnected)
- `openevse/session_elapsed` - Session duration in seconds

#### OpenEVSE Configuration (Optional)

The OpenEVSE section allows you to customize the MQTT topic prefix used when subscribing to OpenEVSE charger status.

```json
{
  "OpenEvse": {
    "TopicPrefix": "openevse"
  }
}
```

**Fields:**
- **TopicPrefix**: The MQTT topic prefix for OpenEVSE charger topics (default: `openevse`)
  
**Note:** The default value of `openevse` matches the default MQTT topic prefix used by OpenEVSE charging devices. If your OpenEVSE device publishes to a different prefix, update this configuration accordingly.

#### OpenTelemetry (OTLP) Configuration (Optional)

OTLP metrics export is **optional**. Configure this section only if you want to send metrics to an OpenTelemetry-compatible backend.

```json
{
  "Otlp": {
    "Endpoint": "http://localhost:4318/v1/metrics",
    "Headers": {
      "Authorization": "Bearer your-token-here"
    },
    "ExportIntervalSeconds": 30,
    "Protocol": "HttpProtobuf"
  }
}
```

**Fields:**
- **Endpoint**: The OTLP endpoint URL (leave empty to disable OTLP export)
  - For gRPC: `http://localhost:4317`
  - For HTTP: `http://localhost:4318/v1/metrics`
- **Headers**: Optional headers for authentication (e.g., for Grafana Cloud)
- **ExportIntervalSeconds**: How often metrics are exported in seconds (default: `30`)
- **Protocol**: Optional protocol specification (`Grpc` or `HttpProtobuf`)
  - If not specified, the application automatically detects based on endpoint port (4317 = gRPC, 4318 = HTTP)

**Exported Metrics:**

When OTLP export is enabled, the following metrics are exported:

- **envoy_production_watts** (Gauge): Current solar production in watts
- **envoy_consumption_watts** (Gauge): Current energy consumption in watts
- **envoy_import_watts** (Gauge): Net energy import in watts

**Compatible OTLP Backends:**

This implementation supports any OTLP-compatible observability platform, including:

- **Prometheus** (with OTLP receiver)
- **Grafana Cloud**
- **Datadog**
- **New Relic**
- **Azure Monitor**
- **AWS X-Ray**
- **Google Cloud Monitoring**
- **Jaeger**
- **OpenTelemetry Collector**

#### Example: Using OpenTelemetry Collector

If you want to use the OpenTelemetry Collector as an intermediary, here's an example configuration:

```yaml
# otel-collector-config.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:

exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
  
service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus]
```

Then configure the application to send to the collector:

```json
{
  "Otlp": {
    "Endpoint": "http://otel-collector:4317",
    "Protocol": "Grpc"
  }
}
```

### Logging Configuration (Optional)

You can control logging verbosity by configuring the `Logging` section in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information",
      "ChargeManager.Services.EnvoyCollectorService": "Information",
      "ChargeManager.Services.OpenEvseCollectorService": "Information",
      "ChargeManager.Services.ImportTrackingService": "Information"
    }
  }
}
```

**Log Levels** (from least to most verbose):
- `Critical`: Fatal errors
- `Error`: Error conditions
- `Warning`: Warning messages
- `Information`: Informational messages (default)
- `Debug`: Debug-level messages
- `Trace`: Detailed diagnostic information (most verbose)

**Tip:** For troubleshooting connection issues, temporarily set the service log levels to `Debug` or `Trace`.


## License

This project is open source. Please refer to the license file for details.


## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
