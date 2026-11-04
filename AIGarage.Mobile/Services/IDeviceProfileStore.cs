using AIGarage.Core.Devices;

namespace AIGarage.Mobile.Services;

public interface IDeviceProfileStore
{
    DeviceCatalog LoadCatalog();
    Task SaveAsync(DeviceProfile profile, BrokerCredentials credentials);
    Task<BrokerCredentials> LoadCredentialsAsync(DeviceProfile profile);
    void Select(Guid id);
    void Remove(Guid id);
}
