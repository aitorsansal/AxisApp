using AxisApp.Services;
using Firebase.Messaging;
using Microsoft.Maui.ApplicationModel;
using JavaException = Java.Lang.Exception;
using Object = Java.Lang.Object;

namespace AxisApp;

/// <summary>Real Android push registration via the raw Xamarin.Firebase.Messaging binding — no
/// Plugin.Firebase-style wrapper, same "bind directly to the platform SDK" convention
/// GoogleAuthService already established for Credential Manager. FirebaseMessaging.Instance.GetToken()
/// returns a Java Android.Gms.Tasks.Task, not a C# Task, so AwaitTask below wraps its
/// AddOnSuccessListener/AddOnFailureListener callbacks in a TaskCompletionSource — the exact same
/// shape GoogleAuthService's Callback/ICredentialManagerCallback wrapping already uses for the
/// identical "Java callback API, not an awaitable one" problem.
///
/// Not yet build-verified against a real device — compiles, but GetToken() actually succeeding
/// (Firebase auto-initializing from google-services.json with no explicit FirebaseApp.InitializeApp
/// call) hasn't been confirmed the way GoogleAuthService's Android flow was confirmed against a
/// real MIUI device. Report back whatever the real first-run behavior is.</summary>
public class PushRegistrationService : IPushRegistrationService
{
    private readonly IDeviceTokensRepository deviceTokensRepository;

    public PushRegistrationService(IDeviceTokensRepository deviceTokensRepository)
    {
        this.deviceTokensRepository = deviceTokensRepository;
    }

    private class SuccessListener : Object, Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly TaskCompletionSource<Object?> tcs;
        public SuccessListener(TaskCompletionSource<Object?> tcs) => this.tcs = tcs;
        public void OnSuccess(Object? result) => tcs.TrySetResult(result);
    }

    private class FailureListener : Object, Android.Gms.Tasks.IOnFailureListener
    {
        private readonly TaskCompletionSource<Object?> tcs;
        public FailureListener(TaskCompletionSource<Object?> tcs) => this.tcs = tcs;
        public void OnFailure(JavaException e) => tcs.TrySetException(new Exception(e.Message));
    }

    private static Task<Object?> AwaitTask(Android.Gms.Tasks.Task task)
    {
        var tcs = new TaskCompletionSource<Object?>();
        task.AddOnSuccessListener(new SuccessListener(tcs));
        task.AddOnFailureListener(new FailureListener(tcs));
        return tcs.Task;
    }

    public async Task RegisterAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted) return;

            var result = await AwaitTask(FirebaseMessaging.Instance!.GetToken());
            var token = result?.ToString();
            if (string.IsNullOrEmpty(token)) return;

            await deviceTokensRepository.RegisterAsync(token, "android");
        }
        catch
        {
            // Best-effort — see IPushRegistrationService's remarks.
        }
    }

    public async Task UnregisterAsync()
    {
        try
        {
            var result = await AwaitTask(FirebaseMessaging.Instance!.GetToken());
            var token = result?.ToString();
            if (!string.IsNullOrEmpty(token))
                await deviceTokensRepository.UnregisterAsync(token);

            // Invalidates the token at Firebase itself, not just our own row — a fresh sign-in on
            // this device (same or different account) gets a genuinely new token from GetToken(),
            // rather than silently reusing one already deleted server-side.
            await AwaitTask(FirebaseMessaging.Instance!.DeleteToken());
        }
        catch
        {
            // Best-effort.
        }
    }
}
