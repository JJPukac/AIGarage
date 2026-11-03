using System.Security.Authentication;
using AIGarage.Core.Models;
using AIGarage.Mobile.Services;
using AIGarage.Mobile.Tests;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

int passed = 0;
List<string> failures = [];

void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

async Task<T> ExpectAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T exception) { return exception; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

async Task Test(string name, Func<Task> check)
{
    try { await check(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures.Add(name + ": " + exception.Message); Console.WriteLine("FAIL " + name + ": " + exception); }
}

await Test("connect uses TLS 1.2/1.3 and default certificate validation", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    var tcp = (MqttClientTcpOptions)session.Fake.Options!.ChannelOptions;
    Assert(tcp.TlsOptions.UseTls);
    Assert(tcp.TlsOptions.SslProtocol == (SslProtocols.Tls12 | SslProtocols.Tls13));
    Assert(!tcp.TlsOptions.AllowUntrustedCertificates && !tcp.TlsOptions.IgnoreCertificateChainErrors && !tcp.TlsOptions.IgnoreCertificateRevocationErrors);
    Assert(tcp.TlsOptions.CertificateValidationHandler is null);
    Assert(session.Fake.Options.CleanSession);
    Assert(session.Fake.Subscription!.TopicFilters.Select(f => f.Topic).Order().SequenceEqual(new[] { TestSession.Settings.AcknowledgementTopic, TestSession.Settings.TelemetryTopic }.Order()));
});

await Test("no command before fresh telemetry", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    Assert(!session.Service.CanOpen);
    await ExpectAsync<InvalidOperationException>(() => session.Service.OpenAsync(CancellationToken.None));
    Assert(session.Fake.Published.Count == 0);
});

await Test("retained telemetry does not enable opening", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry(), true);
    Assert(!session.Service.CanOpen && session.Service.Telemetry is null);
});

await Test("stale and future telemetry do not enable opening", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry(timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10));
    await session.DeliverTelemetryAsync(session.Telemetry(timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60));
    Assert(!session.Service.CanOpen && !session.Service.IsTelemetryFresh);
});

await Test("tampered telemetry does not enable opening", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    string wire = session.Codec.Encode(TestSession.Settings.TelemetryTopic, session.Telemetry());
    var message = new MqttApplicationMessageBuilder().WithTopic(TestSession.Settings.TelemetryTopic).WithPayload(wire.Replace("closed", "not_closed", StringComparison.Ordinal)).Build();
    await session.Fake.DeliverAsync(message);
    Assert(!session.Service.CanOpen);
});

foreach (string state in new[] { GarageDoorStates.NotClosed, GarageDoorStates.MovingEstimated, GarageDoorStates.Unknown })
    await Test("door " + state + " prevents opening", async () =>
    {
        await using TestSession session = await TestSession.CreateAsync();
        await session.DeliverTelemetryAsync(session.Telemetry(door: state));
        Assert(!session.Service.CanOpen && session.Service.IsTelemetryFresh);
    });

await Test("matching signed ACK confirms pulse and command is never retained", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry());
    Assert(session.Service.CanOpen);
    session.Fake.OnPublish = async (message, _) =>
    {
        Assert(!session.Service.CanOpen && session.Service.Telemetry is null);
        Assert(message.Topic == TestSession.Settings.CommandTopic && !message.Retain && message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtMostOnce);
        GarageOpenCommand command = session.DecodeCommand(message);
        await session.DeliverAckAsync(command);
    };
    string result = await session.Service.OpenAsync(CancellationToken.None);
    Assert(result.Contains("impulz", StringComparison.Ordinal) && session.Fake.Published.Count == 1 && !session.Service.CanOpen);
});

foreach (string mismatch in new[] { "boot", "sequence", "retained", "stale", "future", "tampered" })
    await Test("ACK " + mismatch + " cannot complete pending command", async () =>
    {
        await using TestSession session = await TestSession.CreateAsync();
        await session.DeliverTelemetryAsync(session.Telemetry());
        TaskCompletionSource<GarageOpenCommand> published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Fake.OnPublish = (message, _) => { published.TrySetResult(session.DecodeCommand(message)); return Task.CompletedTask; };
        Task<string> opening = session.Service.OpenAsync(CancellationToken.None);
        GarageOpenCommand command = await published.Task;
        if (mismatch == "tampered")
        {
            var ack = new GarageCommandAck(command.Boot, command.Sequence, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "pulsed");
            string wire = session.Codec.Encode(TestSession.Settings.AcknowledgementTopic, ack).Replace("pulsed", "rejected_state", StringComparison.Ordinal);
            await session.Fake.DeliverAsync(new MqttApplicationMessageBuilder().WithTopic(TestSession.Settings.AcknowledgementTopic).WithPayload(wire).Build());
        }
        else
            await session.DeliverAckAsync(command,
                retained: mismatch == "retained",
                boot: mismatch == "boot" ? new string('a', 32) : null,
                sequence: mismatch == "sequence" ? command.Sequence + 1 : null,
                timestamp: mismatch == "stale" ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60 : mismatch == "future" ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60 : null);
        Assert(!opening.IsCompleted, "Mismatching ACK completed a command.");
        await session.DeliverAckAsync(command);
        Assert((await opening).Contains("impulz", StringComparison.Ordinal));
        Assert(session.Fake.Published.Count == 1);
    });

foreach (string result in new[] { "rejected_state", "rejected_replay", "rejected_expired" })
    await Test("signed " + result + " ACK reports rejection without retry", async () =>
    {
        await using TestSession session = await TestSession.CreateAsync();
        await session.DeliverTelemetryAsync(session.Telemetry());
        session.Fake.OnPublish = (message, _) => session.DeliverAckAsync(session.DecodeCommand(message), result);
        string response = await session.Service.OpenAsync(CancellationToken.None);
        Assert(!response.Contains("potvrdilo impulz", StringComparison.Ordinal) && session.Fake.Published.Count == 1);
    });

await Test("ambiguous publish IO failure never retries and consumes current telemetry", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    GarageTelemetry telemetry = session.Telemetry();
    await session.DeliverTelemetryAsync(telemetry);
    session.Fake.OnPublish = (_, _) => throw new IOException("Publish may already have reached the device.");
    var error = await ExpectAsync<InvalidOperationException>(() => session.Service.OpenAsync(CancellationToken.None));
    Assert(error.Message.Contains("mohol vykonať", StringComparison.Ordinal) && session.Fake.Published.Count == 1 && !session.Service.CanOpen);
    await session.DeliverTelemetryAsync(telemetry);
    Assert(!session.Service.CanOpen, "Repeated closed telemetry restored opening after an uncertain publish.");
});

await Test("cancellation after publish reports ambiguity without retry", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry());
    using CancellationTokenSource cancellation = new();
    session.Fake.OnPublish = (_, _) => { cancellation.Cancel(); return Task.CompletedTask; };
    await ExpectAsync<InvalidOperationException>(() => session.Service.OpenAsync(cancellation.Token));
    Assert(session.Fake.Published.Count == 1 && !session.Service.CanOpen);
});

await Test("disconnect during publish reports ambiguity without reconnect or retry", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry());
    session.Fake.OnPublish = (_, _) => session.Fake.TriggerDisconnectAsync();
    await ExpectAsync<InvalidOperationException>(() => session.Service.OpenAsync(CancellationToken.None));
    Assert(session.Fake.Published.Count == 1 && session.Fake.ConnectCount == 1 && !session.Service.CanOpen && !session.Service.IsConnected);
});

await Test("telemetry while command pending cannot enable a second command", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    await session.DeliverTelemetryAsync(session.Telemetry());
    session.Fake.OnPublish = async (message, _) =>
    {
        await session.DeliverTelemetryAsync(session.Telemetry(2));
        Assert(!session.Service.CanOpen);
        await session.DeliverAckAsync(session.DecodeCommand(message));
    };
    await session.Service.OpenAsync(CancellationToken.None);
    Assert(session.Fake.Published.Count == 1);
});

await Test("reconnect preserves replay watermark and ignores old client messages", async () =>
{
    await using TestSession session = await TestSession.CreateAsync();
    GarageTelemetry initial = session.Telemetry();
    await session.DeliverTelemetryAsync(initial);
    FakeMqttClient old = session.Fake;
    await session.Service.ConnectAsync(TestSession.Settings, TestSession.Credentials, CancellationToken.None);
    await session.DeliverTelemetryAsync(initial);
    Assert(!session.Service.CanOpen);
    await old.DeliverAsync(session.Message(TestSession.Settings.TelemetryTopic, session.Telemetry(500)));
    Assert(!session.Service.CanOpen);
    await session.DeliverTelemetryAsync(session.Telemetry(boot: new string('a', 32)));
    Assert(session.Service.CanOpen);
    await session.DeliverTelemetryAsync(session.Telemetry(600));
    Assert(session.Service.Telemetry!.Boot == new string('a', 32));
});

await Test("broker denied subscription invalidates and disposes connection", async () =>
{
    var created = FakeMqttClient.Create();
    created.Fake.SubscriptionResult = (MqttClientSubscribeResultCode)128;
    GarageMqttService service = new(() => created.Client);
    await ExpectAsync<InvalidOperationException>(() => service.ConnectAsync(TestSession.Settings, TestSession.Credentials, CancellationToken.None));
    Assert(!service.IsConnected && !service.CanOpen && created.Fake.Disposed);
});

await Test("real loopback TLS rejects fresh untrusted certificate", LocalTlsCheck.VerifyUntrustedCertificateRejectedAsync);

Console.WriteLine($"\n{passed} passed, {failures.Count} failed.");
if (failures.Count != 0) Environment.ExitCode = 1;
