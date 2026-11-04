using System.Text.Json;
using AIGarage.Core.Configuration;
using AIGarage.Core.Devices;

internal static class DeviceCatalogChecks
{
    private static DeviceProfile Profile(int number) => new()
    {
        Id = new Guid(number, 0, 0, new byte[8]), Name = $"Device {number}",
        Connection = new MqttSettings { Host = "broker.example", TopicRoot = $"aigarage/device{number}" }
    };

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Catalog assertion failed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid catalog was accepted.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("catalog starts empty without an invented endpoint", () => new DeviceCatalog().Validate());
        test("profiles survive JSON round trip with independent routes and functions", () =>
        {
            var second = Profile(2) with { Capabilities = DeviceCapabilities.Temperature };
            var catalog = new DeviceCatalog().WithProfile(Profile(1)).WithProfile(second);
            var restored = JsonSerializer.Deserialize<DeviceCatalog>(JsonSerializer.Serialize(catalog))!;
            restored.Validate();
            Require(restored.SelectedDeviceId == second.Id && restored.Devices.SequenceEqual(catalog.Devices));
        });
        test("profile rename and endpoint edit retain stable device ID", () =>
        {
            var original = Profile(1);
            var edited = original with { Name = "Home", Connection = original.Connection with { Host = "private.example" } };
            var catalog = new DeviceCatalog().WithProfile(original).WithProfile(edited);
            Require(catalog.Devices.Count == 1 && catalog.Devices.Single() == edited && catalog.SelectedDeviceId == original.Id);
        });
        foreach (string name in new[] { "", " ", " leading", "new\nline", new string('x', 65) })
            test("invalid device name rejected: " + name.Length, () => Reject(() => (Profile(1) with { Name = name }).Validate()));
        test("empty device ID rejected", () => Reject(() => (Profile(1) with { Id = Guid.Empty }).Validate()));
        test("unknown functions fail closed", () => Reject(() => (Profile(1) with { Capabilities = (DeviceCapabilities)128 }).Validate()));
        test("opening without door feedback rejected", () => Reject(() => (Profile(1) with { Capabilities = DeviceCapabilities.OpenGarage }).Validate()));
        test("profile without any functions rejected", () => Reject(() => (Profile(1) with { Capabilities = DeviceCapabilities.None }).Validate()));
        test("temperature-only profile is valid and cannot advertise opening", () =>
        {
            var profile = Profile(1) with { Capabilities = DeviceCapabilities.Temperature };
            profile.Validate(); Require(!profile.Supports(DeviceCapabilities.OpenGarage));
        });
        test("duplicate IDs in stored catalog rejected", () => Reject(() => new DeviceCatalog { Devices = new[] { Profile(1), Profile(1) }, SelectedDeviceId = Profile(1).Id }.Validate()));
        test("duplicate names are case insensitive", () => Reject(() => new DeviceCatalog().WithProfile(Profile(1)).WithProfile(Profile(2) with { Name = "DEVICE 1" })));
        test("same broker and topic cannot masquerade as a second device", () => Reject(() => new DeviceCatalog().WithProfile(Profile(1)).WithProfile(Profile(2) with { Connection = Profile(1).Connection with { Host = "BROKER.EXAMPLE" } })));
        test("topic names remain case sensitive", () =>
        {
            var catalog = new DeviceCatalog().WithProfile(Profile(1)).WithProfile(Profile(2) with { Connection = Profile(1).Connection with { TopicRoot = "AIGarage/device1" } });
            Require(catalog.Devices.Count == 2);
        });
        test("catalog size bound preserves existing devices", () =>
        {
            var catalog = new DeviceCatalog();
            for (int i = 1; i <= DeviceCatalog.MaximumDevices; i++) catalog = catalog.WithProfile(Profile(i));
            Reject(() => catalog.WithProfile(Profile(DeviceCatalog.MaximumDevices + 1)));
            Require(catalog.Devices.Count == DeviceCatalog.MaximumDevices);
        });
        test("newer catalog version rejected instead of overwritten", () => Reject(() => new DeviceCatalog { Version = 2 }.Validate()));
        test("selection must name a saved device", () => Reject(() => new DeviceCatalog().Select(Profile(1).Id)));
        test("deleting selected device retains other profiles and selection", () =>
        {
            var catalog = new DeviceCatalog().WithProfile(Profile(1)).WithProfile(Profile(2)).Without(Profile(2).Id);
            catalog.Validate(); Require(catalog.SelectedDeviceId == Profile(1).Id && catalog.Devices.Single() == Profile(1));
            catalog = catalog.Without(Profile(1).Id); catalog.Validate(); Require(catalog.SelectedDeviceId is null);
        });
    }
}
