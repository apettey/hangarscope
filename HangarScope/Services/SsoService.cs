using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HangarScope.Models;

namespace HangarScope.Services;

/// <summary>EVE SSO OAuth2 authorization-code + PKCE flow with a localhost callback listener.</summary>
public sealed class SsoService
{
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize/";
    private const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";
    private readonly HttpClient _http = new();

    public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, long CharacterId, string CharacterName, List<string> Scopes);

    /// <summary>Runs the full interactive flow: opens the system browser, waits for the callback, exchanges the code.</summary>
    public async Task<TokenSet> Authenticate(string clientId, string? secret, int port, CancellationToken ct)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var redirect = $"http://localhost:{port}/sso/callback";

        var url = AuthorizeUrl +
            "?response_type=code" +
            "&redirect_uri=" + Uri.EscapeDataString(redirect) +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&scope=" + Uri.EscapeDataString(string.Join(' ', EsiScopes.Required)) +
            "&code_challenge=" + challenge +
            "&code_challenge_method=S256" +
            "&state=" + state;

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/sso/");
        listener.Start();

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var ctxTask = listener.GetContextAsync();
        var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(5), ct));
        if (done != ctxTask) throw new TimeoutException("SSO callback timed out after 5 minutes.");
        var ctx = await ctxTask;

        var query = ctx.Request.QueryString;
        var code = query["code"];
        var gotState = query["state"];
        var page = code != null && gotState == state
            ? "<html><body style='background:#0b0d10;color:#8bd450;font-family:monospace;padding:40px'>HANGARSCOPE — character linked. You can close this tab.</body></html>"
            : "<html><body style='background:#0b0d10;color:#c0533f;font-family:monospace;padding:40px'>HANGARSCOPE — authorization failed.</body></html>";
        var bytes = Encoding.UTF8.GetBytes(page);
        ctx.Response.ContentType = "text/html";
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();

        if (code == null || gotState != state) throw new InvalidOperationException("SSO authorization was denied or the state did not match.");

        return await Exchange(clientId, secret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
        }, ct);
    }

    public Task<TokenSet> Refresh(string clientId, string? secret, string refreshToken, CancellationToken ct) =>
        Exchange(clientId, secret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    private async Task<TokenSet> Exchange(string clientId, string? secret, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        if (!string.IsNullOrEmpty(secret))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{secret}")));
        }
        else
        {
            form["client_id"] = clientId;
        }
        req.Content = new FormUrlEncodedContent(form);
        req.Headers.Host = "login.eveonline.com";

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"SSO token exchange failed ({(int)res.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var access = doc.RootElement.GetProperty("access_token").GetString()!;
        var refresh = doc.RootElement.GetProperty("refresh_token").GetString()!;
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

        var (charId, charName, scopes) = DecodeJwt(access);
        return new TokenSet(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn - 30), charId, charName, scopes);
    }

    private static (long id, string name, List<string> scopes) DecodeJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        payload = payload.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = doc.RootElement;
        var sub = root.GetProperty("sub").GetString()!; // "CHARACTER:EVE:12345"
        var id = long.Parse(sub.Split(':')[^1]);
        var name = root.GetProperty("name").GetString() ?? "Unknown";
        var scopes = new List<string>();
        if (root.TryGetProperty("scp", out var scp))
        {
            if (scp.ValueKind == JsonValueKind.Array) scopes.AddRange(scp.EnumerateArray().Select(s => s.GetString()!));
            else if (scp.GetString() is { } one) scopes.Add(one);
        }
        return (id, name, scopes);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
