using System.Security.Authentication;
using System.Security.Cryptography;
using AIGarage.Core.Configuration;
using AIGarage.Core.Devices;
using AIGarage.Core.Models;
using AIGarage.Core.Protocol;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace AIGarage.Mobile.Services;

public sealed class GarageMqttService : IGarageService
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Func<IMqttClient> _clientFactory;
    private readonly OpenCommandFactory _commands = new();
    private TelemetryGuard _guard = new();
    private IMqttClient? _client;
    private SignedMessageCodec? _codec;
    private MqttSettings? _settings;
    private readonly Dictionary<string, TelemetryGuard> _guards = new(StringComparer.Ordinal);
    private const int MaximumReplayIdentities = 64;
    private DeviceProfile? _profile;
    private PendingCommand? _pending;

    private sealed record PendingCommand(string Boot, long Sequence,
        TaskCompletionSource<GarageCommandAck> Completion);

    public event Action? StateChanged;
    public event Action<string>? ConnectionChanged;

    public GarageMqttService() : this(() => new MqttFactory().CreateMqttClient()) { }
    internal GarageMqttService(Func<IMqttClient> clientFactory) => _clientFactory = clientFactory;

    public bool IsConnected { get { lock (_stateLock) return _client?.IsConnected == true; } }
    public GarageTelemetry? Telemetry { get { lock (_stateLock) return _guard.Current; } }
    public bool IsTelemetryFresh
    {
        get { lock (_stateLock) return _client?.IsConnected == true && _guard.IsCurrentFresh(DateTimeOffset.UtcNow); }
    }
    public bool CanOpen
    {
        get
        {
            lock (_stateLock)
                return _client?.IsConnected == true && _profile?.Supports(DeviceCapabilities.OpenGarage) == true && _pending is null &&
                    _guard.CanOpen(DateTimeOffset.UtcNow);
        }
    }

    public async Task ConnectAsync(DeviceProfile profile, BrokerCredentials credentials, CancellationToken token)
    {
        profile.Validate();
        var settings = profile.Connection;
        byte[] key = credentials.GetKey();
        try
        {
            await _operations.WaitAsync(token);
            try
            {
                await DisconnectInternalAsync();
                // Signatures bind topic + key, not the transport address. Changing broker,
                // DNS alias or port must not reset the same authenticated stream's history.
                string identity = $"{settings.TopicRoot}/{Convert.ToHexString(SHA256.HashData(key))}";
                lock (_stateLock)
                {
                    if (!_guards.TryGetValue(identity, out var guard))
                    {
                        // Eviction would let a previous session's messages become current again.
                        if (_guards.Count >= MaximumReplayIdentities)
                            throw new InvalidOperationException("Dosiahnutý limit identít zariadení. Reštartujte aplikáciu a prihláste sa znova.");
                        _guards.Add(identity, guard = new TelemetryGuard());
                    }
                    _guard = guard;
                    _guard.BeginConnection();
                }
                var client = _clientFactory();
                var codec = new SignedMessageCodec(key);
                lock (_stateLock) { _client = client; _codec = codec; _settings = settings; _profile = profile; }
                client.ApplicationMessageReceivedAsync += args => OnMessageAsync(client, args);
                client.DisconnectedAsync += _ =>
                {
                    lock (_stateLock)
                    {
                        if (!ReferenceEquals(_client, client)) return Task.CompletedTask;
                        _guard.ClearCurrent();
                        _pending?.Completion.TrySetException(new IOException("Pripojenie sa prerušilo. Výsledok príkazu môže byť neznámy."));
                    }
                    StateChanged?.Invoke();
                    ConnectionChanged?.Invoke("Odpojené. Prihláste sa znova.");
                    return Task.CompletedTask;
                };

                var options = new MqttClientOptionsBuilder()
                    .WithClientId($"aigarage-mobile-{Guid.NewGuid():N}")
                    .WithTcpServer(settings.Host, settings.Port)
                    .WithCredentials(credentials.Username, credentials.Password)
                    .WithProtocolVersion(MqttProtocolVersion.V311)
                    .WithCleanSession()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
                    .WithTlsOptions(tls => tls.UseTls().WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13))
                    .Build();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    await client.ConnectAsync(options, timeout.Token);
                    var subscription = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(settings.TelemetryTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce))
                        .WithTopicFilter(f => f.WithTopic(settings.AcknowledgementTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce))
                        .Build();
                    var result = await client.SubscribeAsync(subscription, timeout.Token);
                    if (result.Items.Any(item => (int)item.ResultCode >= 128))
                        throw new InvalidOperationException("Broker nepovolil odber tém. Skontrolujte prístupové práva.");
                }
                catch
                {
                    await DisconnectInternalAsync();
                    throw;
                }
                ConnectionChanged?.Invoke("Pripojené. Čakám na podpísanú telemetriu.");
                StateChanged?.Invoke();
            }
            finally { _operations.Release(); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async Task<string> OpenAsync(CancellationToken token)
    {
        await _operations.WaitAsync(token);
        try
        {
            IMqttClient client;
            MqttApplicationMessage message;
            PendingCommand pending;
            lock (_stateLock)
            {
                if (_client?.IsConnected != true || _codec is null || _settings is null ||
                    _profile?.Supports(DeviceCapabilities.OpenGarage) != true ||
                    _pending is not null || !_guard.CanOpen(DateTimeOffset.UtcNow))
                    throw new InvalidOperationException("Chýba aktuálne potvrdenie zatvorenej brány.");
                var command = _commands.Create(_guard.Current!, DateTimeOffset.UtcNow);
                pending = new PendingCommand(command.Boot, command.Sequence,
                    new TaskCompletionSource<GarageCommandAck>(TaskCreationOptions.RunContinuationsAsynchronously));
                _pending = pending;
                _guard.ClearCurrent(); // Replay history remains intact.
                client = _client;
                message = new MqttApplicationMessageBuilder()
                    .WithTopic(_settings.CommandTopic)
                    .WithPayload(_codec.Encode(_settings.CommandTopic, command))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .WithRetainFlag(false)
                    .Build();
            }
            StateChanged?.Invoke();
            try
            {
                // One publish only. Timeout/disconnection may follow a successful physical pulse.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.PublishAsync(message, timeout.Token);
                var ack = await pending.Completion.Task.WaitAsync(timeout.Token);
                return ack.Result switch
                {
                    "pulsed" => "Zariadenie potvrdilo impulz relé. Sledujte stav brány.",
                    "rejected_state" => "Zariadenie príkaz odmietlo: brána nie je pripravená na otvorenie.",
                    "rejected_replay" => "Zariadenie odmietlo opakovaný príkaz.",
                    "rejected_expired" => "Príkaz vypršal. Skontrolujte čas zariadenia a telefónu.",
                    _ => "Zariadenie príkaz odmietlo."
                };
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or MQTTnet.Exceptions.MqttCommunicationException)
            {
                throw new InvalidOperationException("Potvrdenie neprišlo. Príkaz sa mohol vykonať; skontrolujte bránu. Príkaz sa neopakuje.");
            }
            finally
            {
                lock (_stateLock) _pending = null;
                StateChanged?.Invoke();
            }
        }
        finally { _operations.Release(); }
    }

    private Task OnMessageAsync(IMqttClient source, MqttApplicationMessageReceivedEventArgs args)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!ReferenceEquals(_client, source) || _codec is null || _settings is null ||
                args.ApplicationMessage.Retain) return Task.CompletedTask;
            var message = args.ApplicationMessage;
            if (message.Topic == _settings.TelemetryTopic &&
                _codec.TryDecodeTelemetry(message.Topic, message.PayloadSegment.AsSpan(), out var telemetry, out _) &&
                _guard.TryAccept(telemetry!, false, DateTimeOffset.UtcNow, out _))
                changed = true;
            else if (message.Topic == _settings.AcknowledgementTopic && _pending is not null &&
                _codec.TryDecodeAck(message.Topic, message.PayloadSegment.AsSpan(), out var ack, out _) &&
                ack!.Boot == _pending.Boot && ack.Sequence == _pending.Sequence &&
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ack.Timestamp is >= 0 and <= 10)
                _pending.Completion.TrySetResult(ack);
        }
        if (changed) StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        await _operations.WaitAsync();
        try { await DisconnectInternalAsync(); }
        finally { _operations.Release(); }
        ConnectionChanged?.Invoke("Odpojené.");
        StateChanged?.Invoke();
    }

    private async Task DisconnectInternalAsync()
    {
        IMqttClient? client;
        SignedMessageCodec? codec;
        lock (_stateLock)
        {
            client = _client; codec = _codec;
            _client = null; _codec = null; _settings = null; _profile = null;
            _guard.ClearCurrent();
            _pending?.Completion.TrySetCanceled();
        }
        if (client is not null)
        {
            try
            {
                if (client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await client.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or MQTTnet.Exceptions.MqttCommunicationException)
            { /* The session is already invalidated. Do not retry commands. */ }
            finally { client.Dispose(); }
        }
        codec?.Dispose();
    }
}
