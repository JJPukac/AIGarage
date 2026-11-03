using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using AIGarage.Mobile.Services;

namespace AIGarage.Mobile.Platforms.Android;

public sealed class AndroidBiometricGate : IBiometricGate
{
    public bool IsSupported
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return false;
            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(Context.BiometricService) as BiometricManager;
            return manager?.CanAuthenticate((int)BiometricManagerAuthenticators.BiometricStrong)
                == BiometricCode.Success;
        }
    }

    public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30) || !IsSupported || cancellationToken.IsCancellationRequested) return false;
            var activity = Platform.CurrentActivity;
            if (activity is null) return false;

            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var signal = new CancellationSignal();
            using var registration = cancellationToken.Register(() =>
            {
                signal.Cancel();
                result.TrySetCanceled(cancellationToken);
            });
            using var callback = new Callback(result);
            using var negative = new NegativeListener(result);
            using var builder = new BiometricPrompt.Builder(activity);
            using var prompt = builder
                .SetTitle("AI Garage")
                .SetSubtitle(reason)
                .SetAllowedAuthenticators((int)BiometricManagerAuthenticators.BiometricStrong)
                .SetNegativeButton("Zrušiť", activity.MainExecutor!, negative)
                .Build();
            prompt.Authenticate(signal, activity.MainExecutor!, callback);
            return await result.Task;
        });

    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class Callback(TaskCompletionSource<bool> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? authenticationResult)
        {
            base.OnAuthenticationSucceeded(authenticationResult);
            result.TrySetResult(true);
        }
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            result.TrySetResult(false);
        }
    }

    private sealed class NegativeListener(TaskCompletionSource<bool> result) : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => result.TrySetResult(false);
    }
}
