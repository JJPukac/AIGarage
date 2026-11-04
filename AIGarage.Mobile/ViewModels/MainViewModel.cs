using AIGarage.Core.Configuration;
using AIGarage.Core.Devices;
using AIGarage.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIGarage.Mobile.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGarageService _garage;
    private readonly IDeviceProfileStore _profiles;
    private DeviceCatalog _catalog = new();
    private Guid _editingId = Guid.NewGuid();
    private DeviceProfile? _authenticatedProfile;
    private string _profileName = "Garáž 1";
    private int _selectedProfileIndex = -1;
    private IReadOnlyList<string> _profileNames = Array.Empty<string>();
    private DeviceCapabilities _capabilities = DeviceCapabilities.GarageWithEnvironment;
    private readonly IBiometricGate _biometrics;
    private readonly IUserInteraction _interaction;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private CancellationTokenSource _session = new();
    private Task _lockTask = Task.CompletedTask;
    private bool _authenticated;
    private bool _locking;
    private bool _disposed;
    private bool _isBusy;
    private string _host = "", _port = "8883", _topicRoot = "aigarage/garage1";
    private string _username = "", _password = "", _hmacKeyHex = "";
    private int _modeIndex;
    private string _connectionText = "Odpojené. Nastavte pripojenie a prihláste sa.";
    private string _resultText = "", _doorText = "Stav neznámy";
    private string _freshnessText = "Čakám na údaje zo zariadenia.";
    private string _temperatureText = "Teplota: —", _humidityText = "Vlhkosť: —", _pressureText = "Tlak: —";
    private string _biometricText = "";
    private bool _canOpen, _canUnlock;

    public MainViewModel(IGarageService garage, IDeviceProfileStore profiles, IBiometricGate biometrics,
        IUserInteraction interaction, IUiDispatcher dispatcher, TimeProvider clock)
    {
        _garage = garage; _profiles = profiles; _biometrics = biometrics;
        _interaction = interaction; _dispatcher = dispatcher; _clock = clock;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => CanEditProfile);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanUnlock);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => CanOpen);
        LogoutCommand = new AsyncRelayCommand(LockAsync, () => !_disposed && !_locking && (!_isBusy || _garage.IsConnected));
        ForgetCommand = new AsyncRelayCommand(ForgetAsync, () => CanEditSettings && HasSavedProfile);
        LoadProfileCommand = new AsyncRelayCommand(LoadProfileAsync, () => CanEditSettings && SelectedProfile is not null);
        NewProfileCommand = new AsyncRelayCommand(NewProfileAsync, () => CanEditSettings);
        _garage.StateChanged += OnGarageStateChanged;
        _garage.ConnectionChanged += OnConnectionChanged;

        try { ReloadCatalog(); ApplyProfile(_catalog.Devices.FirstOrDefault(p => p.Id == _catalog.SelectedDeviceId)); }
        catch (InvalidOperationException ex) { ResultText = ex.Message; }
        RefreshState();
    }

    public string Host { get => _host; set { if (SetProperty(ref _host, value ?? "")) RefreshState(); } }
    public string Port { get => _port; set { if (SetProperty(ref _port, value ?? "")) RefreshState(); } }
    public string TopicRoot { get => _topicRoot; set { if (SetProperty(ref _topicRoot, value ?? "")) RefreshState(); } }
    public int ModeIndex { get => _modeIndex; set { if (SetProperty(ref _modeIndex, value)) RefreshState(); } }
    public string ProfileName { get => _profileName; set { if (SetProperty(ref _profileName, value ?? "")) RefreshState(); } }
    public IReadOnlyList<string> ProfileNames { get => _profileNames; private set => SetProperty(ref _profileNames, value); }
    public int SelectedProfileIndex { get => _selectedProfileIndex; set { if (SetProperty(ref _selectedProfileIndex, value)) RefreshState(); } }
    private DeviceProfile? SelectedProfile => _selectedProfileIndex >= 0 && _selectedProfileIndex < _catalog.Devices.Count
        ? _catalog.Devices[_selectedProfileIndex] : null;
    private bool HasSavedProfile => _catalog.Devices.Any(p => p.Id == _editingId);
    private bool HasPendingSelection => SelectedProfile is not null && SelectedProfile.Id != _editingId;
    public bool CanEditProfile => CanEditSettings && !_authenticated && !_garage.IsConnected && !HasPendingSelection;
    public bool SupportsDoorState { get => Supports(DeviceCapabilities.DoorState); set => SetCapability(DeviceCapabilities.DoorState, value); }
    public bool AllowsOpening { get => Supports(DeviceCapabilities.OpenGarage); set => SetCapability(DeviceCapabilities.OpenGarage, value); }
    public bool SupportsTemperature { get => Supports(DeviceCapabilities.Temperature); set => SetCapability(DeviceCapabilities.Temperature, value); }
    public bool SupportsHumidity { get => Supports(DeviceCapabilities.Humidity); set => SetCapability(DeviceCapabilities.Humidity, value); }
    public bool SupportsPressure { get => Supports(DeviceCapabilities.Pressure); set => SetCapability(DeviceCapabilities.Pressure, value); }
    public bool ShowsEnvironment => SupportsTemperature || SupportsHumidity || SupportsPressure;
    public string Username { get => _username; set => SetProperty(ref _username, value ?? ""); }
    public string Password { get => _password; set => SetProperty(ref _password, value ?? ""); }
    public string HmacKeyHex { get => _hmacKeyHex; set => SetProperty(ref _hmacKeyHex, value ?? ""); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool CanEditSettings => !IsBusy && !_locking && !_disposed;
    public bool CanOpen { get => _canOpen; private set => SetProperty(ref _canOpen, value); }
    public bool CanUnlock { get => _canUnlock; private set => SetProperty(ref _canUnlock, value); }
    public string ConnectionText { get => _connectionText; private set => SetProperty(ref _connectionText, value); }
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }
    public string DoorText { get => _doorText; private set => SetProperty(ref _doorText, value); }
    public string FreshnessText { get => _freshnessText; private set => SetProperty(ref _freshnessText, value); }
    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }
    public string HumidityText { get => _humidityText; private set => SetProperty(ref _humidityText, value); }
    public string PressureText { get => _pressureText; private set => SetProperty(ref _pressureText, value); }
    public string BiometricText { get => _biometricText; private set => SetProperty(ref _biometricText, value); }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand UnlockCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand ForgetCommand { get; }
    public IAsyncRelayCommand LoadProfileCommand { get; }
    public IAsyncRelayCommand NewProfileCommand { get; }

    private bool Supports(DeviceCapabilities capability) => (_capabilities & capability) == capability;

    private void SetCapability(DeviceCapabilities capability, bool enabled)
    {
        _capabilities = enabled ? _capabilities | capability : _capabilities & ~capability;
        NotifyCapabilities(); RefreshState();
    }

    private void NotifyCapabilities()
    {
        OnPropertyChanged(nameof(SupportsDoorState)); OnPropertyChanged(nameof(AllowsOpening));
        OnPropertyChanged(nameof(SupportsTemperature)); OnPropertyChanged(nameof(SupportsHumidity));
        OnPropertyChanged(nameof(SupportsPressure)); OnPropertyChanged(nameof(ShowsEnvironment));
    }

    private void ReloadCatalog()
    {
        _catalog = _profiles.LoadCatalog();
        ProfileNames = _catalog.Devices.Select(p => p.Name).ToArray();
    }

    private void ApplyProfile(DeviceProfile? profile)
    {
        _editingId = profile?.Id ?? Guid.NewGuid();
        _selectedProfileIndex = profile is null ? -1 : _catalog.Devices.ToList().FindIndex(p => p.Id == profile.Id);
        OnPropertyChanged(nameof(SelectedProfileIndex));
        ProfileName = profile?.Name ?? "Nové zariadenie";
        Host = profile?.Connection.Host ?? ""; Port = (profile?.Connection.Port ?? 8883).ToString();
        TopicRoot = profile?.Connection.TopicRoot ?? ""; ModeIndex = (int)(profile?.Connection.Mode ?? InfrastructureMode.DirectCloud);
        _capabilities = profile?.Capabilities ?? DeviceCapabilities.GarageWithEnvironment;
        NotifyCapabilities(); ClearSecretFields(); RefreshState();
    }

    private DeviceProfile ReadProfile()
    {
        var profile = new DeviceProfile { Id = _editingId, Name = ProfileName.Trim(), Connection = ReadSettings(), Capabilities = _capabilities };
        profile.Validate();
        return profile;
    }

    private DeviceProfile ReadSavedProfile()
    {
        var profile = ReadProfile();
        if (HasPendingSelection || !_catalog.Devices.Contains(profile))
            throw new InvalidOperationException("Najprv načítajte vybraný profil alebo uložte jeho zmeny s prihlasovacími údajmi.");
        return profile;
    }

    private bool EditorMatches(DeviceProfile? profile)
    {
        if (profile is null || HasPendingSelection) return false;
        try { return ReadProfile() == profile; }
        catch (ArgumentException) { return false; }
    }

    private MqttSettings ReadSettings()
    {
        if (!int.TryParse(Port, out int port)) throw new ArgumentException("Zadajte platný TLS port.");
        var settings = new MqttSettings
        {
            Mode = (InfrastructureMode)ModeIndex,
            Host = Host.Trim(), Port = port, TopicRoot = TopicRoot.Trim()
        };
        settings.Validate();
        return settings;
    }

    private Task LoginAsync() => !CanEditProfile ? Task.CompletedTask : RunAsync(async token =>
    {
        var profile = ReadProfile();
        var credentials = new BrokerCredentials(Username.Trim(), Password, HmacKeyHex.Trim());
        await _profiles.SaveAsync(profile, credentials);
        ClearSecretFields();
        token.ThrowIfCancellationRequested();
        ReloadCatalog(); ApplyProfile(profile);
        _authenticated = false;
        _authenticatedProfile = null;
        await _garage.ConnectAsync(profile, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticatedProfile = profile; _authenticated = true;
        ResultText = "Prihlásenie úspešné. Čakám na údaje zariadenia.";
    });

    private Task UnlockAsync() => !CanEditProfile ? Task.CompletedTask : RunAsync(async token =>
    {
        var profile = ReadSavedProfile();
        if (!_biometrics.IsSupported || !await _biometrics.AuthenticateAsync("Prihlásenie do garáže", token))
        {
            ResultText = "Biometrické overenie nebolo úspešné alebo nie je dostupné.";
            return;
        }
        token.ThrowIfCancellationRequested();
        var credentials = await _profiles.LoadCredentialsAsync(profile);
        token.ThrowIfCancellationRequested();
        _authenticated = false;
        _authenticatedProfile = null;
        await _garage.ConnectAsync(profile, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticatedProfile = profile; _authenticated = true;
        ResultText = "Prihlásenie úspešné. Čakám na údaje zariadenia.";
    });

    private Task OpenAsync() => RunAsync(async token =>
    {
        EnsureOpenAllowed();
        if (!await _biometrics.AuthenticateAsync("Potvrďte otvorenie garáže", token))
        {
            ResultText = "Otvorenie nebolo potvrdené.";
            return;
        }
        token.ThrowIfCancellationRequested();
        EnsureOpenAllowed();
        // The MQTT service independently rechecks freshness and device state before publishing.
        ResultText = await _garage.OpenAsync(token);
    });

    private void EnsureOpenAllowed()
    {
        if (!_authenticated || !EditorMatches(_authenticatedProfile) ||
            _authenticatedProfile?.Supports(DeviceCapabilities.OpenGarage) != true || !_biometrics.IsSupported || !_garage.IsConnected ||
            !_garage.IsTelemetryFresh || !_garage.CanOpen)
            throw new InvalidOperationException("Brána nemá aktuálny potvrdený zatvorený stav alebo chýba silná biometria.");
    }

    private Task ForgetAsync() => RunAsync(async token =>
    {
        var profile = _catalog.Devices.FirstOrDefault(p => p.Id == _editingId);
        if (profile is null || !await _interaction.ConfirmForgetCredentialsAsync(profile.Name)) return;
        token.ThrowIfCancellationRequested();
        await LockAsync();
        _profiles.Remove(profile.Id);
        ReloadCatalog(); ApplyProfile(_catalog.Devices.FirstOrDefault(p => p.Id == _catalog.SelectedDeviceId));
        ResultText = "Vybraný profil a jeho prihlasovacie údaje boli odstránené.";
    });

    private Task LoadProfileAsync() => RunAsync(async token =>
    {
        var id = SelectedProfile?.Id ?? throw new InvalidOperationException("Vyberte uložený profil.");
        await DisconnectForProfileChangeAsync(token);
        _profiles.Select(id);
        ReloadCatalog(); ApplyProfile(_catalog.Devices.Single(p => p.Id == id));
        ResultText = "Profil načítaný. Prihláste sa; otvorenie sa nevykonalo.";
    });

    private Task NewProfileAsync() => RunAsync(async token =>
    {
        await DisconnectForProfileChangeAsync(token);
        ReloadCatalog(); ApplyProfile(null);
        ResultText = "Vyplňte nový profil. Uloží sa spolu s prihlasovacími údajmi.";
    });

    private async Task DisconnectForProfileChangeAsync(CancellationToken token)
    {
        _authenticated = false; _authenticatedProfile = null;
        ClearSecretFields(); RefreshState();
        await _garage.DisconnectAsync();
        token.ThrowIfCancellationRequested();
    }

    public Task LockAsync()
    {
        if (_disposed || _locking) return _lockTask;
        _locking = true;
        _authenticated = false;
        _authenticatedProfile = null;
        _session.Cancel();
        ClearSecretFields();
        RefreshState();
        _lockTask = DisconnectAsync();
        return _lockTask;
    }

    private async Task DisconnectAsync()
    {
        try { await _garage.DisconnectAsync(); }
        catch { ResultText = "Aplikácia je uzamknutá. Pripojenie bolo ukončené."; }
        finally { _locking = false; RefreshState(); }
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        // The shared gate serializes different commands as well as repeated taps.
        if (!CanEditSettings) return;
        if (_session.IsCancellationRequested)
        {
            _session.Dispose();
            _session = new CancellationTokenSource();
        }
        IsBusy = true;
        RefreshState();
        try { await operation(_session.Token); }
        catch (OperationCanceledException) { ResultText = "Operácia bola zrušená."; }
        catch (Exception ex)
        {
            ResultText = ex is ArgumentException or InvalidOperationException or IOException
                ? ex.Message
                : "Operácia zlyhala. Skontrolujte pripojenie, certifikát a prihlasovacie údaje.";
        }
        finally { IsBusy = false; RefreshState(); }
    }

    private void ClearSecretFields()
    {
        Username = ""; Password = ""; HmacKeyHex = "";
    }

    public void RefreshState()
    {
        if (_disposed) return;
        bool connected = _authenticated && !_locking && _garage.IsConnected && EditorMatches(_authenticatedProfile);
        var telemetry = connected ? _garage.Telemetry : null;
        bool fresh = connected && telemetry is not null && _garage.IsTelemetryFresh;
        long age = telemetry is null ? 0 : Math.Max(0, _clock.GetUtcNow().ToUnixTimeSeconds() - telemetry.Timestamp);
        ConnectionText = connected
            ? fresh ? "Pripojené." : "Pripojené. Čakám na podpísanú telemetriu."
            : "Odpojené. Prihláste sa alebo nastavte pripojenie.";
        DoorText = !fresh ? "Stav neznámy" : telemetry!.Door switch
        {
            "closed" => "Zatvorené", "not_closed" => "Nie je zatvorené",
            "moving_estimated" => "Pohyb (odhad)", _ => "Stav neznámy"
        };
        FreshnessText = fresh ? $"Údaje pred {age} s" : "Údaje chýbajú alebo už nie sú aktuálne.";
        TemperatureText = fresh && SupportsTemperature && telemetry!.TemperatureC is double t ? $"Teplota: {t:F1} °C" : "Teplota: —";
        HumidityText = fresh && SupportsHumidity && telemetry!.HumidityPct is double h ? $"Vlhkosť: {h:F1} %" : "Vlhkosť: —";
        PressureText = fresh && SupportsPressure && telemetry!.PressureHpa is double p ? $"Tlak: {p:F1} hPa" : "Tlak: —";
        bool biometric = _biometrics.IsSupported;
        CanOpen = CanEditSettings && connected && biometric && AllowsOpening && _garage.IsTelemetryFresh && _garage.CanOpen;
        CanUnlock = CanEditProfile && biometric && EditorMatches(_catalog.Devices.FirstOrDefault(p => p.Id == _editingId));
        BiometricText = biometric ? "Otvorenie vždy vyžaduje biometrické potvrdenie."
            : "Ovládanie vyžaduje Android 11+ a zaregistrovanú silnú biometriu. Tu môžete sledovať telemetriu.";
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanEditProfile));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        LoginCommand.NotifyCanExecuteChanged(); UnlockCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged(); LogoutCommand.NotifyCanExecuteChanged(); ForgetCommand.NotifyCanExecuteChanged();
        LoadProfileCommand.NotifyCanExecuteChanged(); NewProfileCommand.NotifyCanExecuteChanged();
    }

    private void OnGarageStateChanged() => _dispatcher.Dispatch(RefreshState);
    private void OnConnectionChanged(string _) => _dispatcher.Dispatch(RefreshState);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _authenticated = false;
        _session.Cancel();
        _garage.StateChanged -= OnGarageStateChanged;
        _garage.ConnectionChanged -= OnConnectionChanged;
        ClearSecretFields();
        CanOpen = false; CanUnlock = false;
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanEditProfile));
        NotifyCommandStates();
        _session.Dispose();
    }
}
