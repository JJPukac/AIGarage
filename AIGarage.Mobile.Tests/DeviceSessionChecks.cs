using AIGarage.Core.Devices;

namespace AIGarage.Mobile.Tests;

internal static class DeviceSessionChecks
{
    private static DeviceProfile OtherProfile(int index = 2) => TestSession.Profile with
    {
        Id = Guid.NewGuid(), Name = $"Garage {index}",
        Connection = TestSession.Settings with { TopicRoot = $"aigarage/garage{index}" }
    };

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Device session assertion failed.");
    }

    private static async Task RejectAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected command/connection rejection.");
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("MQTT read-only capability blocks direct open despite fresh closed telemetry", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            var profile = TestSession.Profile with { Capabilities = DeviceCapabilities.DoorState | DeviceCapabilities.Temperature };
            await session.Service.ConnectAsync(profile, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry());
            Require(session.Service.IsTelemetryFresh && !session.Service.CanOpen);
            await RejectAsync(() => session.Service.OpenAsync(CancellationToken.None));
            Require(session.Fake.Published.Count == 0);
        });
        await test("MQTT switching A-B-A preserves each device replay watermark", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            var initial = session.Telemetry(); await session.DeliverTelemetryAsync(initial);
            var oldClient = session.Fake;
            var second = OtherProfile();
            await session.Service.ConnectAsync(second, TestSession.Credentials, CancellationToken.None);
            Require(!session.Service.CanOpen && session.Service.Telemetry is null);
            await oldClient.DeliverAsync(session.Message(TestSession.Settings.TelemetryTopic, session.Telemetry(900)));
            await session.DeliverTelemetryAsync(initial); // Correct key but the wrong device topic.
            Require(session.Service.Telemetry is null);
            await session.Fake.DeliverAsync(session.Message(second.Connection.TelemetryTopic, initial));
            Require(session.Service.CanOpen);
            await session.Service.ConnectAsync(TestSession.Profile, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(initial);
            Require(!session.Service.CanOpen);
            await session.DeliverTelemetryAsync(session.Telemetry(2));
            Require(session.Service.CanOpen);
        });
        await test("MQTT retired device session cannot return after another profile", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            await session.DeliverTelemetryAsync(session.Telemetry());
            await session.DeliverTelemetryAsync(session.Telemetry(boot: new string('a', 32)));
            await session.Service.ConnectAsync(OtherProfile(), TestSession.Credentials, CancellationToken.None);
            await session.Service.ConnectAsync(TestSession.Profile, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry(999));
            Require(!session.Service.CanOpen);
            await session.DeliverTelemetryAsync(session.Telemetry(2, boot: new string('a', 32)));
            Require(session.Service.CanOpen);
        });
        await test("MQTT renaming a profile and changing DNS case do not reset replay history", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            await session.DeliverTelemetryAsync(session.Telemetry());
            var renamed = TestSession.Profile with { Name = "Renamed", Connection = TestSession.Settings with { Host = "LOCALHOST" } };
            await session.Service.ConnectAsync(renamed, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry()); Require(!session.Service.CanOpen);
        });
        await test("MQTT transport migration preserves authenticated stream replay history", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            await session.DeliverTelemetryAsync(session.Telemetry());
            var migrated = TestSession.Profile with { Connection = TestSession.Settings with
                { Host = "private.example", Port = 8884, Mode = Core.Configuration.InfrastructureMode.PrivateInfrastructure } };
            await session.Service.ConnectAsync(migrated, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry()); Require(!session.Service.CanOpen);
            await session.DeliverTelemetryAsync(session.Telemetry(2)); Require(session.Service.CanOpen);
        });
        await test("MQTT returning to a previous HMAC key retains its replay watermark", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            await session.DeliverTelemetryAsync(session.Telemetry());
            var rotated = TestSession.Credentials with { HmacKeyHex = new string('a', 64) };
            await session.Service.ConnectAsync(TestSession.Profile, rotated, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry(2)); Require(!session.Service.CanOpen); // Wrong key.
            await session.Service.ConnectAsync(TestSession.Profile, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry()); Require(!session.Service.CanOpen);
            await session.DeliverTelemetryAsync(session.Telemetry(2)); Require(session.Service.CanOpen);
        });
        await test("MQTT replay identity limit fails closed without evicting old protection", async () =>
        {
            await using var session = await TestSession.CreateAsync();
            await session.DeliverTelemetryAsync(session.Telemetry());
            for (int i = 2; i <= 64; i++)
                await session.Service.ConnectAsync(OtherProfile(i), TestSession.Credentials, CancellationToken.None);
            await RejectAsync(() => session.Service.ConnectAsync(OtherProfile(65), TestSession.Credentials, CancellationToken.None));
            Require(!session.Service.IsConnected && session.Clients.All(c => c.Disposed));
            await session.Service.ConnectAsync(TestSession.Profile, TestSession.Credentials, CancellationToken.None);
            await session.DeliverTelemetryAsync(session.Telemetry()); Require(!session.Service.CanOpen);
        });

        await test("VM explicit device switch disconnects and requires new biometric unlock", async () =>
        {
            using ViewModelTestContext context = new();
            var second = OtherProfile(); context.Credentials.Catalog = context.Credentials.Catalog.WithProfile(second).Select(TestSession.Profile.Id);
            // Reload metadata through the same user action as reopening the list.
            await context.ViewModel.LoadProfileCommand.ExecuteAsync(null);
            await context.LoginAsync(); context.Receive();
            int previousConnections = context.Garage.ConnectCount;
            context.ViewModel.SelectedProfileIndex = context.ViewModel.ProfileNames.ToList().IndexOf(second.Name);
            Require(!context.ViewModel.CanOpen);
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            await context.ViewModel.LoadProfileCommand.ExecuteAsync(null);
            context.Receive();
            Require(context.Garage.OpenCount == 0 && context.Garage.ConnectCount == previousConnections && !context.ViewModel.CanOpen);
            Require(context.ViewModel.TopicRoot == second.Connection.TopicRoot && context.ViewModel.Username == "" && context.ViewModel.Password == "");
            await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(context.Garage.ConnectedProfile?.Id == second.Id && context.Biometrics.Count == 1);
        });
        await test("VM new device is an isolated draft and never reuses credentials", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            await context.ViewModel.NewProfileCommand.ExecuteAsync(null);
            Require(!context.ViewModel.CanOpen && !context.ViewModel.CanUnlock && context.ViewModel.ProfileNames.Count == 1);
            Require(context.ViewModel.Host == "" && context.ViewModel.TopicRoot == "" && context.ViewModel.HmacKeyHex == "");
            await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(context.Credentials.LoadCount == 0 && context.Garage.ConnectCount == 1);
        });
        await test("VM read-only profile blocks physical command even if transport permits it", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin(); context.ViewModel.AllowsOpening = false;
            await context.ViewModel.LoginCommand.ExecuteAsync(null); context.Receive();
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(!context.ViewModel.CanOpen && context.Garage.OpenCount == 0 && context.Biometrics.Count == 0);
        });
        await test("VM unsaved profile changes cannot unlock old endpoint credentials", async () =>
        {
            using ViewModelTestContext context = new(); context.ViewModel.Host = "edited.example";
            await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(!context.ViewModel.CanUnlock && context.Credentials.LoadCount == 0 && context.Garage.ConnectCount == 0);
        });
        await test("VM in-flight biometric open blocks profile change and extra connect", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            var biometric = new TaskCompletionSource<bool>();
            context.Biometrics.OnAuthenticate = _ => biometric.Task;
            var opening = context.ViewModel.OpenCommand.ExecuteAsync(null);
            await context.ViewModel.NewProfileCommand.ExecuteAsync(null);
            Require(context.ViewModel.Host == "localhost" && context.Garage.DisconnectCount == 0);
            biometric.SetResult(true); await opening; Require(context.Garage.OpenCount == 1);
        });
        await test("VM background cancellation during profile disconnect cannot apply a new draft", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync();
            var disconnected = new TaskCompletionSource(); context.Garage.OnDisconnect = () => disconnected.Task;
            var changing = context.ViewModel.NewProfileCommand.ExecuteAsync(null);
            var locking = context.ViewModel.LockAsync();
            disconnected.SetResult(); await Task.WhenAll(changing, locking);
            Require(context.ViewModel.Host == "localhost" && !context.ViewModel.CanOpen);
        });
        await test("VM editing active profile programmatically cannot route an open to another device", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.ViewModel.TopicRoot = "aigarage/different";
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(!context.ViewModel.CanOpen && context.Garage.OpenCount == 0);
        });
        await test("VM cleared nullable input bindings fail validation without crashing", async () =>
        {
            using ViewModelTestContext context = new();
            context.ViewModel.ProfileName = null!; context.ViewModel.Host = null!;
            context.ViewModel.TopicRoot = null!; context.ViewModel.Port = null!;
            context.ViewModel.Username = null!; context.ViewModel.Password = null!; context.ViewModel.HmacKeyHex = null!;
            await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Credentials.SaveCount == 0 && context.Garage.ConnectCount == 0 && !context.ViewModel.CanOpen);
        });
        await test("VM changed profile selection during biometrics prevents physical send", async () =>
        {
            using ViewModelTestContext context = new();
            var second = OtherProfile(); context.Credentials.Catalog = context.Credentials.Catalog.WithProfile(second);
            await context.ViewModel.LoadProfileCommand.ExecuteAsync(null);
            await context.LoginAsync(); context.Receive();
            var biometric = new TaskCompletionSource<bool>(); context.Biometrics.OnAuthenticate = _ => biometric.Task;
            var opening = context.ViewModel.OpenCommand.ExecuteAsync(null);
            context.ViewModel.SelectedProfileIndex = context.ViewModel.ProfileNames.ToList().IndexOf(second.Name);
            biometric.SetResult(true); await opening;
            Require(context.Garage.OpenCount == 0 && !context.ViewModel.CanOpen);
        });
    }
}
