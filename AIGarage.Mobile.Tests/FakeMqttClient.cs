using System.Reflection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;

namespace AIGarage.Mobile.Tests;

// Implements the MQTT boundary without replacing the real service or protocol code.
public class FakeMqttClient : DispatchProxy
{
    private readonly List<Func<MqttApplicationMessageReceivedEventArgs, Task>> _receivers = [];
    private readonly List<Func<MqttClientDisconnectedEventArgs, Task>> _disconnectHandlers = [];
    public bool Connected { get; private set; }
    public MqttClientOptions? Options { get; private set; }
    public MqttClientSubscribeOptions? Subscription { get; private set; }
    public MqttClientSubscribeResultCode SubscriptionResult { get; set; } = MqttClientSubscribeResultCode.GrantedQoS0;
    public Func<MqttApplicationMessage, CancellationToken, Task>? OnPublish { get; set; }
    public List<MqttApplicationMessage> Published { get; } = [];
    public int ConnectCount { get; private set; }
    public bool Disposed { get; private set; }

    public static (IMqttClient Client, FakeMqttClient Fake) Create()
    {
        IMqttClient client = Create<IMqttClient, FakeMqttClient>();
        return (client, (FakeMqttClient)client);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        object?[] args = arguments ?? [];
        switch (targetMethod!.Name)
        {
            case "get_IsConnected": return Connected;
            case "get_Options": return Options;
            case "add_ApplicationMessageReceivedAsync": _receivers.Add((Func<MqttApplicationMessageReceivedEventArgs, Task>)args[0]!); return null;
            case "remove_ApplicationMessageReceivedAsync": _receivers.Remove((Func<MqttApplicationMessageReceivedEventArgs, Task>)args[0]!); return null;
            case "add_DisconnectedAsync": _disconnectHandlers.Add((Func<MqttClientDisconnectedEventArgs, Task>)args[0]!); return null;
            case "remove_DisconnectedAsync": _disconnectHandlers.Remove((Func<MqttClientDisconnectedEventArgs, Task>)args[0]!); return null;
            case "ConnectAsync":
                Options = (MqttClientOptions)args[0]!; Connected = true; ConnectCount++;
                return Task.FromResult(new MqttClientConnectResult());
            case "SubscribeAsync":
                Subscription = (MqttClientSubscribeOptions)args[0]!;
                return Task.FromResult(new MqttClientSubscribeResult(1,
                    Subscription.TopicFilters.Select(filter => new MqttClientSubscribeResultItem(filter, SubscriptionResult)).ToArray(), string.Empty, []));
            case "PublishAsync": return PublishAsync((MqttApplicationMessage)args[0]!, (CancellationToken)args[1]!);
            case "DisconnectAsync": return TriggerDisconnectAsync();
            case "Dispose": Disposed = true; Connected = false; return null;
            default: throw new NotSupportedException("Unexpected MQTT boundary call: " + targetMethod.Name);
        }
    }

    private async Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage message, CancellationToken token)
    {
        Published.Add(message);
        if (OnPublish is not null) await OnPublish(message, token);
        return new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, string.Empty, []);
    }

    public async Task DeliverAsync(MqttApplicationMessage message)
    {
        var args = new MqttApplicationMessageReceivedEventArgs("test-device", message, new MqttPublishPacket(), (_, _) => Task.CompletedTask);
        foreach (var receiver in _receivers.ToArray()) await receiver(args);
    }

    public async Task TriggerDisconnectAsync()
    {
        bool wasConnected = Connected; Connected = false;
        var args = new MqttClientDisconnectedEventArgs(wasConnected, new MqttClientConnectResult(), (MqttClientDisconnectReason)0, string.Empty, [], new IOException("Test connection interrupted."));
        foreach (var handler in _disconnectHandlers.ToArray()) await handler(args);
    }
}
