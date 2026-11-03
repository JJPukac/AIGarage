using AIGarage.Mobile.ViewModels;

namespace AIGarage.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IDispatcherTimer _timer;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => _viewModel.RefreshState();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshState();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }
}
