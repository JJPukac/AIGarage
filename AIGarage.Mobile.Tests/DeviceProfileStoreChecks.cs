using System.Text.Json;
using AIGarage.Core.Devices;
using AIGarage.Mobile.Services;

namespace AIGarage.Mobile.Tests;

internal static class DeviceProfileStoreChecks
{
    private const string CatalogKey = "mqtt.devices.v1";
    private static readonly DeviceProfile Second = TestSession.Profile with
    {
        Id = Guid.NewGuid(), Name = "Second garage",
        Connection = TestSession.Settings with { TopicRoot = "aigarage/garage2" }
    };
    private static readonly BrokerCredentials SecondCredentials = new("second-user", "second-password", new string('a', 64));

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Profile storage assertion failed.");
    }

    private static async Task RejectAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static DeviceProfileStore EmptyStore()
    {
        Preferences.Default.Reset(); SecureStorage.Default.Reset();
        return new DeviceProfileStore();
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("credential diagnostics never format secret values", () =>
        {
            string formatted = TestSession.Credentials.ToString();
            Require(!formatted.Contains(TestSession.Credentials.Username) && !formatted.Contains(TestSession.Credentials.Password) && !formatted.Contains(TestSession.Credentials.HmacKeyHex));
            return Task.CompletedTask;
        });
        await test("store scopes credentials to device IDs without plaintext secrets in catalog", async () =>
        {
            var store = EmptyStore();
            await store.SaveAsync(TestSession.Profile, TestSession.Credentials);
            await store.SaveAsync(Second, SecondCredentials);
            Require(await store.LoadCredentialsAsync(TestSession.Profile) == TestSession.Credentials);
            Require(await store.LoadCredentialsAsync(Second) == SecondCredentials);
            string publicJson = Preferences.Default.Get(CatalogKey, "");
            Require(!publicJson.Contains(TestSession.Credentials.Password) && !publicJson.Contains(SecondCredentials.HmacKeyHex) && !publicJson.Contains(SecondCredentials.Username));
        });
        await test("deleting one stored device never deletes another device's credentials", async () =>
        {
            var store = EmptyStore();
            await store.SaveAsync(TestSession.Profile, TestSession.Credentials); await store.SaveAsync(Second, SecondCredentials);
            store.Remove(TestSession.Profile.Id);
            Require(store.LoadCatalog().Devices.Single() == Second);
            Require(await store.LoadCredentialsAsync(Second) == SecondCredentials);
            await RejectAsync<InvalidOperationException>(() => store.LoadCredentialsAsync(TestSession.Profile));
        });
        await test("swapped secure bundles cannot cross device boundaries", async () =>
        {
            var store = EmptyStore(); await store.SaveAsync(TestSession.Profile, TestSession.Credentials); await store.SaveAsync(Second, SecondCredentials);
            var firstBundle = await SecureStorage.Default.GetAsync($"mqtt.device.{TestSession.Profile.Id:N}.v2");
            await SecureStorage.Default.SetAsync($"mqtt.device.{Second.Id:N}.v2", firstBundle!);
            await RejectAsync<InvalidOperationException>(() => store.LoadCredentialsAsync(Second));
            Require(await store.LoadCredentialsAsync(TestSession.Profile) == TestSession.Credentials);
        });
        foreach (string tamper in new[] { "host", "topic", "capabilities", "id" })
            await test("public profile " + tamper + " tampering cannot reuse stored credentials", async () =>
            {
                var store = EmptyStore();
                var original = TestSession.Profile with { Capabilities = DeviceCapabilities.DoorState };
                await store.SaveAsync(original, TestSession.Credentials);
                var modified = tamper switch
                {
                    "host" => original with { Connection = original.Connection with { Host = "different.example" } },
                    "topic" => original with { Connection = original.Connection with { TopicRoot = "aigarage/other" } },
                    "capabilities" => original with { Capabilities = DeviceCapabilities.GarageWithEnvironment },
                    _ => original with { Id = Guid.NewGuid() }
                };
                Preferences.Default.Set(CatalogKey, JsonSerializer.Serialize(new DeviceCatalog().WithProfile(modified)));
                await RejectAsync<InvalidOperationException>(() => store.LoadCredentialsAsync(modified));
            });
        await test("failed secure write leaves original catalog and credentials usable", async () =>
        {
            var store = EmptyStore(); await store.SaveAsync(TestSession.Profile, TestSession.Credentials);
            SecureStorage.Default.BeforeWrite = () => throw new IOException("Simulated secure storage failure.");
            await RejectAsync<IOException>(() => store.SaveAsync(Second, SecondCredentials));
            SecureStorage.Default.BeforeWrite = null;
            Require(store.LoadCatalog().Devices.Single() == TestSession.Profile);
            Require(await store.LoadCredentialsAsync(TestSession.Profile) == TestSession.Credentials);
        });
        await test("partial metadata write fails closed instead of sending credentials to wrong host", async () =>
        {
            var store = EmptyStore(); await store.SaveAsync(TestSession.Profile, TestSession.Credentials);
            var edited = TestSession.Profile with { Connection = TestSession.Settings with { Host = "new.example" } };
            Preferences.Default.BeforeWrite = () => throw new IOException("Simulated metadata failure.");
            await RejectAsync<IOException>(() => store.SaveAsync(edited, SecondCredentials));
            Preferences.Default.BeforeWrite = null;
            Require(store.LoadCatalog().Devices.Single() == TestSession.Profile);
            await RejectAsync<InvalidOperationException>(() => store.LoadCredentialsAsync(TestSession.Profile));
            await store.SaveAsync(edited, SecondCredentials);
            Require(await store.LoadCredentialsAsync(edited) == SecondCredentials);
        });
        foreach (string json in new[] { "{", "null", "{\"Version\":99}", "{\"Devices\":null}" })
            await test("invalid catalog is preserved and cannot be silently overwritten: " + json, async () =>
            {
                var store = EmptyStore(); Preferences.Default.Set(CatalogKey, json);
                await RejectAsync<InvalidOperationException>(() => store.SaveAsync(TestSession.Profile, TestSession.Credentials));
                Require(Preferences.Default.Get(CatalogKey, "") == json && SecureStorage.Default.ReadCount == 0);
            });
        await test("legacy metadata loads without reading secrets and migrates after authorized load", async () =>
        {
            var store = EmptyStore();
            Preferences.Default.Set("mqtt.settings.v1", JsonSerializer.Serialize(TestSession.Settings));
            await SecureStorage.Default.SetAsync("mqtt.credentials.v1", JsonSerializer.Serialize(TestSession.Credentials));
            var legacy = store.LoadCatalog().Devices.Single();
            Require(legacy.Id == DeviceProfileStore.LegacyDeviceId && legacy.Connection == TestSession.Settings && SecureStorage.Default.ReadCount == 0);
            Require(await store.LoadCredentialsAsync(legacy) == TestSession.Credentials);
            Require(Preferences.Default.Get("mqtt.settings.v1", "") == "" && await SecureStorage.Default.GetAsync("mqtt.credentials.v1") is null);
            Require(new DeviceProfileStore().LoadCatalog().Devices.Single() == legacy);
            Require(await store.LoadCredentialsAsync(legacy) == TestSession.Credentials);
        });
        await test("failed migration preserves original credentials for retry", async () =>
        {
            var store = EmptyStore();
            Preferences.Default.Set("mqtt.settings.v1", JsonSerializer.Serialize(TestSession.Settings));
            await SecureStorage.Default.SetAsync("mqtt.credentials.v1", JsonSerializer.Serialize(TestSession.Credentials));
            var legacy = store.LoadCatalog().Devices.Single();
            Preferences.Default.BeforeWrite = () => throw new IOException("Migration interrupted.");
            await RejectAsync<IOException>(() => store.LoadCredentialsAsync(legacy));
            Preferences.Default.BeforeWrite = null;
            Require(Preferences.Default.Get("mqtt.settings.v1", "") != "" && await SecureStorage.Default.GetAsync("mqtt.credentials.v1") is not null);
            Require(await store.LoadCredentialsAsync(legacy) == TestSession.Credentials);
            Require(Preferences.Default.Get("mqtt.settings.v1", "") == "" && await SecureStorage.Default.GetAsync("mqtt.credentials.v1") is null);
        });
        EmptyStore();
    }
}
