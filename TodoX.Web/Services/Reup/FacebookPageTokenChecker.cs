using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class FacebookPageTokenChecker
{
    private readonly HttpClient _http;
    private readonly TodoXConnectionFactory _factory;

    public FacebookPageTokenChecker(HttpClient http, TodoXConnectionFactory factory)
    {
        _http = http;
        _factory = factory;
    }

    public async Task<FacebookTokenCheckResult> CheckAsync(Guid pageId, string externalPageId, ReupPageTokenDto token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalPageId))
        {
            return new(false, "PAGE_EXTERNAL_ID_MISSING", "Facebook Page chưa có external_page_id.");
        }

        try
        {
            var url = $"https://graph.facebook.com/{Uri.EscapeDataString(externalPageId)}?fields=id,name&access_token={Uri.EscapeDataString(token.TokenValue)}";
            using var response = await _http.GetAsync(url, ct);
            var ok = response.IsSuccessStatusCode;
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                UPDATE social.page_access_tokens
                   SET last_validated_at = now(),
                       last_validation_status = @status,
                       updated_at = now()
                 WHERE id = @tokenId;

                UPDATE social.customer_pages
                   SET last_checked_at = now(),
                       verification_status = @verification,
                       updated_at = now()
                 WHERE id = @pageId;
                """,
                new
                {
                    tokenId = token.Id,
                    pageId,
                    status = ok ? "valid" : "invalid",
                    verification = ok ? "verified" : "failed"
                });

            return ok
                ? new(true, null, null)
                : new(false, "PAGE_TOKEN_INVALID", "Token Facebook Page không hợp lệ hoặc đã bị thu hồi.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return new(false, "FACEBOOK_TIMEOUT", ex.Message);
        }
        catch (Exception ex)
        {
            return new(false, "PAGE_ACCESS_DENIED", ex.Message);
        }
    }
}
