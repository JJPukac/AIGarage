namespace AIGarage.Core.Devices;

public sealed record DeviceCatalog
{
    public const int CurrentVersion = 1;
    public const int MaximumDevices = 32;
    public int Version { get; init; } = CurrentVersion;
    public IReadOnlyList<DeviceProfile> Devices { get; init; } = Array.Empty<DeviceProfile>();
    public Guid? SelectedDeviceId { get; init; }

    public void Validate()
    {
        if (Version != CurrentVersion) throw new ArgumentException("Nepodporovaná verzia katalógu zariadení.");
        if (Devices is null || Devices.Count > MaximumDevices)
            throw new ArgumentException($"Katalóg podporuje najviac {MaximumDevices} zariadení.");
        HashSet<Guid> ids = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<(string Host, int Port, string Topic)> routes = [];
        foreach (var profile in Devices)
        {
            if (profile is null) throw new ArgumentException("Katalóg obsahuje neplatné zariadenie.");
            profile.Validate();
            if (!ids.Add(profile.Id)) throw new ArgumentException("ID zariadenia sa nesmie opakovať.");
            if (!names.Add(profile.Name)) throw new ArgumentException("Každé zariadenie potrebuje odlišný názov.");
            var settings = profile.Connection;
            if (!routes.Add((settings.Host.ToUpperInvariant(), settings.Port, settings.TopicRoot)))
                throw new ArgumentException("Rovnaký broker a koreň tém už patria inému profilu.");
        }
        if ((SelectedDeviceId is Guid selected && !ids.Contains(selected)) || (Devices.Count != 0 && SelectedDeviceId is null))
            throw new ArgumentException("Vybrané zariadenie musí patriť do katalógu.");
    }

    public DeviceCatalog WithProfile(DeviceProfile profile)
    {
        Validate();
        var devices = Devices.ToList();
        int existing = devices.FindIndex(p => p.Id == profile.Id);
        if (existing < 0) devices.Add(profile); else devices[existing] = profile;
        var result = this with
        {
            Devices = devices.ToArray(),
            SelectedDeviceId = profile.Id
        };
        result.Validate();
        return result;
    }

    public DeviceCatalog Select(Guid id)
    {
        var result = this with { SelectedDeviceId = id };
        result.Validate();
        return result;
    }

    public DeviceCatalog Without(Guid id)
    {
        Validate();
        var remaining = Devices.Where(p => p.Id != id).ToArray();
        return this with
        {
            Devices = remaining,
            SelectedDeviceId = SelectedDeviceId == id ? remaining.FirstOrDefault()?.Id : SelectedDeviceId
        };
    }
}
