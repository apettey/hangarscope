using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HangarScope.Services;

/// <summary>Thin ESI HTTP client: base URL, ETag caching, X-Pages pagination, error-limit backoff.</summary>
public sealed class EsiClient
{
    public const string BaseUrl = "https://esi.evetech.net/latest";
    private readonly HttpClient _http;
    private readonly Dictionary<string, (string etag, string body)> _etags = new();
    private readonly SemaphoreSlim _gate = new(8);

    public EsiClient()
    {
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HangarScope/0.1 (self-hosted asset ledger)");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<JsonDocument> GetJson(string path, string? token = null, CancellationToken ct = default)
    {
        var (body, _) = await GetRaw(path, token, ct);
        return JsonDocument.Parse(body);
    }

    public async Task<(string body, int pages)> GetRaw(string path, string? token = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var cacheKey = path + (token != null ? "|auth" : "");
                if (_etags.TryGetValue(cacheKey, out var cached))
                    req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.etag));

                using var res = await _http.SendAsync(req, ct);

                if (res.StatusCode == HttpStatusCode.NotModified)
                    return (cached.body, 1);

                if ((int)res.StatusCode == 420 || (int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                {
                    if (attempt >= 3) res.EnsureSuccessStatusCode();
                    var wait = res.Headers.TryGetValues("X-Esi-Error-Limit-Reset", out var v) && int.TryParse(v.FirstOrDefault(), out var s)
                        ? s : (attempt + 1) * 5;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 60)), ct);
                    continue;
                }

                res.EnsureSuccessStatusCode();
                var body = await res.Content.ReadAsStringAsync(ct);
                var pages = res.Headers.TryGetValues("X-Pages", out var pv) && int.TryParse(pv.FirstOrDefault(), out var p) ? p : 1;
                if (res.Headers.ETag is { } etag)
                    _etags[cacheKey] = (etag.Tag, body);
                return (body, pages);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>GET all pages of a paginated endpoint and concatenate the JSON arrays.</summary>
    public async Task<List<JsonElement>> GetPaged(string path, string? token = null, CancellationToken ct = default)
    {
        var sep = path.Contains('?') ? "&" : "?";
        var (first, pages) = await GetRaw($"{path}{sep}page=1", token, ct);
        var items = JsonDocument.Parse(first).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        if (pages > 1)
        {
            var tasks = Enumerable.Range(2, pages - 1)
                .Select(p => GetRaw($"{path}{sep}page={p}", token, ct)).ToList();
            foreach (var t in tasks)
            {
                var (body, _) = await t;
                items.AddRange(JsonDocument.Parse(body).RootElement.EnumerateArray().Select(e => e.Clone()));
            }
        }
        return items;
    }

    public async Task<JsonDocument> PostJson(string path, object payload, string? token = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        }
        finally { _gate.Release(); }
    }

    public HttpClient Http => _http;
}
