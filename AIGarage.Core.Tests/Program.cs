using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIGarage.Core.Configuration;
using AIGarage.Core.Models;
using AIGarage.Core.Protocol;

byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
const string topic = "aigarage/garage1/telemetry";
const string boot = "0123456789abcdef0123456789abcdef";
DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
GarageTelemetry closed = new(boot, 1, now.ToUnixTimeSeconds(), GarageDoorStates.Closed);
using SignedMessageCodec codec = new(key);
int passed = 0;
List<string> failures = [];

void Test(string name, Action check)
{
    try { check(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures.Add(name + ": " + exception.Message); Console.WriteLine($"FAIL {name}: {exception.Message}"); }
}

void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}

string SignRaw(string body, string messageTopic = topic)
{
    string mac = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(messageTopic + "\n" + body))).ToLowerInvariant();
    return JsonSerializer.Serialize(new { body, mac });
}

bool Decode(string wire) => codec.TryDecodeTelemetry(topic, Encoding.UTF8.GetBytes(wire), out _, out _);
bool Accept(TelemetryGuard guard, GarageTelemetry telemetry, bool retained = false) => guard.TryAccept(telemetry, retained, now, out _);
string wire = codec.Encode(topic, closed);
string body = JsonDocument.Parse(wire).RootElement.GetProperty("body").GetString()!;

Test("HMAC matches independent Python hashlib fixture", () =>
{
    using JsonDocument envelope = JsonDocument.Parse(wire);
    Assert(envelope.RootElement.GetProperty("mac").GetString() == "a0a787f4b89c9e990bede27a50fa1844ea60d2a8e838aaae3f370d8161c0ae09");
});
Test("telemetry round trip with omitted sensors", () =>
{
    Assert(codec.TryDecodeTelemetry(topic, Encoding.UTF8.GetBytes(wire), out GarageTelemetry? decoded, out _));
    Assert(decoded == closed);
    Assert(!body.Contains("temperatureC", StringComparison.Ordinal));
});
Test("telemetry round trip with BME280 data", () =>
{
    GarageTelemetry sensors = closed with { TemperatureC = 22.5, HumidityPct = 48.5, PressureHpa = 1013.25 };
    Assert(codec.TryDecodeTelemetry(topic, Encoding.UTF8.GetBytes(codec.Encode(topic, sensors)), out GarageTelemetry? decoded, out _));
    Assert(decoded == sensors);
});
Test("tampered body rejected", () => Assert(!Decode(wire.Replace("closed", "not_closed", StringComparison.Ordinal))));
Test("topic substitution rejected", () => Assert(!codec.TryDecodeTelemetry("aigarage/other/telemetry", Encoding.UTF8.GetBytes(wire), out _, out _)));
Test("wrong HMAC key rejected", () =>
{
    using SignedMessageCodec wrong = new(new byte[32]);
    Assert(!wrong.TryDecodeTelemetry(topic, Encoding.UTF8.GetBytes(wire), out _, out _));
});
Test("short HMAC key rejected", () => Throws<ArgumentException>(() => new SignedMessageCodec(new byte[31])));
Test("upper-case MAC rejected", () =>
{
    using JsonDocument envelope = JsonDocument.Parse(wire);
    Assert(!Decode(JsonSerializer.Serialize(new { body, mac = envelope.RootElement.GetProperty("mac").GetString()!.ToUpperInvariant() })));
});
Test("duplicate envelope property rejected", () =>
{
    string duplicate = "{\"body\":" + JsonSerializer.Serialize(body) + ",\"body\":" + JsonSerializer.Serialize(body) + ",\"mac\":\"" + JsonDocument.Parse(wire).RootElement.GetProperty("mac").GetString() + "\"}";
    Assert(!Decode(duplicate));
});
Test("unknown envelope property rejected", () => Assert(!Decode(wire[..^1] + ",\"extra\":1}")));
Test("non-string envelope body rejected", () => Assert(!Decode("{\"body\":{},\"mac\":\"" + new string('a', 64) + "\"}")));
Test("empty envelope rejected", () => Assert(!Decode("")));
Test("oversized envelope rejected", () => Assert(!Decode(new string(' ', SignedMessageCodec.MaximumWireBytes + 1))));
Test("malformed UTF8 rejected", () => Assert(!codec.TryDecodeTelemetry(topic, [0x7b, 0xff, 0x7d], out _, out _)));
Test("correctly signed duplicate body property rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"seq\":2}"))));
Test("escaped duplicate body property rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"s\\u0065q\":2}"))));
Test("correctly signed unknown body property rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"open\":true}"))));
Test("missing required door rejected", () => Assert(!Decode(SignRaw(body.Replace(",\"door\":\"closed\"", "", StringComparison.Ordinal)))));
Test("invalid door name rejected", () => Assert(!Decode(SignRaw(body.Replace("closed", "opened", StringComparison.Ordinal)))));
Test("negative telemetry sequence rejected", () => Assert(!Decode(SignRaw(body.Replace("\"seq\":1", "\"seq\":-1", StringComparison.Ordinal)))));
Test("zero telemetry sequence rejected", () => Assert(!Decode(SignRaw(body.Replace("\"seq\":1", "\"seq\":0", StringComparison.Ordinal)))));
Test("fractional telemetry sequence rejected", () => Assert(!Decode(SignRaw(body.Replace("\"seq\":1", "\"seq\":1.1", StringComparison.Ordinal)))));
Test("unsigned sequence overflow rejected", () => Assert(!Decode(SignRaw(body.Replace("\"seq\":1", "\"seq\":4294967296", StringComparison.Ordinal)))));
Test("upper-case boot nonce rejected", () => Assert(!Decode(SignRaw(body.Replace(boot, boot.ToUpperInvariant(), StringComparison.Ordinal)))));
Test("wrong boot nonce length rejected", () => Assert(!Decode(SignRaw(body.Replace(boot, "abc", StringComparison.Ordinal)))));
Test("non-numeric sensor rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"temperatureC\":\"22\"}"))));
Test("overflowing non-finite sensor rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"temperatureC\":1e999}"))));
Test("impossible humidity rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"humidityPct\":101}"))));
Test("nonpositive pressure rejected", () => Assert(!Decode(SignRaw(body[..^1] + ",\"pressureHpa\":0}"))));
Test("newline topic injection rejected", () => Throws<ArgumentException>(() => codec.Encode(topic + "\nother", closed)));
Test("MQTT wildcard signing rejected", () => Throws<ArgumentException>(() => codec.Encode("aigarage/+/telemetry", closed)));
Test("unsupported model signing rejected", () => Throws<ArgumentException>(() => codec.Encode(topic, new { door = "closed" })));
Test("ACK command identity survives long sequence", () =>
{
    GarageCommandAck ack = new(boot, 1800000000000, now.ToUnixTimeSeconds(), "pulsed");
    string ackWire = codec.Encode("aigarage/garage1/ack", ack);
    Assert(codec.TryDecodeAck("aigarage/garage1/ack", Encoding.UTF8.GetBytes(ackWire), out GarageCommandAck? decoded, out _));
    Assert(ack == decoded);
    Assert(!codec.TryDecodeTelemetry("aigarage/garage1/ack", Encoding.UTF8.GetBytes(ackWire), out _, out _));
});
Test("unknown ACK result rejected", () => Throws<ArgumentException>(() => codec.Encode("aigarage/garage1/ack", new GarageCommandAck(boot, 1, now.ToUnixTimeSeconds(), "opened"))));

Test("fresh closed telemetry permits opening", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed)); Assert(guard.CanOpen(now)); });
Test("retained telemetry cannot enable opening", () => { TelemetryGuard guard = new(); Assert(!Accept(guard, closed, true)); Assert(!guard.CanOpen(now)); });
Test("stale telemetry rejected", () => Assert(!Accept(new(), closed with { Timestamp = now.ToUnixTimeSeconds() - 6 })));
Test("future telemetry rejected", () => Assert(!Accept(new(), closed with { Timestamp = now.ToUnixTimeSeconds() + 1 })));
Test("freshness boundary five seconds accepted", () => Assert(Accept(new(), closed with { Timestamp = now.ToUnixTimeSeconds() - 5 })));
Test("telemetry becomes stale without incoming messages", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed)); Assert(!guard.CanOpen(now.AddSeconds(6))); });
Test("monotonic receive age blocks frozen wall clock", () =>
{
    ManualClock clock = new(); TelemetryGuard guard = new(clock);
    Assert(Accept(guard, closed)); clock.Advance(TimeSpan.FromSeconds(6)); Assert(!guard.CanOpen(now));
});
Test("display freshness uses monotonic receive age for every door state", () =>
{
    ManualClock clock = new(); TelemetryGuard guard = new(clock);
    Assert(Accept(guard, closed with { Door = GarageDoorStates.NotClosed }));
    Assert(guard.IsCurrentFresh(now) && !guard.CanOpen(now));
    clock.Advance(TimeSpan.FromSeconds(6));
    Assert(!guard.IsCurrentFresh(now));
});
Test("wall-clock rollback cannot restore display freshness", () =>
{
    TelemetryGuard guard = new(); Assert(Accept(guard, closed));
    Assert(!guard.IsCurrentFresh(now.AddSeconds(-1)) && !guard.CanOpen(now.AddSeconds(-1)));
    guard.ClearCurrent(); Assert(!guard.IsCurrentFresh(now));
});
Test("duplicate telemetry sequence rejected", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed)); Assert(!Accept(guard, closed)); });
Test("out-of-order telemetry sequence rejected", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed with { Sequence = 3 })); Assert(!Accept(guard, closed with { Sequence = 2 })); });
Test("backwards telemetry timestamp rejected", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed)); Assert(!Accept(guard, closed with { Sequence = 2, Timestamp = now.ToUnixTimeSeconds() - 1 })); });
Test("clearing current state preserves replay watermark", () =>
{
    TelemetryGuard guard = new(); Assert(Accept(guard, closed)); guard.ClearCurrent();
    Assert(!guard.CanOpen(now)); Assert(!Accept(guard, closed)); Assert(guard.Current is null);
    Assert(Accept(guard, closed with { Sequence = 2 }));
});
Test("new connection preserves replay watermark", () =>
{
    TelemetryGuard guard = new(); Assert(Accept(guard, closed)); guard.BeginConnection();
    Assert(!guard.CanOpen(now)); Assert(!Accept(guard, closed));
});
Test("new boot accepts reset sequence", () =>
{
    TelemetryGuard guard = new(); Assert(Accept(guard, closed with { Sequence = 50 }));
    Assert(Accept(guard, closed with { Boot = new string('a', 32) }));
});
Test("previous boot cannot become current again", () =>
{
    TelemetryGuard guard = new(); Assert(Accept(guard, closed)); Assert(Accept(guard, closed with { Boot = new string('a', 32) }));
    guard.BeginConnection(); Assert(!Accept(guard, closed with { Sequence = 500 })); Assert(!guard.CanOpen(now));
});
Test("bounded boot history fails closed", () =>
{
    TelemetryGuard guard = new();
    for (int i = 0; i <= 64; i++) Assert(Accept(guard, closed with { Boot = i.ToString("x32") }));
    guard.ClearCurrent(); Assert(!Accept(guard, closed with { Boot = 65.ToString("x32") })); Assert(!guard.CanOpen(now));
});
foreach (string state in new[] { GarageDoorStates.NotClosed, GarageDoorStates.MovingEstimated, GarageDoorStates.Unknown })
    Test($"door {state} disables opening", () => { TelemetryGuard guard = new(); Assert(Accept(guard, closed with { Door = state })); Assert(!guard.CanOpen(now)); });

Test("open command round trip has ten-second expiry", () =>
{
    GarageOpenCommand command = new OpenCommandFactory().Create(closed, now);
    Assert(command.Action == "open" && command.Expires == now.ToUnixTimeSeconds() + 10 && command.Boot == boot);
    Assert(codec.TryDecodeCommand("aigarage/garage1/command", Encoding.UTF8.GetBytes(codec.Encode("aigarage/garage1/command", command)), out GarageOpenCommand? decoded, out _));
    Assert(decoded == command);
});
Test("open factory rejects stale or unconfirmed state", () =>
{
    OpenCommandFactory factory = new();
    Throws<InvalidOperationException>(() => factory.Create(closed with { Door = GarageDoorStates.NotClosed }, now));
    Throws<InvalidOperationException>(() => factory.Create(closed, now.AddSeconds(6)));
});
Test("open sequence remains monotonic in same millisecond", () =>
{
    OpenCommandFactory factory = new(); GarageOpenCommand first = factory.Create(closed, now); GarageOpenCommand second = factory.Create(closed, now);
    Assert(second.Sequence == first.Sequence + 1);
});
Test("open sequence is unique under concurrent callers", () =>
{
    OpenCommandFactory factory = new(); ConcurrentBag<long> sequences = [];
    Parallel.For(0, 500, _ => sequences.Add(factory.Create(closed, now).Sequence));
    Assert(sequences.Distinct().Count() == 500);
});
Test("close action cannot be signed", () => Throws<ArgumentException>(() => codec.Encode("aigarage/garage1/command", new GarageOpenCommand(boot, 1, "close", now.ToUnixTimeSeconds() + 10))));
Test("disposed codec cannot sign", () => { SignedMessageCodec disposed = new(key); disposed.Dispose(); Throws<ObjectDisposedException>(() => disposed.Encode(topic, closed)); });

Test("cloud and private configuration share topics", () =>
{
    MqttSettings cloud = new() { Host = "example.s1.eu.hivemq.cloud" }; cloud.Validate();
    MqttSettings vpn = cloud with { Mode = InfrastructureMode.PrivateInfrastructure, Host = "garage.example.ts.net" }; vpn.Validate();
    Assert(cloud.CommandTopic == vpn.CommandTopic && cloud.AcknowledgementTopic.EndsWith("/ack", StringComparison.Ordinal));
});
Test("literal private IP configuration accepted", () => new MqttSettings { Host = "100.100.100.100" }.Validate());
foreach (string invalidHost in new[] { "", "mqtts://example.com", "example.com:8883", " example.com", "example.com/", "-invalid.example" })
    Test($"invalid host '{invalidHost}' rejected", () => Throws<ArgumentException>(() => new MqttSettings { Host = invalidHost }.Validate()));
foreach (string invalidTopic in new[] { "", "/aigarage", "aigarage/", "aigarage//garage1", "aigarage/+", "aigarage/#", "aigarage\nother" })
    Test($"invalid topic root '{invalidTopic.Replace("\n", "\\n")}' rejected", () => Throws<ArgumentException>(() => new MqttSettings { Host = "example.com", TopicRoot = invalidTopic }.Validate()));
Test("invalid broker port rejected", () => Throws<ArgumentOutOfRangeException>(() => new MqttSettings { Host = "example.com", Port = 65536 }.Validate()));
Test("unknown mode rejected", () => Throws<ArgumentException>(() => new MqttSettings { Host = "example.com", Mode = (InfrastructureMode)999 }.Validate()));

Console.WriteLine($"\n{passed} passed, {failures.Count} failed.");
if (failures.Count != 0) Environment.ExitCode = 1;

sealed class ManualClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}
