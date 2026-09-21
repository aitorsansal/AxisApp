using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AxisApp.Services;
using Microsoft.Maui.ApplicationModel;

namespace AxisApp;

/// <summary>Windows has no Credential Manager equivalent and no working deep-link path back into
/// an unpackaged Win32 app (see CLAUDE.md's "Deep linking" notes — Android App Links only today),
/// so this can't mirror the Android flow.
///
/// Originally built on client.Auth.SignIn(Provider, SignInOptions) with FlowType.PKCE, following
/// gotrue-csharp's own documented native-app pattern — but that hit a real, reproducible
/// bad_oauth_state failure against this project every time (confirmed via Supabase's own Auth
/// Logs: state rejected within ~6-9 seconds of a clean /authorize -> /callback round trip, ruling
/// out expiry/staleness). That matches an open, unresolved issue
/// (supabase-community/supabase-csharp#222) about that SDK's PKCE state handling not
/// round-tripping correctly. It was then replaced with a hand-built implicit flow, which worked
/// but had a login-CSRF hole (SECURITY_AUDIT.md #3): any web page open in the user's browser could
/// send its own tokens to the fixed loopback port during the sign-in window and the app would
/// adopt that session. A random state couldn't fix it — Supabase's implicit flow doesn't
/// round-trip a client-supplied one, and a nonce embedded in the loopback page doesn't help when
/// an attacker can navigate the browser to the loopback URL with their own fragment.
///
/// So this is PKCE again, built by hand against GoTrue's HTTP API instead of through the SDK's
/// state handling: a random code_verifier never leaves the process, only its SHA-256
/// code_challenge goes into the authorize URL, and the code that comes back on the loopback
/// redirect is only redeemable with that verifier. A code an attacker injects was issued against
/// the attacker's own challenge, so the exchange fails. As a bonus the result now arrives in the
/// query string, so the old fragment-extractor page is gone.
///
/// Untested against Supabase since the rewrite: if bad_oauth_state comes back, the fault is
/// server-side after all and this needs a rethink rather than a retry.</summary>
public class GoogleAuthService : IGoogleAuthService
{
    // Deliberately "localhost", not "127.0.0.1" — Supabase's own ecosystem has documented
    // sensitivity to this exact distinction, even though HttpListener treats both as equivalent
    // loopback binds on Windows. Fixed port because the redirect allow-list entry is exact.
    private const string RedirectUri = "http://localhost:48291/";

    private static readonly HttpClient Http = new();

    public async Task<AuthResult> SignInAsync(Supabase.Client client)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUri = $"{SupabaseConfig.Url}/auth/v1/authorize?provider=google"
            + $"&redirect_to={Uri.EscapeDataString(RedirectUri)}"
            + $"&code_challenge={challenge}&code_challenge_method=s256";

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            return new AuthResult(false, $"Couldn't start the local sign-in listener: {ex.Message}");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        timeout.Token.Register(() =>
        {
            try { listener.Stop(); } catch { /* already stopped */ }
        });

        try
        {
            await Launcher.Default.OpenAsync(new Uri(authorizeUri));

            while (true)
            {
                var context = await listener.GetContextAsync();
                var query = context.Request.QueryString;
                var error = query["error_description"] ?? query["error"];
                var code = query["code"];

                // Browsers also probe /favicon.ico, and anything else on the machine can hit this
                // port; only a request to the root carrying a code or an error is the callback.
                if (context.Request.Url?.AbsolutePath != "/" || (error is null && code is null))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                if (error is not null)
                {
                    await RespondHtmlAsync(context, PageHtml("Sign-in failed", error));
                    return new AuthResult(false, error);
                }

                var (accessToken, refreshToken, exchangeError) = await ExchangeCodeAsync(code!, verifier);
                if (accessToken is null || refreshToken is null)
                {
                    var message = exchangeError ?? "No session token received.";
                    await RespondHtmlAsync(context, PageHtml("Sign-in failed", message));
                    return new AuthResult(false, message);
                }

                await RespondHtmlAsync(context, PageHtml("Signed in", "You can close this tab and return to Axis."));
                await client.Auth.SetSession(accessToken, refreshToken);
                return new AuthResult(true);
            }
        }
        catch (System.Exception) when (timeout.IsCancellationRequested)
        {
            return new AuthResult(false, "Google sign-in timed out.");
        }
        catch (System.Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>GoTrue's PKCE code exchange (POST /auth/v1/token?grant_type=pkce). Only the
    /// publishable key goes in <c>apikey</c> — it isn't a JWT, so it must not be sent as a Bearer
    /// token.</summary>
    private static async Task<(string? AccessToken, string? RefreshToken, string? Error)> ExchangeCodeAsync(string code, string verifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/auth/v1/token?grant_type=pkce")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["auth_code"] = code, ["code_verifier"] = verifier }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("apikey", SupabaseConfig.PublishableKey);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (response.IsSuccessStatusCode
                && root.TryGetProperty("access_token", out var access)
                && root.TryGetProperty("refresh_token", out var refresh))
            {
                return (access.GetString(), refresh.GetString(), null);
            }

            var description = root.TryGetProperty("error_description", out var d) ? d.GetString()
                : root.TryGetProperty("msg", out var m) ? m.GetString()
                : null;
            return (null, null, description ?? $"Sign-in failed ({(int)response.StatusCode}).");
        }
        catch (JsonException)
        {
            return (null, null, $"Sign-in failed ({(int)response.StatusCode}).");
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Same dark card look as web/reset & web/invite, for visual consistency.
    private const string Style = """
        <style>
          :root { --bg:#0B1220; --card:#121A2B; --text:#E8ECF4; --muted:#8A93A6; --accent:#3D7EFF; --accent2:#F5A623; --border:#22304A; }
          * { box-sizing: border-box; }
          body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center; background:var(--bg); color:var(--text); font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif; padding:24px; }
          .card { width:100%; max-width:420px; background:var(--card); border:1px solid var(--border); border-radius:20px; padding:32px 28px; text-align:center; }
          .mark { width:56px; height:56px; border-radius:16px; background:linear-gradient(135deg,var(--accent),var(--accent2)); margin:0 auto 20px; }
          h1 { font-size:1.4rem; margin:0 0 8px; }
          p { color:var(--muted); line-height:1.5; margin:0; font-size:0.95rem; }
        </style>
        """;

    private static string PageHtml(string title, string message) => $"""
        <html><head><meta charset="utf-8">{Style}</head><body>
        <div class="card"><div class="mark"></div>
        <h1 id="title">{WebUtility.HtmlEncode(title)}</h1>
        <p id="message">{WebUtility.HtmlEncode(message)}</p>
        </div></body></html>
        """;

    private static async Task RespondHtmlAsync(HttpListenerContext context, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();
    }
}
