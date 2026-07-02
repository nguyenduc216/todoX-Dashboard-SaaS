using System.Text.Json;

namespace TodoX.Web.Services;

public sealed record FacebookPageInfo(bool Ok, string? PageId, string? Name, string? Error, string[] Permissions);

/// <summary>
/// Minimal Facebook Graph API client to validate a Page Access Token and read basic page info.
/// Used when a customer pastes a token manually (option 1). No Facebook App required for this path.
/// </summary>
public sealed class FacebookGraphService
{
    private const string GraphBase = "https://graph.facebook.com/v21.0";
    private readonly HttpClient _http;
    private readonly ILogger<FacebookGraphService> _logger;

    public FacebookGraphService(HttpClient http, ILogger<FacebookGraphService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Validates a page access token by calling /me?fields=id,name. Returns page id/name on success.</summary>
    public async Task<FacebookPageInfo> ValidatePageTokenAsync(string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new FacebookPageInfo(false, null, null, "Token trống", Array.Empty<string>());
        }

        try
        {
            var url = $"{GraphBase}/me?fields=id,name,category&access_token={Uri.EscapeDataString(accessToken)}";
            using var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!resp.IsSuccessStatusCode || root.TryGetProperty("error", out var err))
            {
                var msg = root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                    ? m.GetString()
                    : $"HTTP {(int)resp.StatusCode}";
                return new FacebookPageInfo(false, null, null, msg, Array.Empty<string>());
            }

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

            var perms = await GetPermissionsAsync(accessToken, ct);
            return new FacebookPageInfo(true, id, name, null, perms);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Facebook token validation failed");
            return new FacebookPageInfo(false, null, null, ex.Message, Array.Empty<string>());
        }
    }

    private async Task<string[]> GetPermissionsAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            var url = $"{GraphBase}/me/permissions?access_token={Uri.EscapeDataString(accessToken)}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<string>();
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<string>();

            var result = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("status", out var st) && st.GetString() == "granted"
                    && item.TryGetProperty("permission", out var p))
                {
                    var perm = p.GetString();
                    if (perm is not null) result.Add(perm);
                }
            }
            return result.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Masked hint of a token for display, e.g. "EAAB…3f9Q".</summary>
    public static string Hint(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length <= 8) return "••••";
        return $"{token[..4]}…{token[^4..]}";
    }
}
