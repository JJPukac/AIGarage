using Microsoft.Extensions.Logging;
using AIGarage.Mobile.Services;
using AIGarage.Mobile.ViewModels;

namespace AIGarage.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<ICredentialStore, CredentialStore>();
        builder.Services.AddSingleton<IGarageService, GarageMqttService>();
        builder.Services.AddSingleton<IUserInteraction, MauiUserInteraction>();
        builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
        builder.Services.AddSingleton(TimeProvider.System);
#if ANDROID
        builder.Services.AddSingleton<IBiometricGate, Platforms.Android.AndroidBiometricGate>();
#else
        builder.Services.AddSingleton<IBiometricGate, UnsupportedBiometricGate>();
#endif
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();
        return builder.Build();
    }
}
