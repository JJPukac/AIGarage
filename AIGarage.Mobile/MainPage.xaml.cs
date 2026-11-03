using AIGarage.Core.Configuration;
using AIGarage.Mobile.Services;

namespace AIGarage.Mobile;

public partial class MainPage : ContentPage
{
    private readonly GarageMqttService _mqtt;
    private readonly CredentialStore _credentials;
    private readonly IBiometricGate _biometrics;
    private readonly IDispatcherTimer _timer;
    private CancellationTokenSource _session = new();
    private bool _busy;
    private bool _authenticated;

    public MainPage(GarageMqttService mqtt, CredentialStore credentials, IBiometricGate biometrics)
    {
        InitializeComponent();
        _mqtt = mqtt; _credentials = credentials; _biometrics = biometrics;
        _mqtt.StateChanged += () => MainThread.BeginInvokeOnMainThread(UpdateState);
        _mqtt.ConnectionChanged += text => MainThread.BeginInvokeOnMainThread(() => ConnectionLabel.Text = text);
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateState();
        var settings = _credentials.LoadSettings();
        if (settings is not null)
        {
            HostEntry.Text = settings.Host; PortEntry.Text = settings.Port.ToString();
            RootEntry.Text = settings.TopicRoot;
            ModePicker.SelectedIndex = (int)settings.Mode;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _timer.Start();
        UpdateState();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private MqttSettings ReadSettings()
    {
        if (!int.TryParse(PortEntry.Text, out int port))
            throw new ArgumentException("Zadajte platný TLS port.");
        var settings = new MqttSettings
        {
            Mode = ModePicker.SelectedIndex == 1 ? InfrastructureMode.PrivateInfrastructure : InfrastructureMode.DirectCloud,
            Host = HostEntry.Text?.Trim() ?? "",
            Port = port,
            TopicRoot = RootEntry.Text?.Trim() ?? ""
        };
        settings.Validate();
        return settings;
    }

    private async void OnLoginClicked(object? sender, EventArgs e) => await RunAsync(async token =>
    {
        var settings = ReadSettings();
        var credentials = new BrokerCredentials(UsernameEntry.Text?.Trim() ?? "", PasswordEntry.Text ?? "", HmacEntry.Text?.Trim() ?? "");
        await _credentials.SaveAsync(settings, credentials);
        ClearSecretFields();
        await _mqtt.ConnectAsync(settings, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticated = true;
        ResultLabel.Text = "Prihlásenie úspešné. Čakám na údaje zariadenia.";
    });

    private async void OnUnlockClicked(object? sender, EventArgs e) => await RunAsync(async token =>
    {
        if (!await _biometrics.AuthenticateAsync("Prihlásenie do garáže", token))
        {
            ResultLabel.Text = "Biometrické overenie nebolo úspešné alebo nie je dostupné.";
            return;
        }
        var settings = _credentials.LoadSettings() ?? throw new InvalidOperationException("Najprv uložte nastavenia pripojenia.");
        var credentials = await _credentials.LoadCredentialsAsync();
        await _mqtt.ConnectAsync(settings, credentials, token);
        token.ThrowIfCancellationRequested();
        _authenticated = true;
        ResultLabel.Text = "Prihlásenie úspešné. Čakám na údaje zariadenia.";
    });

    private async void OnOpenClicked(object? sender, EventArgs e) => await RunAsync(async token =>
    {
        if (!_authenticated || !_mqtt.CanOpen)
            throw new InvalidOperationException("Brána nemá aktuálny potvrdený zatvorený stav.");
        if (!await _biometrics.AuthenticateAsync("Potvrďte otvorenie garáže", token))
        {
            ResultLabel.Text = "Otvorenie nebolo potvrdené.";
            return;
        }
        // The service rechecks freshness after biometric authentication.
        token.ThrowIfCancellationRequested();
        ResultLabel.Text = await _mqtt.OpenAsync(token);
    });

    private async void OnLogoutClicked(object? sender, EventArgs e) => await LockAsync();

    private async void OnForgetClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Odstrániť údaje?", "Uložené pripojenie a prihlasovacie údaje sa odstránia z tohto zariadenia.", "Odstrániť", "Zrušiť")) return;
        await LockAsync();
        _credentials.Forget();
        ClearSecretFields();
        ResultLabel.Text = "Uložené údaje boli odstránené.";
    }

    public async Task LockAsync()
    {
        _authenticated = false;
        _session.Cancel();
        ClearSecretFields();
        UpdateState();
        try { await _mqtt.DisconnectAsync(); }
        catch { ResultLabel.Text = "Aplikácia je uzamknutá. Pripojenie bolo ukončené."; }
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_busy) return;
        if (_session.IsCancellationRequested)
        {
            _session.Dispose();
            _session = new CancellationTokenSource();
        }
        _busy = true;
        UpdateState();
        try { await operation(_session.Token); }
        catch (OperationCanceledException) { ResultLabel.Text = "Operácia bola zrušená."; }
        catch (Exception ex)
        {
            ResultLabel.Text = ex is ArgumentException or InvalidOperationException or IOException
                ? ex.Message
                : "Operácia zlyhala. Skontrolujte pripojenie, certifikát a prihlasovacie údaje.";
        }
        finally { _busy = false; UpdateState(); }
    }

    private void ClearSecretFields()
    {
        UsernameEntry.Text = ""; PasswordEntry.Text = ""; HmacEntry.Text = "";
    }

    private void UpdateState()
    {
        bool connected = _authenticated && _mqtt.IsConnected;
        var telemetry = connected ? _mqtt.Telemetry : null;
        long age = telemetry is null ? long.MaxValue : DateTimeOffset.UtcNow.ToUnixTimeSeconds() - telemetry.Timestamp;
        bool fresh = connected && telemetry is not null && _mqtt.IsTelemetryFresh;
        DoorLabel.Text = !fresh ? "Stav neznámy" : telemetry!.Door switch
        {
            "closed" => "Zatvorené",
            "not_closed" => "Nie je zatvorené",
            "moving_estimated" => "Pohyb (odhad)",
            _ => "Stav neznámy"
        };
        FreshnessLabel.Text = fresh ? $"Údaje pred {age} s" : "Údaje chýbajú alebo už nie sú aktuálne.";
        TemperatureLabel.Text = fresh && telemetry!.TemperatureC is double t ? $"Teplota: {t:F1} °C" : "Teplota: —";
        HumidityLabel.Text = fresh && telemetry!.HumidityPct is double h ? $"Vlhkosť: {h:F1} %" : "Vlhkosť: —";
        PressureLabel.Text = fresh && telemetry!.PressureHpa is double p ? $"Tlak: {p:F1} hPa" : "Tlak: —";
        bool biometric = _biometrics.IsSupported;
        OpenButton.IsEnabled = !_busy && connected && biometric && _mqtt.CanOpen;
        LoginButton.IsEnabled = !_busy;
        UnlockButton.IsEnabled = !_busy && biometric;
        LogoutButton.IsEnabled = !_busy || connected;
        ForgetButton.IsEnabled = !_busy;
        HostEntry.IsEnabled = PortEntry.IsEnabled = RootEntry.IsEnabled = ModePicker.IsEnabled = !_busy;
        UsernameEntry.IsEnabled = PasswordEntry.IsEnabled = HmacEntry.IsEnabled = !_busy;
        BiometricLabel.Text = biometric
            ? "Otvorenie vždy vyžaduje biometrické potvrdenie."
            : "Ovládanie vyžaduje Android 11+ a zaregistrovanú silnú biometriu. Tu môžete sledovať telemetriu.";
    }
}
