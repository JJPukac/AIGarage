using System.Text.Json;
using AIGarage.Core.Configuration;
using AIGarage.Core.Devices;

namespace AIGarage.Mobile.Services;

public sealed class DeviceProfileStore : IDeviceProfileStore
{
    private const string LegacySettingsKey = "mqtt.settings.v1";
    private const string LegacyCredentialsKey = "mqtt.credentials.v1";
    private const string CatalogKey = "mqtt.devices.v1";
    internal static readonly Guid LegacyDeviceId = new("0c101bc1-e870-4c5a-b25b-c5b4e62ac0a1");
    private sealed record SecureBundle(DeviceProfile Profile, BrokerCredentials Credentials);
    private static string SecretKey(Guid id) => $"mqtt.device.{id:N}.v2";

    public DeviceCatalog LoadCatalog()
    {
        try
        {
            var json = Preferences.Default.Get(CatalogKey, string.Empty);
            DeviceCatalog catalog;
            if (!string.IsNullOrEmpty(json))
                catalog = JsonSerializer.Deserialize<DeviceCatalog>(json)
                    ?? throw new ArgumentException("Invalid catalog.");
            else
            {
                var legacy = LoadLegacyProfile();
                catalog = legacy is null ? new DeviceCatalog()
                    : new DeviceCatalog { Devices = new[] { legacy }, SelectedDeviceId = legacy.Id };
            }
            catalog.Validate();
            return catalog;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // Never replace corrupt or newer configuration with an empty catalog.
            throw new InvalidOperationException("Katalóg zariadení je neplatný alebo má nepodporovanú verziu. Údaje sa neprepísali.");
        }
    }

    public async Task SaveAsync(DeviceProfile profile, BrokerCredentials credentials)
    {
        var catalog = LoadCatalog().WithProfile(profile);
        var key = credentials.GetKey();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        // Bind the entire immutable profile to its secrets in one encrypted value.
        // If the subsequent public write fails, a metadata mismatch prevents credential reuse.
        await SecureStorage.Default.SetAsync(SecretKey(profile.Id), JsonSerializer.Serialize(new SecureBundle(profile, credentials)));
        SaveCatalog(catalog);
        if (profile.Id == LegacyDeviceId) RemoveLegacy();
    }

    public async Task<BrokerCredentials> LoadCredentialsAsync(DeviceProfile profile)
    {
        profile.Validate();
        if (!LoadCatalog().Devices.Contains(profile))
            throw new InvalidOperationException("Profil sa zmenil. Načítajte ho znova.");
        var json = await SecureStorage.Default.GetAsync(SecretKey(profile.Id));
        BrokerCredentials credentials;
        try
        {
            if (!string.IsNullOrEmpty(json))
            {
                var bundle = JsonSerializer.Deserialize<SecureBundle>(json);
                if (bundle?.Profile != profile || bundle.Credentials is null)
                    throw new ArgumentException("Profile and credentials do not match.");
                credentials = bundle.Credentials;
            }
            else
            {
                // Legacy secrets are read only when the caller has authorized unlocking.
                if (profile != LoadLegacyProfile()) throw new ArgumentException("No matching credentials.");
                var legacyJson = await SecureStorage.Default.GetAsync(LegacyCredentialsKey);
                credentials = string.IsNullOrEmpty(legacyJson) ? throw new ArgumentException("Missing legacy credentials.")
                    : JsonSerializer.Deserialize<BrokerCredentials>(legacyJson) ?? throw new ArgumentException("Invalid credentials.");
            }
            var key = credentials.GetKey();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidOperationException("Uložené údaje nezodpovedajú profilu alebo chýbajú. Zadajte ich a uložte znova.");
        }
        if (string.IsNullOrEmpty(json) || profile.Id == LegacyDeviceId && !string.IsNullOrEmpty(Preferences.Default.Get(LegacySettingsKey, "")))
            await SaveAsync(profile, credentials);
        return credentials;
    }

    public void Select(Guid id) => SaveCatalog(LoadCatalog().Select(id));

    public void Remove(Guid id)
    {
        var catalog = LoadCatalog();
        SecureStorage.Default.Remove(SecretKey(id));
        if (id == LegacyDeviceId) RemoveLegacy();
        SaveCatalog(catalog.Without(id));
    }

    private static void SaveCatalog(DeviceCatalog catalog)
    {
        catalog.Validate();
        Preferences.Default.Set(CatalogKey, JsonSerializer.Serialize(catalog));
    }

    private static DeviceProfile? LoadLegacyProfile()
    {
        var json = Preferences.Default.Get(LegacySettingsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;
        var settings = JsonSerializer.Deserialize<MqttSettings>(json) ?? throw new ArgumentException("Invalid legacy settings.");
        return new DeviceProfile { Id = LegacyDeviceId, Name = "Garáž 1", Connection = settings };
    }

    private static void RemoveLegacy()
    {
        SecureStorage.Default.Remove(LegacyCredentialsKey);
        Preferences.Default.Remove(LegacySettingsKey);
    }
}
