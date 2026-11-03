using System.Text.Json.Serialization;

namespace AIGarage.Core.Models;

public static class GarageDoorStates
{
    public const string Closed = "closed";
    public const string NotClosed = "not_closed";
    public const string MovingEstimated = "moving_estimated";
    public const string Unknown = "unknown";
}

public sealed record GarageTelemetry(
    [property: JsonPropertyName("boot")] string Boot,
    [property: JsonPropertyName("seq")] uint Sequence,
    [property: JsonPropertyName("ts")] long Timestamp,
    [property: JsonPropertyName("door")] string Door,
    [property: JsonPropertyName("temperatureC")] double? TemperatureC = null,
    [property: JsonPropertyName("humidityPct")] double? HumidityPct = null,
    [property: JsonPropertyName("pressureHpa")] double? PressureHpa = null);

public sealed record GarageOpenCommand(
    [property: JsonPropertyName("boot")] string Boot,
    [property: JsonPropertyName("seq")] long Sequence,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("expires")] long Expires);

public sealed record GarageCommandAck(
    [property: JsonPropertyName("boot")] string Boot,
    [property: JsonPropertyName("seq")] long Sequence,
    [property: JsonPropertyName("ts")] long Timestamp,
    [property: JsonPropertyName("result")] string Result);
