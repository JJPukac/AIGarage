using AIGarage.Core.Configuration;

namespace AIGarage.Mobile.Services;

public interface ICredentialStore
{
    MqttSettings? LoadSettings();
    Task SaveAsync(MqttSettings settings, BrokerCredentials credentials);
    Task<BrokerCredentials> LoadCredentialsAsync();
    void Forget();
}
