using AIGarage.Core.Configuration;
using AIGarage.Core.Models;

namespace AIGarage.Mobile.Services;

// Screen-facing operations. Physical command validation remains in the implementation and device.
public interface IGarageService
{
    event Action? StateChanged;
    event Action<string>? ConnectionChanged;
    bool IsConnected { get; }
    GarageTelemetry? Telemetry { get; }
    bool IsTelemetryFresh { get; }
    bool CanOpen { get; }
    Task ConnectAsync(MqttSettings settings, BrokerCredentials credentials, CancellationToken token);
    Task<string> OpenAsync(CancellationToken token);
    Task DisconnectAsync();
}
