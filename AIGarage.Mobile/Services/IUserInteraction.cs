namespace AIGarage.Mobile.Services;

public interface IUserInteraction
{
    Task<bool> ConfirmForgetCredentialsAsync();
}

public interface IUiDispatcher
{
    void Dispatch(Action action);
}
