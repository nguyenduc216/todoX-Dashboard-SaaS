using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace TodoX.Web.Services;

public sealed record FacebookOAuthResult(bool Success, string Message);

public sealed class FacebookOAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly SocialPageRepository _pages;
    private readonly ILogger<FacebookOAuthService> _logger;

    public FacebookOAuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        SocialPageRepository pages,
        ILogger<FacebookOAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _pages = pages;
        _logger = logger;
    }

    public bool IsEnabled =>
        string.Equals(_configuration["Facebook:OAuthEnabled"], "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_configuration["Facebook:AppId"])
        && !string.IsNullOrWhiteSpace(_configuration["Facebook:AppSecret"]);

    public string BuildLoginUrl(Guid customerId, Guid userId)
    {
        var appId = _configuration["Facebook:AppId"];
        var redirectUri = _configuration["Facebook:RedirectUri"] ?? "https://dashboard.todox.vn/auth/facebook/callback";
        var graphVersion = _configuration["Facebook:GraphVersion"] ?? "v21.0";
        var scopes = _configuration["Facebook:Scopes"] ?? "pages_show_list,pages_read_engagement,pages_manage_posts,pages_manage_metadata";

        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new InvalidOperationException("Chưa cấu hình Facebook App ID.");
        }

        // TODO: Replace this temporary state with one-time nonce storage in DB/session/cache before production OAuth is enabled broadly.
        var statePayload = $"{customerId}|{userId}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(statePayload));

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = scopes,
            ["response_type"] = "code"
        };

        return QueryHelpers.AddQueryString($"https://www.facebook.com/{graphVersion}/dialog/oauth", query);
    }

    public async Task<FacebookOAuthResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
    {
        if (!TryDecodeState(state, out var customerId, out var userId))
        {
            return new FacebookOAuthResult(false, "State OAuth không hợp lệ.");
        }

        var appId = _configuration["Facebook:AppId"];
        var appSecret = _configuration["Facebook:AppSecret"];
        var redirectUri = _configuration["Facebook:RedirectUri"] ?? "https://dashboard.todox.vn/auth/facebook/callback";
        var graphVersion = _configuration["Facebook:GraphVersion"] ?? "v21.0";

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            return new FacebookOAuthResult(false, "Chưa cấu hình Facebook App ID/App Secret.");
        }

        var client = _httpClientFactory.CreateClient();

        var tokenUrl = QueryHelpers.AddQueryString(
            $"https://graph.facebook.com/{graphVersion}/oauth/access_token",
            new Dictionary<string, string?>
            {
                ["client_id"] = appId,
                ["client_secret"] = appSecret,
                ["redirect_uri"] = redirectUri,
                ["code"] = code
            });

        FacebookTokenResponse? tokenResponse;
        try
        {
            tokenResponse = await client.GetFromJsonAsync<FacebookTokenResponse>(tokenUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Facebook OAuth token exchange failed");
            return new FacebookOAuthResult(false, "Không đổi được Facebook code sang access token.");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            return new FacebookOAuthResult(false, "Không lấy được Facebook access token.");
        }

        var accountsUrl = QueryHelpers.AddQueryString(
            $"https://graph.facebook.com/{graphVersion}/me/accounts",
            new Dictionary<string, string?>
            {
                ["fields"] = "id,name,access_token,category,tasks,perms",
                ["access_token"] = tokenResponse.AccessToken
            });

        FacebookAccountsResponse? accounts;
        try
        {
            accounts = await client.GetFromJsonAsync<FacebookAccountsResponse>(accountsUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Facebook page list request failed");
            return new FacebookOAuthResult(false, "Không lấy được danh sách Facebook Page.");
        }

        if (accounts?.Data is null || accounts.Data.Count == 0)
        {
            return new FacebookOAuthResult(false, "Facebook không trả về Page nào. Vui lòng kiểm tra quyền pages_show_list và quyền quản trị Page.");
        }

        var saved = 0;
        foreach (var page in accounts.Data)
        {
            if (string.IsNullOrWhiteSpace(page.Id) || string.IsNullOrWhiteSpace(page.Name))
            {
                continue;
            }

            await _pages.UpsertFacebookPageFromOAuthAsync(
                customerId,
                userId,
                page.Id,
                page.Name,
                page.AccessToken,
                ct);
            saved++;
        }

        return saved > 0
            ? new FacebookOAuthResult(true, $"Đã kết nối {saved} Facebook Page.")
            : new FacebookOAuthResult(false, "Không có Facebook Page hợp lệ để lưu.");
    }

    private static bool TryDecodeState(string state, out Guid customerId, out Guid userId)
    {
        customerId = Guid.Empty;
        userId = Guid.Empty;

        try
        {
            var stateText = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var stateParts = stateText.Split('|');
            return stateParts.Length >= 2
                && Guid.TryParse(stateParts[0], out customerId)
                && Guid.TryParse(stateParts[1], out userId);
        }
        catch
        {
            return false;
        }
    }

    private sealed class FacebookTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class FacebookAccountsResponse
    {
        [JsonPropertyName("data")]
        public List<FacebookPageItem> Data { get; set; } = new();
    }

    private sealed class FacebookPageItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
