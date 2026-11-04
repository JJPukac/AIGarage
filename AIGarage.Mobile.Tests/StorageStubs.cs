namespace AIGarage.Mobile.Services;

// Compile-time replacements for MAUI storage only. Production services are linked from the app.
internal static class Preferences
{
    public static FakePreferences Default { get; } = new();
}

internal sealed class FakePreferences
{
    private readonly Dictionary<string, string> _values = [];
    public Action? BeforeWrite { get; set; }
    public string Get(string key, string fallback) => _values.GetValueOrDefault(key, fallback);
    public void Set(string key, string value) { BeforeWrite?.Invoke(); _values[key] = value; }
    public void Remove(string key) => _values.Remove(key);
    public void Reset() { _values.Clear(); BeforeWrite = null; }
}

internal static class SecureStorage
{
    public static FakeSecureStorage Default { get; } = new();
}

internal sealed class FakeSecureStorage
{
    private readonly Dictionary<string, string> _values = [];
    public Action? BeforeWrite { get; set; }
    public int ReadCount { get; private set; }
    public Task<string?> GetAsync(string key) { ReadCount++; return Task.FromResult(_values.GetValueOrDefault(key)); }
    public Task SetAsync(string key, string value) { BeforeWrite?.Invoke(); _values[key] = value; return Task.CompletedTask; }
    public bool Remove(string key) => _values.Remove(key);
    public void Reset() { _values.Clear(); BeforeWrite = null; ReadCount = 0; }
}
