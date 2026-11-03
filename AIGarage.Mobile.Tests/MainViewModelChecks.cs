namespace AIGarage.Mobile.Tests;

internal static class MainViewModelChecks
{
    private static void Require(bool condition, string message = "ViewModel safety assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("VM starts locked even when service has closed telemetry", () =>
        {
            using ViewModelTestContext context = new();
            context.Garage.IsConnected = true; context.Receive();
            Require(!context.ViewModel.CanOpen && context.ViewModel.DoorText == "Stav neznámy");
            return Task.CompletedTask;
        });

        await test("VM invalid configuration cannot save or connect", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin();
            context.ViewModel.Port = "not-a-port";
            await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Credentials.SaveCount == 0 && context.Garage.ConnectCount == 0);
            Require(!context.ViewModel.IsBusy && context.ViewModel.CanEditSettings);
        });

        await test("VM login preserves profile and clears credential fields", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin();
            context.ViewModel.ModeIndex = 1; context.ViewModel.TopicRoot = "home/garage2";
            await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Credentials.Settings!.Mode == Core.Configuration.InfrastructureMode.PrivateInfrastructure);
            Require(context.Credentials.Settings.TopicRoot == "home/garage2");
            Require(context.Garage.ConnectCount == 1 && context.ViewModel.Username == "" && context.ViewModel.Password == "" && context.ViewModel.HmacKeyHex == "");
            Require(!context.ViewModel.CanOpen, "Login alone enabled a physical command.");
        });

        foreach (string door in new[] { "not_closed", "moving_estimated", "unknown" })
            await test("VM " + door + " never enables opening", async () =>
            {
                using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive(door);
                await context.ViewModel.OpenCommand.ExecuteAsync(null);
                Require(!context.ViewModel.CanOpen && context.Garage.OpenCount == 0 && context.Biometrics.Count == 0);
            });

        await test("VM fresh closed state permits one biometric-confirmed open", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            Require(context.ViewModel.CanOpen);
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(context.Biometrics.Count == 1 && context.Garage.OpenCount == 1);
        });

        await test("VM unsupported biometrics prevent direct command execution", async () =>
        {
            using ViewModelTestContext context = new(); context.Biometrics.IsSupported = false;
            await context.LoginAsync(); context.Receive();
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(!context.ViewModel.CanOpen && !context.ViewModel.CanUnlock && context.Garage.OpenCount == 0 && context.Credentials.LoadCount == 0);
        });

        await test("VM rejected biometrics never send an open", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.Biometrics.Result = false;
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(context.Biometrics.Count == 1 && context.Garage.OpenCount == 0);
        });

        await test("VM telemetry expiry during biometrics prevents send", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.Biometrics.OnAuthenticate = _ => { context.Garage.IsTelemetryFresh = false; return Task.FromResult(true); };
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(context.Garage.OpenCount == 0 && !context.ViewModel.CanOpen);
        });

        await test("VM background lock cancels biometric open and blocks late success", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            var biometric = new TaskCompletionSource<bool>();
            CancellationToken captured = default;
            context.Biometrics.OnAuthenticate = token => { captured = token; return biometric.Task; };
            Task opening = context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(context.ViewModel.IsBusy && !context.ViewModel.CanOpen);
            await context.ViewModel.LockAsync();
            Require(captured.IsCancellationRequested);
            biometric.SetResult(true); await opening;
            Require(context.Garage.OpenCount == 0 && !context.ViewModel.CanOpen);
            Require(context.ViewModel.DoorText == "Stav neznámy");
        });

        await test("VM background lock during credential save prevents connect", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin();
            var saved = new TaskCompletionSource(); context.Credentials.OnSave = () => saved.Task;
            Task login = context.ViewModel.LoginCommand.ExecuteAsync(null);
            await context.ViewModel.LockAsync(); saved.SetResult(); await login;
            Require(context.Garage.ConnectCount == 0 && context.ViewModel.Password == "" && context.ViewModel.HmacKeyHex == "");
        });

        await test("VM late connect after background cannot restore authentication", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin();
            var connected = new TaskCompletionSource(); context.Garage.OnConnect = _ => connected.Task;
            Task login = context.ViewModel.LoginCommand.ExecuteAsync(null);
            await context.ViewModel.LockAsync(); connected.SetResult(); await login; context.Receive();
            Require(!context.ViewModel.CanOpen && context.ViewModel.DoorText == "Stav neznámy");
        });

        await test("VM disconnect in progress blocks new login", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync();
            var disconnected = new TaskCompletionSource(); context.Garage.OnDisconnect = () => disconnected.Task;
            Task locking = context.ViewModel.LockAsync();
            context.FillLogin(); await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Garage.ConnectCount == 1 && !context.ViewModel.CanEditSettings);
            disconnected.SetResult(); await locking;
            Require(context.ViewModel.CanEditSettings);
        });

        await test("VM concurrent background locks share one disconnect", async () =>
        {
            using ViewModelTestContext context = new();
            var disconnected = new TaskCompletionSource(); context.Garage.OnDisconnect = () => disconnected.Task;
            Task first = context.ViewModel.LockAsync(), second = context.ViewModel.LockAsync();
            Require(ReferenceEquals(first, second) && context.Garage.DisconnectCount == 1);
            disconnected.SetResult(); await Task.WhenAll(first, second);
        });

        await test("VM busy command blocks different commands and repeated taps", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            var confirmed = new TaskCompletionSource<bool>();
            context.Biometrics.OnAuthenticate = _ => confirmed.Task;
            Task first = context.ViewModel.OpenCommand.ExecuteAsync(null);
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Biometrics.Count == 1 && context.Garage.ConnectCount == 1 && !context.ViewModel.CanEditSettings);
            confirmed.SetResult(true); await first;
            Require(context.Garage.OpenCount == 1);
        });

        await test("VM uncertain open is reported once without retry", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.Garage.OnOpen = _ => throw new InvalidOperationException("Príkaz sa mohol vykonať; skontrolujte bránu.");
            await context.ViewModel.OpenCommand.ExecuteAsync(null);
            Require(context.Garage.OpenCount == 1 && context.ViewModel.ResultText.Contains("mohol vykonať", StringComparison.Ordinal));
        });

        await test("VM stored credentials are unread until biometrics succeeds", async () =>
        {
            using ViewModelTestContext context = new(); context.Biometrics.Result = false;
            await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(context.Credentials.LoadCount == 0 && context.Garage.ConnectCount == 0);
            context.Biometrics.Result = true; await context.ViewModel.UnlockCommand.ExecuteAsync(null);
            Require(context.Credentials.LoadCount == 1 && context.Garage.ConnectCount == 1);
        });

        await test("VM fresh closed telemetry after logout cannot bypass unlock", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            await context.ViewModel.LockAsync();
            context.Garage.IsConnected = true; context.Receive();
            Require(!context.ViewModel.CanOpen && context.ViewModel.DoorText == "Stav neznámy");
        });

        await test("VM status and readings clear when service marks data stale", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.Clock.Now = context.Clock.Now.AddSeconds(20);
            context.Garage.IsTelemetryFresh = false; context.ViewModel.RefreshState();
            Require(!context.ViewModel.CanOpen && context.ViewModel.DoorText == "Stav neznámy");
            Require(context.ViewModel.TemperatureText == "Teplota: —" && context.ViewModel.HumidityText == "Vlhkosť: —" && context.ViewModel.PressureText == "Tlak: —");
        });

        await test("VM service freshness stays authoritative after clock rollback", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            context.Garage.IsTelemetryFresh = false; context.Clock.Now = context.Clock.Now.AddSeconds(-10);
            context.ViewModel.RefreshState();
            Require(!context.ViewModel.CanOpen && context.ViewModel.DoorText == "Stav neznámy");
        });

        await test("VM cancelled deletion retains saved credentials", async () =>
        {
            using ViewModelTestContext context = new(); context.Interaction.Confirm = false;
            await context.ViewModel.ForgetCommand.ExecuteAsync(null);
            Require(context.Credentials.ForgetCount == 0 && context.Garage.DisconnectCount == 0);
        });

        await test("VM confirmed deletion locks and removes saved credentials", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync(); context.Receive();
            await context.ViewModel.ForgetCommand.ExecuteAsync(null);
            Require(context.Credentials.ForgetCount == 1 && context.Garage.DisconnectCount == 1);
            Require(!context.ViewModel.CanOpen && context.Credentials.SavedCredentials is null);
        });

        await test("VM background during deletion dialog cancels late confirmation", async () =>
        {
            using ViewModelTestContext context = new();
            var confirmed = new TaskCompletionSource<bool>(); context.Interaction.OnConfirm = () => confirmed.Task;
            Task forgetting = context.ViewModel.ForgetCommand.ExecuteAsync(null);
            await context.ViewModel.LockAsync(); confirmed.SetResult(true); await forgetting;
            Require(context.Credentials.ForgetCount == 0);
        });

        await test("VM service events reach screen state through UI dispatcher", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync();
            context.Dispatcher.Defer = true; context.Receive();
            Require(!context.ViewModel.CanOpen);
            context.Dispatcher.Drain();
            Require(context.ViewModel.CanOpen && context.Dispatcher.DispatchCount > 0);
        });

        await test("VM disposal removes subscriptions and ignores queued state updates", async () =>
        {
            using ViewModelTestContext context = new(); await context.LoginAsync();
            context.Receive(); Require(context.ViewModel.CanOpen);
            context.Dispatcher.Defer = true; context.Receive();
            context.ViewModel.Dispose(); context.Dispatcher.Drain();
            Require(context.Garage.Subscribers == 0 && !context.ViewModel.CanOpen && !context.ViewModel.LoginCommand.CanExecute(null) && !context.ViewModel.LogoutCommand.CanExecute(null));
            context.FillLogin(); await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(context.Garage.ConnectCount == 1);
        });

        await test("VM transport errors do not expose raw exception details", async () =>
        {
            using ViewModelTestContext context = new(); context.FillLogin();
            const string sensitive = "sensitive-transport-detail";
            context.Garage.OnConnect = _ => throw new Exception(sensitive);
            await context.ViewModel.LoginCommand.ExecuteAsync(null);
            Require(!context.ViewModel.ResultText.Contains(sensitive, StringComparison.Ordinal) && !context.ViewModel.IsBusy);
        });
    }
}
