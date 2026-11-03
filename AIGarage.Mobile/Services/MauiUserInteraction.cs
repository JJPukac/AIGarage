namespace AIGarage.Mobile.Services;

public sealed class MauiUserInteraction : IUserInteraction
{
    public Task<bool> ConfirmForgetCredentialsAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var page = Shell.Current ?? throw new InvalidOperationException("Obrazovka aplikácie nie je dostupná.");
        return await page.DisplayAlertAsync("Odstrániť údaje?",
            "Uložené pripojenie a prihlasovacie údaje sa odstránia z tohto zariadenia.",
            "Odstrániť", "Zrušiť");
    });
}

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Dispatch(Action action) => MainThread.BeginInvokeOnMainThread(action);
}
