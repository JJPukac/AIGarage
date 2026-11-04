namespace AIGarage.Mobile.Services;

public sealed class MauiUserInteraction : IUserInteraction
{
    public Task<bool> ConfirmForgetCredentialsAsync(string profileName) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var page = Shell.Current ?? throw new InvalidOperationException("Obrazovka aplikácie nie je dostupná.");
        return await page.DisplayAlertAsync("Odstrániť údaje?",
            $"Profil „{profileName}“ a jeho prihlasovacie údaje sa odstránia z tohto zariadenia. Ostatné profily zostanú uložené.",
            "Odstrániť", "Zrušiť");
    });
}

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Dispatch(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
