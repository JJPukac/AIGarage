using AIGarage.Mobile.ViewModels;

namespace AIGarage.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly MainViewModel _viewModel;

    public App(AppShell shell, MainViewModel viewModel)
    {
        InitializeComponent();
        _shell = shell;
        _viewModel = viewModel;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Stopped += async (_, _) => await _viewModel.LockAsync();
        window.Destroying += async (_, _) => await _viewModel.LockAsync();
        return window;
    }
}
