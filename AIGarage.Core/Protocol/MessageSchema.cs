using System.Text.Json;
using AIGarage.Core.Models;

namespace AIGarage.Core.Protocol;

internal static class MessageSchema
{
    public static bool IsSupported<T>(T value) => value switch
    {
        GarageTelemetry telemetry => IsTelemetry(telemetry),
        GarageOpenCommand command => IsCommand(command),
        GarageCommandAck ack => IsAck(ack),
        _ => false
    };

    public static bool IsLowerHex(string? value, int length) => value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsTelemetry(GarageTelemetry value) => IsLowerHex(value.Boot, 32) && value.Sequence > 0 && value.Timestamp > 0 &&
        value.Door is GarageDoorStates.Closed or GarageDoorStates.NotClosed or GarageDoorStates.MovingEstimated or GarageDoorStates.Unknown &&
        IsFinite(value.TemperatureC) && IsFinite(value.HumidityPct) && IsFinite(value.PressureHpa) &&
        (value.HumidityPct is null || value.HumidityPct is >= 0 and <= 100) &&
        (value.PressureHpa is null || value.PressureHpa > 0);

    public static bool IsCommand(GarageOpenCommand value) => IsLowerHex(value.Boot, 32) && value.Sequence > 0 &&
        value.Action == "open" && value.Expires > 0;

    public static bool IsAck(GarageCommandAck value) => IsLowerHex(value.Boot, 32) && value.Sequence > 0 && value.Timestamp > 0 &&
        value.Result is "pulsed" or "rejected_state" or "rejected_replay" or "rejected_expired";

    private static bool IsFinite(double? value) => value is null || double.IsFinite(value.Value);

    // Duplicate properties, including differently escaped copies of a name, are rejected.
    public static bool HasProperties(JsonElement element, string[] allowed, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> found = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) return false;
        return required.All(found.Contains);
    }

    public static GarageTelemetry? ReadTelemetry(JsonElement element)
    {
        if (!HasProperties(element, ["boot", "seq", "ts", "door", "temperatureC", "humidityPct", "pressureHpa"], ["boot", "seq", "ts", "door"]) ||
            !TryString(element, "boot", out string boot) || !TryString(element, "door", out string door) ||
            !TryUInt(element, "seq", out uint sequence) || !TryLong(element, "ts", out long timestamp) ||
            !TryNullableDouble(element, "temperatureC", out double? temperature) ||
            !TryNullableDouble(element, "humidityPct", out double? humidity) ||
            !TryNullableDouble(element, "pressureHpa", out double? pressure)) return null;
        var telemetry = new GarageTelemetry(boot, sequence, timestamp, door, temperature, humidity, pressure);
        return IsTelemetry(telemetry) ? telemetry : null;
    }

    public static GarageCommandAck? ReadAck(JsonElement element)
    {
        if (!HasProperties(element, ["boot", "seq", "ts", "result"], ["boot", "seq", "ts", "result"]) ||
            !TryString(element, "boot", out string boot) || !TryString(element, "result", out string result) ||
            !TryLong(element, "seq", out long sequence) || !TryLong(element, "ts", out long timestamp)) return null;
        var ack = new GarageCommandAck(boot, sequence, timestamp, result);
        return IsAck(ack) ? ack : null;
    }

    public static GarageOpenCommand? ReadCommand(JsonElement element)
    {
        if (!HasProperties(element, ["boot", "seq", "action", "expires"], ["boot", "seq", "action", "expires"]) ||
            !TryString(element, "boot", out string boot) || !TryString(element, "action", out string action) ||
            !TryLong(element, "seq", out long sequence) || !TryLong(element, "expires", out long expires)) return null;
        var command = new GarageOpenCommand(boot, sequence, action, expires);
        return IsCommand(command) ? command : null;
    }

    private static bool TryString(JsonElement element, string property, out string value)
    {
        JsonElement field = element.GetProperty(property);
        value = field.ValueKind == JsonValueKind.String ? field.GetString()! : "";
        return field.ValueKind == JsonValueKind.String;
    }

    private static bool TryUInt(JsonElement element, string property, out uint value)
    {
        JsonElement field = element.GetProperty(property);
        value = 0;
        return field.ValueKind == JsonValueKind.Number && field.TryGetUInt32(out value);
    }

    private static bool TryLong(JsonElement element, string property, out long value)
    {
        JsonElement field = element.GetProperty(property);
        value = 0;
        return field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out value);
    }

    private static bool TryNullableDouble(JsonElement element, string property, out double? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out JsonElement field) || field.ValueKind == JsonValueKind.Null) return true;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetDouble(out double number) || !double.IsFinite(number)) return false;
        value = number;
        return true;
    }
}
