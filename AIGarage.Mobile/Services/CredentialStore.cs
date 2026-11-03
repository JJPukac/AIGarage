using System.Text.Json;
using AIGarage.Core.Configuration;

namespace AIGarage.Mobile.Services;

public sealed record BrokerCredentials(string Username, string Password, string HmacKeyHex)
{
    public byte[] GetKey()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            throw new ArgumentException("Vyplňte MQTT používateľa aj heslo.");
        if (HmacKeyHex.Length != 64)
            throw new ArgumentException("HMAC kľúč musí obsahovať 64 hexadecimálnych znakov.");
        byte[] key;
        try { key = Convert.FromHexString(HmacKeyHex); }
        catch (FormatException) { throw new ArgumentException("Neplatný HMAC kľúč."); }
        return key;
    }
}

public sealed class CredentialStore
{
    private const string SettingsKey = "mqtt.settings.v1";
    private const string CredentialsKey = "mqtt.credentials.v1";

    public MqttSettings? LoadSettings()
    {
        var json = Preferences.Default.Get(SettingsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<MqttSettings>(json); }
        catch (JsonException) { return null; }
    }

    public async Task SaveAsync(MqttSettings settings, BrokerCredentials credentials)
    {
        settings.Validate();
        var key = credentials.GetKey();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        // One secure value prevents partially written username/password/key combinations.
        await SecureStorage.Default.SetAsync(CredentialsKey, JsonSerializer.Serialize(credentials));
        Preferences.Default.Set(SettingsKey, JsonSerializer.Serialize(settings));
    }

    public async Task<BrokerCredentials> LoadCredentialsAsync()
    {
        var json = await SecureStorage.Default.GetAsync(CredentialsKey);
        if (string.IsNullOrEmpty(json))
            throw new InvalidOperationException("Najprv zadajte a uložte prihlasovacie údaje.");
        return JsonSerializer.Deserialize<BrokerCredentials>(json)
            ?? throw new InvalidOperationException("Uložené údaje sú neplatné. Zadajte ich znova.");
    }

    public void Forget()
    {
        SecureStorage.Default.Remove(CredentialsKey);
        Preferences.Default.Remove(SettingsKey);
    }
}
