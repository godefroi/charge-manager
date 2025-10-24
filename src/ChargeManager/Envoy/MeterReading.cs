using System.Text.Json.Serialization;

namespace ChargeManager.Envoy;

public record MeterReading(
	/// <summary>
	/// Current active power reading in watts.
	/// </summary>
	[property: JsonPropertyName("currW")]
	double CurrentWatts,

	/// <summary>
	/// Active power (real power) in watts.
	/// </summary>
	[property: JsonPropertyName("actPower")]
	double ActivePower,

	/// <summary>
	/// Apparent power in volt-amperes (VA).
	/// </summary>
	[property: JsonPropertyName("apprntPwr")]
	double ApparentPower,

	/// <summary>
	/// Reactive power in volt-amperes reactive (var).
	/// </summary>
	[property: JsonPropertyName("reactPwr")]
	double ReactivePower,

	/// <summary>
	/// Cumulative watt-hours delivered (Wh).
	/// </summary>
	[property: JsonPropertyName("whDlvdCum")]
	double CumulativeDeliveredWh,

	/// <summary>
	/// Cumulative watt-hours received (Wh).
	/// </summary>
	[property: JsonPropertyName("whRcvdCum")]
	double CumulativeReceivedWh,

	/// <summary>
	/// Cumulative lagging var-hours (varh).
	/// </summary>
	[property: JsonPropertyName("varhLagCum")]
	double CumulativeLaggingVarh,

	/// <summary>
	/// Cumulative leading var-hours (varh).
	/// </summary>
	[property: JsonPropertyName("varhLeadCum")]
	double CumulativeLeadingVarh,

	/// <summary>
	/// Cumulative volt-ampere hours (VAh).
	/// </summary>
	[property: JsonPropertyName("vahCum")]
	double CumulativeVah,

	/// <summary>
	/// Root mean square voltage (V).
	/// </summary>
	[property: JsonPropertyName("rmsVoltage")]
	double RmsVoltage,

	/// <summary>
	/// Root mean square current (A).
	/// </summary>
	[property: JsonPropertyName("rmsCurrent")]
	double RmsCurrent,

	/// <summary>
	/// Power factor (unitless).
	/// </summary>
	[property: JsonPropertyName("pwrFactor")]
	double PowerFactor,

	/// <summary>
	/// Frequency in hertz (Hz).
	/// </summary>
	[property: JsonPropertyName("freqHz")]
	double FrequencyHz
);
