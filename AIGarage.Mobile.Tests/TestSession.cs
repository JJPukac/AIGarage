using AIGarage.Core.Configuration;
using AIGarage.Core.Models;
using AIGarage.Core.Protocol;
using AIGarage.Mobile.Services;
using MQTTnet;

namespace AIGarage.Mobile.Tests;

internal sealed class TestSession : IAsyncDisposable
{
    public static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    public static readonly BrokerCredentials Credentials = new("mobile", "test-password", Convert.ToHexString(Key));
    public static readonly MqttSettings Settings = new() { Host = "localhost" };
    public const string Boot = "0123456789abcdef0123456789abcdef";
    public GarageMqttService Service { get; }
    public FakeMqttClient Fake { get; private set; } = null!;
    public List<FakeMqttClient> Clients { get; } = [];
    public SignedMessageCodec Codec { get; } = new(Key);

    private TestSession()
    {
        Service = new GarageMqttService(() =>
        {
            var created = FakeMqttClient.Create(); Fake = created.Fake; Clients.Add(Fake); return created.Client;
        });
    }

    public static async Task<TestSession> CreateAsync()
    {
        TestSession session = new();
        await session.Service.ConnectAsync(Settings, Credentials, CancellationToken.None);
        return session;
    }

    public GarageTelemetry Telemetry(uint sequence = 1, string door = GarageDoorStates.Closed, long? timestamp = null, string boot = Boot)
        => new(boot, sequence, timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), door, 20, 50, 1013);

    public Task DeliverTelemetryAsync(GarageTelemetry telemetry, bool retained = false) => Fake.DeliverAsync(Message(Settings.TelemetryTopic, telemetry, retained));

    public Task DeliverAckAsync(GarageOpenCommand command, string result = "pulsed", bool retained = false, string? boot = null, long? sequence = null, long? timestamp = null)
    {
        GarageCommandAck ack = new(boot ?? command.Boot, sequence ?? command.Sequence, timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), result);
        return Fake.DeliverAsync(Message(Settings.AcknowledgementTopic, ack, retained));
    }

    public GarageOpenCommand DecodeCommand(MqttApplicationMessage message)
    {
        if (!Codec.TryDecodeCommand(message.Topic, message.PayloadSegment.AsSpan(), out GarageOpenCommand? command, out string? error))
            throw new InvalidOperationException(error);
        return command!;
    }

    public MqttApplicationMessage Message<T>(string topic, T model, bool retained = false) => new MqttApplicationMessageBuilder()
        .WithTopic(topic).WithPayload(Codec.Encode(topic, model)).WithRetainFlag(retained).Build();

    public async ValueTask DisposeAsync()
    {
        await Service.DisconnectAsync();
        Codec.Dispose();
    }
}
