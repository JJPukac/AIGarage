namespace AIGarage.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly MainPage _page;

    public App(AppShell shell, MainPage page)
    {
        InitializeComponent();
        _shell = shell;
        _page = page;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Stopped += async (_, _) => await _page.LockAsync();
        window.Destroying += async (_, _) => await _page.LockAsync();
        return window;
    }
}
