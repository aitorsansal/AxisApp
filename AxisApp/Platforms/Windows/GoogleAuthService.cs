using System.Net;
using System.Text;
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
/// out expiry/staleness). A manual browser test of the exact same round trip — no PKCE, no
/// explicit state, letting Supabase generate and manage its own state entirely — completed
/// cleanly. That isolates the bug to gotrue-csharp's PKCE/state construction specifically, which
/// matches an open, unresolved issue (supabase-community/supabase-csharp#222) about that SDK's
/// PKCE state handling not round-tripping correctly. So this builds the authorize URL by hand
/// instead, using the plain implicit flow that's confirmed to work.
///
/// The cost: implicit flow returns the token in the URL *fragment*, which browsers never send to
/// a server. The loopback listener below serves a tiny page whose JS reads location.hash and
/// re-submits it as a real query string to a second local request, which the listener can
/// actually read.</summary>
public class GoogleAuthService : IGoogleAuthService
{
    // Deliberately "localhost", not "127.0.0.1" — Supabase's own ecosystem has documented
    // sensitivity to this exact distinction, even though HttpListener treats both as equivalent
    // loopback binds on Windows.
    private const string RedirectUri = "http://localhost:48291/";

    public async Task<AuthResult> SignInAsync(Supabase.Client client)
    {
        var authorizeUri = $"{SupabaseConfig.Url}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(RedirectUri)}";

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

            // First hit is either a genuine query-string error from Supabase's own callback (the
            // shape bad_oauth_state used to arrive as), or a bare navigation carrying the real
            // result only in the fragment — invisible here until the extractor page below
            // reflects it back as a real query string on a second request.
            var first = await listener.GetContextAsync();
            var firstError = first.Request.QueryString["error_description"] ?? first.Request.QueryString["error"];
            if (firstError is not null)
            {
                await RespondHtmlAsync(first, PageHtml("Sign-in failed", firstError));
                return new AuthResult(false, firstError);
            }

            await RespondHtmlAsync(first, ExtractorHtml);

            // This second response is read via the extractor page's own fetch() call, not shown
            // as a page navigation — its body is plain text that JS injects into the page the
            // user is actually looking at, not a full HTML document of its own.
            var second = await listener.GetContextAsync();
            var query = second.Request.QueryString;
            var accessToken = query["access_token"];
            var refreshToken = query["refresh_token"];
            var error = query["error_description"] ?? query["error"];

            await RespondTextAsync(second, error ?? "ok");

            if (error is not null) return new AuthResult(false, error);
            if (accessToken is null || refreshToken is null) return new AuthResult(false, "No session token received.");

            await client.Auth.SetSession(accessToken, refreshToken);
            return new AuthResult(true);
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

    // Same dark card look as web/reset & web/invite, for visual consistency — this is the one
    // page the user actually sees; the second response never renders as a page (see below).
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

    // Not an interpolated raw string ($"""...""") — the JS below is full of literal braces, and
    // raw-string interpolation needs escalating $ counts to escape those, which gets unreadable
    // fast. Plain concatenation for the one actual hole (Style) instead.
    private static readonly string ExtractorHtml = $"<html><head><meta charset=\"utf-8\">{Style}</head><body>" + """
        <div class="card"><div class="mark"></div>
        <h1 id="title">Finishing sign-in…</h1>
        <p id="message">Just a moment.</p>
        </div>
        <script>
          var params = location.hash ? location.hash.substring(1) : location.search.substring(1);
          fetch('/token?' + params)
            .then(function (r) { return r.text(); })
            .then(function (result) {
              var ok = result === 'ok';
              document.getElementById('title').textContent = ok ? 'Signed in' : 'Sign-in failed';
              document.getElementById('message').textContent = ok
                ? 'You can close this tab and return to Axis.'
                : result;
            })
            .catch(function () {
              document.getElementById('title').textContent = 'Sign-in failed';
              document.getElementById('message').textContent = 'Could not reach Axis — you can close this tab and try again.';
            });
        </script>
        </body></html>
        """;

    private static async Task RespondHtmlAsync(HttpListenerContext context, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();
    }

    private static async Task RespondTextAsync(HttpListenerContext context, string text)
    {
        var buffer = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();
    }
}
