using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIGarage.Core.Models;

namespace AIGarage.Core.Protocol;

public sealed class SignedMessageCodec : IDisposable
{
    public const int MaximumWireBytes = 2048;
    public const int MaximumBodyBytes = 1024;
    private readonly object _gate = new();
    private readonly byte[] _key;
    private bool _disposed;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private sealed record Envelope(string body, string mac);

    public SignedMessageCodec(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) throw new ArgumentException("HMAC-SHA256 requires a provisioned 32-byte key.", nameof(key));
        _key = key.ToArray();
    }

    public string Encode<T>(string topic, T message)
    {
        ValidateTopic(topic);
        if (!MessageSchema.IsSupported(message)) throw new ArgumentException("Unsupported or invalid message schema.", nameof(message));
        string body = JsonSerializer.Serialize(message, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes) throw new ArgumentException("Message body is too large.", nameof(message));
        string mac = Convert.ToHexString(Sign(topic, body)).ToLowerInvariant();
        string wire = JsonSerializer.Serialize(new Envelope(body, mac));
        if (Encoding.UTF8.GetByteCount(wire) > MaximumWireBytes) throw new ArgumentException("Envelope is too large.", nameof(message));
        return wire;
    }

    public bool TryDecodeTelemetry(string topic, ReadOnlySpan<byte> payload, out GarageTelemetry? telemetry, out string? error)
        => TryDecode(topic, payload, MessageSchema.ReadTelemetry, out telemetry, out error);

    public bool TryDecodeAck(string topic, ReadOnlySpan<byte> payload, out GarageCommandAck? acknowledgement, out string? error)
        => TryDecode(topic, payload, MessageSchema.ReadAck, out acknowledgement, out error);

    public bool TryDecodeCommand(string topic, ReadOnlySpan<byte> payload, out GarageOpenCommand? command, out string? error)
        => TryDecode(topic, payload, MessageSchema.ReadCommand, out command, out error);

    private bool TryDecode<T>(string topic, ReadOnlySpan<byte> payload, Func<JsonElement, T?> readBody, out T? result, out string? error) where T : class
    {
        result = null;
        error = "Invalid signed message.";
        if (!IsTopicValid(topic) || payload.Length is 0 or > MaximumWireBytes) return false;
        try
        {
            using JsonDocument envelope = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            if (!MessageSchema.HasProperties(envelope.RootElement, ["body", "mac"], ["body", "mac"])) return false;
            JsonElement bodyValue = envelope.RootElement.GetProperty("body");
            JsonElement macValue = envelope.RootElement.GetProperty("mac");
            if (bodyValue.ValueKind != JsonValueKind.String || macValue.ValueKind != JsonValueKind.String) return false;
            string body = bodyValue.GetString()!;
            string mac = macValue.GetString()!;
            if (Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes || !MessageSchema.IsLowerHex(mac, 64)) return false;
            byte[] signature = Convert.FromHexString(mac);
            if (!CryptographicOperations.FixedTimeEquals(Sign(topic, body), signature))
            {
                error = "Message signature is invalid.";
                return false;
            }
            using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 2 });
            result = readBody(document.RootElement);
            if (result is null) return false;
            error = null;
            return true;
        }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
    }

    private byte[] Sign(string topic, string body)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(topic + "\n" + body));
        }
    }

    private static void ValidateTopic(string topic)
    {
        if (!IsTopicValid(topic)) throw new ArgumentException("A concrete MQTT topic without control characters or wildcards is required.", nameof(topic));
    }

    private static bool IsTopicValid(string topic) => !string.IsNullOrEmpty(topic) &&
        Encoding.UTF8.GetByteCount(topic) <= 256 && !topic.Any(c => char.IsControl(c) || c is '+' or '#');

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }
}
