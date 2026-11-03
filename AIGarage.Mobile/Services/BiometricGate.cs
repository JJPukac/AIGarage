namespace AIGarage.Mobile.Services;

public interface IBiometricGate
{
    bool IsSupported { get; }
    Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken);
}

// Desktop/iOS are telemetry development targets in this first Android stage.
public sealed class UnsupportedBiometricGate : IBiometricGate
{
    public bool IsSupported => false;
    public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
