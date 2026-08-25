using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace AxisApp.Services;

/// <summary>
/// Wires Supabase.Gotrue's session persistence hook to device SecureStorage. Ported from
/// PokeCards' SecureStorageSessionPersistence — same Supabase package version (1.6.0), same
/// interface, confirmed working there. Two things had to be true for this to actually restore a
/// session on launch, both now wired: SupabaseOptions.SessionHandler set to an instance of this
/// class (MauiProgram.cs's Client registration), and SupabaseAuthService.RestoreSessionAsync
/// calling client.Auth.LoadSession() before client.InitializeAsync() — confirmed via instrumented
/// logging 2026-08-25 that SaveSession fired correctly on sign-in, but nothing ever called
/// LoadSession on the next launch without that explicit call; InitializeAsync() alone doesn't
/// invoke the configured handler on its own.
/// </summary>
public sealed class SupabaseSessionPersistence : IGotrueSessionPersistence<Session>
{
    private const string Key = AppConstants.Preferences.SupabaseSession;

    public void SaveSession(Session session)
    {
        var json = JsonConvert.SerializeObject(session);
        Task.Run(() => SecureStorage.Default.SetAsync(Key, json)).GetAwaiter().GetResult();
    }

    public void DestroySession()
    {
        SecureStorage.Default.Remove(Key);
    }

    public Session? LoadSession()
    {
        var json = Task.Run(() => SecureStorage.Default.GetAsync(Key)).GetAwaiter().GetResult();
        return json is null ? null : JsonConvert.DeserializeObject<Session>(json);
    }
}
