namespace AIGarage.Mobile.Services;

// Compile-time replacements for MAUI storage only. MQTT service and credentials are linked unchanged.
internal static class Preferences
{
    public static FakePreferences Default { get; } = new();
}

internal sealed class FakePreferences
{
    private readonly Dictionary<string, string> _values = [];
    public string Get(string key, string fallback) => _values.GetValueOrDefault(key, fallback);
    public void Set(string key, string value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
}

internal static class SecureStorage
{
    public static FakeSecureStorage Default { get; } = new();
}

internal sealed class FakeSecureStorage
{
    private readonly Dictionary<string, string> _values = [];
    public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
    public bool Remove(string key) => _values.Remove(key);
}
