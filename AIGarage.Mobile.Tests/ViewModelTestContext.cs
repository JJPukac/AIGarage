using AIGarage.Core.Configuration;
using AIGarage.Core.Devices;
using AIGarage.Core.Models;
using AIGarage.Mobile.Services;
using AIGarage.Mobile.ViewModels;

namespace AIGarage.Mobile.Tests;

internal sealed class ViewModelTestContext : IDisposable
{
    public TestClock Clock { get; } = new();
    public FakeGarage Garage { get; } = new();
    public FakeCredentials Credentials { get; } = new();
    public FakeBiometrics Biometrics { get; } = new();
    public FakeInteraction Interaction { get; } = new();
    public TestDispatcher Dispatcher { get; } = new();
    public MainViewModel ViewModel { get; }

    public ViewModelTestContext()
    {
        ViewModel = new MainViewModel(Garage, Credentials, Biometrics, Interaction, Dispatcher, Clock);
    }

    public void FillLogin()
    {
        ViewModel.Host = "localhost"; ViewModel.Username = TestSession.Credentials.Username;
        ViewModel.Password = TestSession.Credentials.Password;
        ViewModel.HmacKeyHex = TestSession.Credentials.HmacKeyHex;
    }

    public async Task LoginAsync()
    {
        FillLogin();
        await ViewModel.LoginCommand.ExecuteAsync(null);
    }

    public void Receive(string door = "closed", bool fresh = true)
    {
        Garage.Telemetry = new GarageTelemetry(TestSession.Boot, 1, Clock.GetUtcNow().ToUnixTimeSeconds(), door, 20, 50, 1013);
        Garage.IsTelemetryFresh = fresh;
        Garage.Notify();
    }

    public void Dispose() => ViewModel.Dispose();

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class FakeGarage : IGarageService
    {
        public event Action? StateChanged;
        public event Action<string>? ConnectionChanged;
        public bool IsConnected { get; set; }
        public GarageTelemetry? Telemetry { get; set; }
        public bool IsTelemetryFresh { get; set; }
        public bool CanOpen => IsConnected && IsTelemetryFresh && Telemetry?.Door == "closed";
        public int ConnectCount { get; private set; }
        public int OpenCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public int Subscribers => StateChanged?.GetInvocationList().Length ?? 0;
        public Func<CancellationToken, Task>? OnConnect { get; set; }
        public Func<CancellationToken, Task<string>>? OnOpen { get; set; }
        public Func<Task>? OnDisconnect { get; set; }
        public DeviceProfile? ConnectedProfile { get; private set; }

        public async Task ConnectAsync(DeviceProfile profile, BrokerCredentials credentials, CancellationToken token)
        {
            ConnectCount++;
            if (OnConnect is not null) await OnConnect(token);
            ConnectedProfile = profile;
            IsConnected = true;
            ConnectionChanged?.Invoke("Connected");
            Notify();
        }

        public Task<string> OpenAsync(CancellationToken token)
        {
            if (!CanOpen) throw new InvalidOperationException("State no longer permits opening.");
            OpenCount++;
            return OnOpen?.Invoke(token) ?? Task.FromResult("Pulse confirmed.");
        }

        public async Task DisconnectAsync()
        {
            DisconnectCount++;
            if (OnDisconnect is not null) await OnDisconnect();
            IsConnected = false;
            Telemetry = null;
            IsTelemetryFresh = false;
            ConnectionChanged?.Invoke("Disconnected");
            Notify();
        }

        public void Notify() => StateChanged?.Invoke();
    }

    internal sealed class FakeCredentials : IDeviceProfileStore
    {
        public DeviceCatalog Catalog { get; set; } = new() { Devices = new[] { TestSession.Profile }, SelectedDeviceId = TestSession.Profile.Id };
        public MqttSettings? Settings => Catalog.Devices.FirstOrDefault()?.Connection;
        public BrokerCredentials? SavedCredentials { get; private set; } = TestSession.Credentials;
        public int SaveCount { get; private set; }
        public int LoadCount { get; private set; }
        public int ForgetCount { get; private set; }
        public Func<Task>? OnSave { get; set; }
        public DeviceCatalog LoadCatalog() => Catalog;
        public async Task SaveAsync(DeviceProfile profile, BrokerCredentials credentials)
        {
            profile.Validate();
            var key = credentials.GetKey();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            SaveCount++;
            if (OnSave is not null) await OnSave();
            Catalog = Catalog.WithProfile(profile); SavedCredentials = credentials;
        }
        public Task<BrokerCredentials> LoadCredentialsAsync(DeviceProfile profile)
        {
            LoadCount++;
            return Task.FromResult(SavedCredentials ?? throw new InvalidOperationException("No saved credentials."));
        }
        public void Select(Guid id) => Catalog = Catalog.Select(id);
        public void Remove(Guid id) { ForgetCount++; Catalog = Catalog.Without(id); SavedCredentials = null; }
    }

    internal sealed class FakeBiometrics : IBiometricGate
    {
        public bool IsSupported { get; set; } = true;
        public bool Result { get; set; } = true;
        public int Count { get; private set; }
        public Func<CancellationToken, Task<bool>>? OnAuthenticate { get; set; }
        public Task<bool> AuthenticateAsync(string reason, CancellationToken token)
        {
            Count++;
            return OnAuthenticate?.Invoke(token) ?? Task.FromResult(Result);
        }
    }

    internal sealed class FakeInteraction : IUserInteraction
    {
        public bool Confirm { get; set; } = true;
        public Func<Task<bool>>? OnConfirm { get; set; }
        public Task<bool> ConfirmForgetCredentialsAsync(string profileName) => OnConfirm?.Invoke() ?? Task.FromResult(Confirm);
    }

    internal sealed class TestDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();
        public bool Defer { get; set; }
        public int DispatchCount { get; private set; }
        public void Dispatch(Action action)
        {
            DispatchCount++;
            if (Defer) _queue.Enqueue(action); else action();
        }
        public void Drain() { while (_queue.TryDequeue(out var action)) action(); }
    }
}
