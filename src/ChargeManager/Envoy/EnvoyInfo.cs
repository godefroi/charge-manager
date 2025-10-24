using System.Xml;

namespace ChargeManager.Envoy;

public record EnvoyInfo
{
	public required DateTimeOffset Time { get; set; }

	public required Device Device { get; init; }

	public required bool WebTokens { get; init; }

	public required Package[] Packages { get; init; }

	public required DeviceBuildInfo BuildInfo { get; init; }

	public static EnvoyInfo Parse(Stream xmlStream)
	{
		DateTimeOffset time = default;
		Device? device = null;
		bool webTokens = false;
		var packages = new List<Package>();
		DeviceBuildInfo? buildInfo = null;

		using var reader = XmlReader.Create(xmlStream);

		// Move to root element
		reader.MoveToContent();
		if (reader.Name != "envoy_info") {
			throw new InvalidOperationException("Expected 'envoy_info' root element");
		}

		// Read child elements
		while (reader.Read()) {
			if (reader.NodeType == XmlNodeType.Element) {
				switch (reader.Name) {
					case "time":
						var timeStr = reader.ReadElementContentAsString();
						time = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timeStr));
						break;

					case "device":
						device = ParseDevice(reader);
						break;

					case "web-tokens":
						webTokens = reader.ReadElementContentAsBoolean();
						break;

					case "package":
						packages.Add(ParsePackage(reader));
						break;

					case "build_info":
						buildInfo = ParseDeviceBuildInfo(reader);
						break;
				}
			}
		}

		return new() {
			Time      = time,
			Device    = device ?? throw new InvalidOperationException("Device element is required"),
			WebTokens = webTokens,
			Packages  = [.. packages],
			BuildInfo = buildInfo ?? throw new InvalidOperationException("BuildInfo element is required")
		};
	}

	private static Device ParseDevice(XmlReader reader)
	{
		string serial = string.Empty;
		string productNumber = string.Empty;
		string software = string.Empty;
		string euaId = string.Empty;
		int sequenceNumber = 0;
		int apiVersion = 0;
		bool iMeter = false;

		var deviceSubtree = reader.ReadSubtree();
		
		while (deviceSubtree.Read()) {
			if (deviceSubtree.NodeType == XmlNodeType.Element) {
				switch (deviceSubtree.Name) {
					case "sn":
						serial = deviceSubtree.ReadElementContentAsString();
						break;
					case "pn":
						productNumber = deviceSubtree.ReadElementContentAsString();
						break;
					case "software":
						software = deviceSubtree.ReadElementContentAsString();
						break;
					case "euaid":
						euaId = deviceSubtree.ReadElementContentAsString();
						break;
					case "seqnum":
						sequenceNumber = deviceSubtree.ReadElementContentAsInt();
						break;
					case "apiver":
						apiVersion = deviceSubtree.ReadElementContentAsInt();
						break;
					case "imeter":
						iMeter = deviceSubtree.ReadElementContentAsBoolean();
						break;
				}
			}
		}

		deviceSubtree.Close();

		return new() {
			Serial         = serial,
			ProductNumber  = productNumber,
			Software       = software,
			EUAId          = euaId,
			SequenceNumber = sequenceNumber,
			ApiVerion      = apiVersion,
			IMeter         = iMeter
		};
	}

	private static Package ParsePackage(XmlReader reader)
	{
		string name = reader.GetAttribute("name") ?? string.Empty;
		string productNumber = string.Empty;
		string version = string.Empty;
		string build = string.Empty;

		var packageSubtree = reader.ReadSubtree();
		
		while (packageSubtree.Read()) {
			if (packageSubtree.NodeType == XmlNodeType.Element) {
				switch (packageSubtree.Name) {
					case "pn":
						productNumber = packageSubtree.ReadElementContentAsString();
						break;
					case "version":
						version = packageSubtree.ReadElementContentAsString();
						break;
					case "build":
						build = packageSubtree.ReadElementContentAsString();
						break;
				}
			}
		}

		packageSubtree.Close();

		return new() {
			Name          = name,
			ProductNumber = productNumber,
			Version       = version,
			Build         = build
		};
	}

	private static DeviceBuildInfo ParseDeviceBuildInfo(XmlReader reader)
	{
		DateTimeOffset time = default;
		string id = string.Empty;

		var buildInfoSubtree = reader.ReadSubtree();
		
		while (buildInfoSubtree.Read()) {
			if (buildInfoSubtree.NodeType == XmlNodeType.Element) {
				switch (buildInfoSubtree.Name) {
					case "build_time_gmt":
						var timeStr = buildInfoSubtree.ReadElementContentAsString();
						time = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timeStr));
						break;
					case "build_id":
						id = buildInfoSubtree.ReadElementContentAsString();
						break;
				}
			}
		}

		buildInfoSubtree.Close();

		return new() {
			Time = time,
			Id   = id
		};
	}
}

public record Device
{
	public required string Serial { get; init; }

	public required string ProductNumber { get; init; }

	public required string Software { get; init; }

	public required string EUAId { get; init; }

	public required int SequenceNumber { get; init; }

	public required int ApiVerion { get; init; }

	public required bool IMeter { get; init; }
}

public record Package
{
	public required string Name { get; init; }

	public required string ProductNumber { get; init; }

	public required string Version { get; init; }

	public required string Build { get; init; }
}

public record DeviceBuildInfo
{
	public required DateTimeOffset Time { get; set; }

	public required string Id { get; init; }
}
