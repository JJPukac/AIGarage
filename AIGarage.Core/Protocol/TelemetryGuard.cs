using AIGarage.Core.Models;

namespace AIGarage.Core.Protocol;

// Keeps replay tracking separate from the displayed/current state.
public sealed class TelemetryGuard
{
    public const int MaximumAgeSeconds = 5;
    private const int MaximumRetiredBoots = 64;
    private readonly object _gate = new();
    private readonly HashSet<string> _retiredBoots = new(StringComparer.Ordinal);
    private string? _activeBoot;
    private uint _lastSequence;
    private long _lastTimestamp;
    private GarageTelemetry? _current;
    private readonly TimeProvider _clock;
    private long _receivedTimestamp;

    public TelemetryGuard(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public GarageTelemetry? Current { get { lock (_gate) return _current; } }

    public bool TryAccept(GarageTelemetry telemetry, bool retained, DateTimeOffset now, out string? reason)
    {
        lock (_gate)
        {
            if (retained) return Reject("Retained telemetry is not accepted.", out reason);
            if (!MessageSchema.IsTelemetry(telemetry)) return Reject("Invalid telemetry schema.", out reason);
            if (!IsFresh(telemetry.Timestamp, now)) return Reject("Telemetry is stale or dated in the future.", out reason);
            if (_activeBoot == telemetry.Boot)
            {
                if (telemetry.Sequence <= _lastSequence || telemetry.Timestamp < _lastTimestamp)
                    return Reject("Repeated or out-of-order telemetry.", out reason);
            }
            else if (_activeBoot is not null)
            {
                if (_retiredBoots.Contains(telemetry.Boot) || telemetry.Timestamp < _lastTimestamp)
                    return Reject("Previous boot telemetry cannot become current again.", out reason);
                if (_retiredBoots.Count >= MaximumRetiredBoots)
                    return Reject("Boot history is full. A new trusted application session is required.", out reason);
                _retiredBoots.Add(_activeBoot);
            }

            _activeBoot = telemetry.Boot;
            _lastSequence = telemetry.Sequence;
            _lastTimestamp = telemetry.Timestamp;
            _current = telemetry;
            _receivedTimestamp = _clock.GetTimestamp();
            reason = null;
            return true;
        }
    }

    public bool CanOpen(DateTimeOffset now)
    {
        lock (_gate)
            return _current?.Door == GarageDoorStates.Closed && IsCurrentFresh(now);
    }

    public bool IsCurrentFresh(DateTimeOffset now)
    {
        lock (_gate)
            return _current is not null && IsFresh(_current.Timestamp, now) &&
                _clock.GetElapsedTime(_receivedTimestamp) <= TimeSpan.FromSeconds(MaximumAgeSeconds);
    }

    // Use on disconnect, connection start, or before sending a command.
    // Replay state intentionally survives these operations.
    public void ClearCurrent() { lock (_gate) _current = null; }
    public void BeginConnection() => ClearCurrent();

    public static bool IsFresh(long timestamp, DateTimeOffset now)
    {
        long current = now.ToUnixTimeSeconds();
        return timestamp <= current && timestamp >= current - MaximumAgeSeconds;
    }

    private static bool Reject(string message, out string? reason)
    {
        reason = message;
        return false;
    }
}

public sealed class OpenCommandFactory
{
    private readonly object _gate = new();
    private long _lastSequence;

    public GarageOpenCommand Create(GarageTelemetry telemetry, DateTimeOffset now)
    {
        if (!MessageSchema.IsTelemetry(telemetry) || telemetry.Door != GarageDoorStates.Closed ||
            !TelemetryGuard.IsFresh(telemetry.Timestamp, now))
            throw new InvalidOperationException("Opening requires fresh, confirmed closed telemetry.");
        lock (_gate)
        {
            long sequence = Math.Max(now.ToUnixTimeMilliseconds(), checked(_lastSequence + 1));
            if (sequence <= 0) throw new InvalidOperationException("The device clock is not synchronized.");
            _lastSequence = sequence;
            return new GarageOpenCommand(telemetry.Boot, sequence, "open", now.ToUnixTimeSeconds() + 10);
        }
    }
}
