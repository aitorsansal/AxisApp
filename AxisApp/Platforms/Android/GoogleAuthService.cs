using Android.Runtime;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using AxisApp.Services;
using Google.Android.Libraries.Identity.GoogleId;
using Java.Lang;
using Java.Util.Concurrent;
using Microsoft.Maui.ApplicationModel;
using Object = Java.Lang.Object;

namespace AxisApp;

/// <summary>Ported from PokeCards' Platforms/Android/GoogleAuthService.cs (same AndroidX
/// Credentials + Google Identity packages) — native account picker via Jetpack Credential
/// Manager, no browser round trip needed on Android. Requires SupabaseConfig.GoogleWebClientId
/// (the Web OAuth client already entered in Supabase's Google provider settings — Credential
/// Manager wants the *Web* client id here, not a separate Android one, so it can hand back a
/// token Supabase's own backend can verify) and a SHA-1 fingerprint for this app's signing key
/// registered against that Google Cloud project, or every request is rejected before any UI
/// shows.</summary>
public class GoogleAuthService : IGoogleAuthService
{
    private class InlineExecutor : Object, IExecutor
    {
        public void Execute(IRunnable? command) => command?.Run();
    }

    private class Callback : Object, ICredentialManagerCallback
    {
        private readonly TaskCompletionSource<GetCredentialResponse> tcs;

        public Callback(TaskCompletionSource<GetCredentialResponse> tcs) => this.tcs = tcs;

        public void OnResult(Object? result) => tcs.TrySetResult(result!.JavaCast<GetCredentialResponse>());

        public void OnError(Object e) => tcs.TrySetException(e.JavaCast<GetCredentialException>());
    }

    public async Task<AuthResult> SignInAsync(Supabase.Client client)
    {
        var context = Platform.CurrentActivity ?? throw new InvalidOperationException("No current Activity.");

        var googleIdOption = new GetGoogleIdOption.Builder()
            .SetFilterByAuthorizedAccounts(false)
            .SetServerClientId(SupabaseConfig.GoogleWebClientId)
            .Build();

        var request = new GetCredentialRequest.Builder()
            .AddCredentialOption(googleIdOption)
            .Build();

        var credentialManager = CredentialManager.Create(context);
        var tcs = new TaskCompletionSource<GetCredentialResponse>();
        credentialManager.GetCredentialAsync(context, request, null, new InlineExecutor(), new Callback(tcs));

        GetCredentialResponse credentialResponse;
        try
        {
            credentialResponse = await tcs.Task;
        }
        catch (GetCredentialCancellationException)
        {
            // User dismissed the account picker — see IGoogleAuthService's remarks on why this
            // is (false, null) rather than a real error.
            return new AuthResult(false, null);
        }
        catch (System.Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }

        var credential = GoogleIdTokenCredential.CreateFrom(credentialResponse.Credential.Data);

        try
        {
            await client.Auth.SignInWithIdToken(Supabase.Gotrue.Constants.Provider.Google, credential.IdToken!);
            return new AuthResult(true);
        }
        catch (System.Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }
}
