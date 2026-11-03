using AIGarage.Core.Configuration;
using AIGarage.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIGarage.Mobile.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGarageService _garage;
    private readonly ICredentialStore _credentials;
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

    public MainViewModel(IGarageService garage, ICredentialStore credentials, IBiometricGate biometrics,
        IUserInteraction interaction, IUiDispatcher dispatcher, TimeProvider clock)
    {
        _garage = garage; _credentials = credentials; _biometrics = biometrics;
        _interaction = interaction; _dispatcher = dispatcher; _clock = clock;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => CanEditSettings);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanUnlock);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => CanOpen);
        LogoutCommand = new AsyncRelayCommand(LockAsync, () => !_disposed && !_locking && (!_isBusy || _garage.IsConnected));
        ForgetCommand = new AsyncRelayCommand(ForgetAsync, () => CanEditSettings);
        _garage.StateChanged += OnGarageStateChanged;
        _garage.ConnectionChanged += OnConnectionChanged;

        var settings = _credentials.LoadSettings();
        if (settings is not null)
        {
            Host = settings.Host; Port = settings.Port.ToString(); TopicRoot = settings.TopicRoot;
            ModeIndex = (int)settings.Mode;
        }
        RefreshState();
    }

    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public string Port { get => _port; set => SetProperty(ref _port, value); }
    public string TopicRoot { get => _topicRoot; set => SetProperty(ref _topicRoot, value); }
    public int ModeIndex { get => _modeIndex; set => SetProperty(ref _modeIndex, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string HmacKeyHex { get => _hmacKeyHex; set => SetProperty(ref _hmacKeyHex, value); }
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

    private MqttSettings ReadSettings()
    {
        if (!int.TryParse(Port, out int port)) throw new ArgumentException("Zadajte platný TLS port.");
        var settings = new MqttSettings
        {
            Mode = ModeIndex == 1 ? InfrastructureMode.PrivateInfrastructure : InfrastructureMode.DirectCloud,
            Host = Host.Trim(), Port = port, TopicRoot = TopicRoot.Trim()
        };
        settings.Validate();
        return settings;
    }

    private Task LoginAsync() => RunAsync(async token =>
    {
        var settings = ReadSettings();
        var credentials = new BrokerCredentials(Username.Trim(), Password, HmacKeyHex.Trim());
        await _credentials.SaveAsync(settings, credentials);
        ClearSecretFields();
        token.ThrowIfCancellationRequested();
        _authenticated = false;
        await _garage.ConnectAsync(settings, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticated = true;
        ResultText = "Prihlásenie úspešné. Čakám na údaje zariadenia.";
    });

    private Task UnlockAsync() => RunAsync(async token =>
    {
        if (!_biometrics.IsSupported || !await _biometrics.AuthenticateAsync("Prihlásenie do garáže", token))
        {
            ResultText = "Biometrické overenie nebolo úspešné alebo nie je dostupné.";
            return;
        }
        token.ThrowIfCancellationRequested();
        var settings = _credentials.LoadSettings()
            ?? throw new InvalidOperationException("Najprv uložte nastavenia pripojenia.");
        var credentials = await _credentials.LoadCredentialsAsync();
        token.ThrowIfCancellationRequested();
        _authenticated = false;
        await _garage.ConnectAsync(settings, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticated = true;
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
        if (!_authenticated || !_biometrics.IsSupported || !_garage.IsConnected ||
            !_garage.IsTelemetryFresh || !_garage.CanOpen)
            throw new InvalidOperationException("Brána nemá aktuálny potvrdený zatvorený stav alebo chýba silná biometria.");
    }

    private Task ForgetAsync() => RunAsync(async token =>
    {
        if (!await _interaction.ConfirmForgetCredentialsAsync()) return;
        token.ThrowIfCancellationRequested();
        await LockAsync();
        _credentials.Forget();
        ClearSecretFields();
        ResultText = "Uložené údaje boli odstránené.";
    });

    public Task LockAsync()
    {
        if (_disposed || _locking) return _lockTask;
        _locking = true;
        _authenticated = false;
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
        bool connected = _authenticated && !_locking && _garage.IsConnected;
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
        TemperatureText = fresh && telemetry!.TemperatureC is double t ? $"Teplota: {t:F1} °C" : "Teplota: —";
        HumidityText = fresh && telemetry!.HumidityPct is double h ? $"Vlhkosť: {h:F1} %" : "Vlhkosť: —";
        PressureText = fresh && telemetry!.PressureHpa is double p ? $"Tlak: {p:F1} hPa" : "Tlak: —";
        bool biometric = _biometrics.IsSupported;
        CanOpen = CanEditSettings && connected && biometric && _garage.CanOpen;
        CanUnlock = CanEditSettings && biometric;
        BiometricText = biometric ? "Otvorenie vždy vyžaduje biometrické potvrdenie."
            : "Ovládanie vyžaduje Android 11+ a zaregistrovanú silnú biometriu. Tu môžete sledovať telemetriu.";
        OnPropertyChanged(nameof(CanEditSettings));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        LoginCommand.NotifyCanExecuteChanged(); UnlockCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged(); LogoutCommand.NotifyCanExecuteChanged(); ForgetCommand.NotifyCanExecuteChanged();
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
        NotifyCommandStates();
        _session.Dispose();
    }
}
