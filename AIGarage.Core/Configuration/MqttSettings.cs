using System.Net;
using System.Text.RegularExpressions;

namespace AIGarage.Core.Configuration;

public enum InfrastructureMode
{
    DirectCloud,
    PrivateInfrastructure
}

public sealed record MqttSettings
{
    public InfrastructureMode Mode { get; init; } = InfrastructureMode.DirectCloud;
    public required string Host { get; init; }
    public int Port { get; init; } = 8883;
    public string TopicRoot { get; init; } = "aigarage/garage1";

    public string CommandTopic => $"{TopicRoot}/command";
    public string TelemetryTopic => $"{TopicRoot}/telemetry";
    public string AcknowledgementTopic => $"{TopicRoot}/ack";

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentException("Unknown infrastructure mode.", nameof(Mode));
        if (string.IsNullOrWhiteSpace(Host) || Host != Host.Trim() ||
            !(IPAddress.TryParse(Host, out _) || IsDnsName(Host)))
            throw new ArgumentException("Use a DNS name or IP address without a scheme, path or port.", nameof(Host));
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port));
        if (string.IsNullOrEmpty(TopicRoot) || TopicRoot.Length > 128 ||
            !Regex.IsMatch(TopicRoot, @"^[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Topic root requires nonempty letters, numbers, hyphens or underscores separated by slashes.", nameof(TopicRoot));
    }

    private static bool IsDnsName(string value)
    {
        if (value.Length > 253) return false;
        return value.Split('.').All(label => label.Length is >= 1 and <= 63 &&
            Regex.IsMatch(label, @"^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$", RegexOptions.CultureInvariant));
    }
}
