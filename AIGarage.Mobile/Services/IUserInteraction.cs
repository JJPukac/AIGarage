namespace AIGarage.Mobile.Services;

public interface IUserInteraction
{
    Task<bool> ConfirmForgetCredentialsAsync(string profileName);
}

public interface IUiDispatcher
{
    void Dispatch(Action action);
}
