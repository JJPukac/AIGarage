using AIGarage.Core.Configuration;

namespace AIGarage.Core.Devices;

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    DoorState = 1,
    OpenGarage = 2,
    Temperature = 4,
    Humidity = 8,
    Pressure = 16,
    GarageWithEnvironment = DoorState | OpenGarage | Temperature | Humidity | Pressure
}

// Local identity and configured functions are independent of board type and broker address.
// MQTT topic + HMAC remain the authenticated wire identity until the protocol migration (A3).
public sealed record DeviceProfile
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required MqttSettings Connection { get; init; }
    public DeviceCapabilities Capabilities { get; init; } = DeviceCapabilities.GarageWithEnvironment;

    public bool Supports(DeviceCapabilities capability) => (Capabilities & capability) == capability;

    public void Validate()
    {
        if (Id == Guid.Empty) throw new ArgumentException("Zariadenie potrebuje jednoznačné ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name != Name.Trim() || Name.Length > 64 || Name.Any(char.IsControl))
            throw new ArgumentException("Názov zariadenia musí mať 1 až 64 znakov bez riadiacich znakov.");
        if (Connection is null) throw new ArgumentException("Chýba profil MQTT pripojenia.");
        Connection.Validate();
        if (Capabilities == DeviceCapabilities.None || (Capabilities & ~DeviceCapabilities.GarageWithEnvironment) != 0)
            throw new ArgumentException("Zariadenie obsahuje neznáme alebo žiadne podporované funkcie.");
        if (Supports(DeviceCapabilities.OpenGarage) && !Supports(DeviceCapabilities.DoorState))
            throw new ArgumentException("Otvorenie garáže vyžaduje sledovanie zatvorenej polohy.");
    }
}
