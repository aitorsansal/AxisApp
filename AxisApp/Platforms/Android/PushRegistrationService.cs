using AxisApp.Services;
using Firebase.Messaging;
using Microsoft.Maui.ApplicationModel;
using JavaException = Java.Lang.Exception;
using Object = Java.Lang.Object;
using Log = Android.Util.Log;

namespace AxisApp;

/// <summary>Real Android push registration via the raw Xamarin.Firebase.Messaging binding — no
/// Plugin.Firebase-style wrapper, same "bind directly to the platform SDK" convention
/// GoogleAuthService already established for Credential Manager. FirebaseMessaging.Instance.GetToken()
/// returns a Java Android.Gms.Tasks.Task, not a C# Task, so AwaitTask below wraps its
/// AddOnSuccessListener/AddOnFailureListener callbacks in a TaskCompletionSource — the exact same
/// shape GoogleAuthService's Callback/ICredentialManagerCallback wrapping already uses for the
/// identical "Java callback API, not an awaitable one" problem.
///
/// Confirmed working end to end against a real Play Store install (2026-09-07) — a device_tokens
/// row was created correctly after signing in and visiting Groups. Getting there surfaced a real
/// production-only failure mode: the Android API key embedded in google-services.json can be
/// API-restricted in Google Cloud Console, and if the Firebase Installations API isn't in its
/// allowed list, GetToken() fails at the FIS-auth-token step with a 403 (API_KEY_SERVICE_BLOCKED)
/// before it ever reaches real FCM registration — invisible from the app's own logs (see the catch
/// below), only found via a live adb logcat capture. Unrelated to Play App Signing/SHA fingerprints
/// despite looking similar on the surface — check the API key's restrictions first if this silently
/// stops working again.</summary>
public class PushRegistrationService : IPushRegistrationService
{
    private const string LogTag = "AxisPushRegistration";

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
        catch (Exception ex)
        {
            // Best-effort — see IPushRegistrationService's remarks. Logged (not surfaced to the
            // user) since this failed silently and invisibly in production once already — see this
            // class's remarks.
            Log.Warn(LogTag, $"RegisterAsync failed: {ex}");
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
        catch (Exception ex)
        {
            // Best-effort.
            Log.Warn(LogTag, $"UnregisterAsync failed: {ex}");
        }
    }
}
