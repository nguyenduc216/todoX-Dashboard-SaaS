using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed class CustomerPageDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Platform { get; set; } = "facebook";
    public string PageName { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? ExternalPageId { get; set; }
    public string? Username { get; set; }
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "pending";
    public string VerificationStatus { get; set; } = "not_verified";
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public bool HasToken { get; set; }
    public string? TokenHint { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Read/write access to social.* (customer_pages, page_access_tokens, page_oauth_connections).
/// Schema installed by V003. All rows are tenant-scoped and customer-scoped.
/// </summary>
public sealed class SocialPageRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public SocialPageRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<CustomerPageDto>> GetPagesAsync(Guid customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerPageDto>(
            """
            SELECT p.id AS Id, p.customer_id AS CustomerId, p.platform AS Platform,
                   p.page_name AS PageName, p.page_url AS PageUrl, p.external_page_id AS ExternalPageId,
                   p.username AS Username, p.avatar_url AS AvatarUrl, p.status AS Status,
                   p.verification_status AS VerificationStatus, p.connected_at AS ConnectedAt,
                   p.last_checked_at AS LastCheckedAt, p.created_at AS CreatedAt,
                   EXISTS(SELECT 1 FROM social.page_access_tokens t
                           WHERE t.page_id = p.id AND t.status='active' AND t.token_value IS NOT NULL) AS HasToken,
                   (SELECT t.token_hint FROM social.page_access_tokens t
                     WHERE t.page_id = p.id AND t.status='active' ORDER BY t.created_at DESC LIMIT 1) AS TokenHint
              FROM social.customer_pages p
             WHERE p.tenant_id = @tenant AND p.customer_id = @cid
             ORDER BY p.created_at DESC;
            """, new { tenant = _tenant.TenantId, cid = customerId });
        return rows.ToList();
    }

    public async Task<Guid> InsertPageAsync(CustomerPageDto p, Guid createdBy)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO social.customer_pages
                (id, tenant_id, customer_id, platform, page_name, page_url, external_page_id,
                 username, status, verification_status, metadata, created_at, created_by)
            VALUES
                (@id, @tenant, @cid, @platform, @name, @url, @extid,
                 @username, @status, @vstatus, '{}'::jsonb, now(), @by);
            """,
            new
            {
                id, tenant = _tenant.TenantId, cid = p.CustomerId, platform = p.Platform,
                name = p.PageName, url = p.PageUrl, extid = p.ExternalPageId, username = p.Username,
                status = p.Status, vstatus = p.VerificationStatus, by = createdBy
            });
        return id;
    }

    public async Task UpdatePageAsync(CustomerPageDto p)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE social.customer_pages
               SET page_name=@name, page_url=@url, external_page_id=@extid,
                   username=@username, platform=@platform, updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id = p.Id, name = p.PageName, url = p.PageUrl, extid = p.ExternalPageId,
                username = p.Username, platform = p.Platform
            });
    }

    public async Task DeletePageAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        // page_access_tokens & page_oauth_connections cascade on page delete.
        await conn.ExecuteAsync("DELETE FROM social.customer_pages WHERE id=@id;", new { id });
    }

    public async Task SetVerificationAsync(Guid pageId, string verificationStatus, string pageStatus, string? externalPageId, string? username)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE social.customer_pages
               SET verification_status=@vstatus, status=@status,
                   external_page_id=COALESCE(@extid, external_page_id),
                   username=COALESCE(@username, username),
                   connected_at = CASE WHEN @vstatus='verified' THEN now() ELSE connected_at END,
                   last_checked_at=now(), updated_at=now()
             WHERE id=@id;
            """,
            new { id = pageId, vstatus = verificationStatus, status = pageStatus, extid = externalPageId, username });
    }

    /// <summary>Upsert the manual/verified page access token for a page.</summary>
    public async Task SaveAccessTokenAsync(Guid pageId, Guid customerId, string platform,
        string tokenValue, string? tokenHint, string validationStatus, IEnumerable<string>? permissions = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var permJson = System.Text.Json.JsonSerializer.Serialize(permissions ?? Array.Empty<string>());

        // Deactivate previous tokens, then insert the new active one.
        await conn.ExecuteAsync(
            "UPDATE social.page_access_tokens SET status='revoked', updated_at=now() WHERE page_id=@pid AND status='active';",
            new { pid = pageId });

        await conn.ExecuteAsync(
            """
            INSERT INTO social.page_access_tokens
                (id, tenant_id, customer_id, page_id, platform, token_value, token_hint,
                 status, permissions, last_validated_at, last_validation_status, created_at)
            VALUES
                (gen_random_uuid(), @tenant, @cid, @pid, @platform, @token, @hint,
                 'active', @perm::jsonb, now(), @vstatus, now());
            """,
            new
            {
                tenant = _tenant.TenantId, cid = customerId, pid = pageId, platform,
                token = tokenValue, hint = tokenHint, perm = permJson, vstatus = validationStatus
            });
    }

    public async Task UpsertFacebookPageFromOAuthAsync(
        Guid customerId,
        Guid createdBy,
        string externalPageId,
        string pageName,
        string? pageAccessToken,
        CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync(ct);

        var pageId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT id
              FROM social.customer_pages
             WHERE tenant_id = @tenant
               AND customer_id = @cid
               AND platform = 'facebook'
               AND external_page_id = @externalPageId
             ORDER BY created_at DESC
             LIMIT 1;
            """,
            new { tenant = _tenant.TenantId, cid = customerId, externalPageId });

        if (pageId is null)
        {
            pageId = Guid.NewGuid();
            await conn.ExecuteAsync(
                """
                INSERT INTO social.customer_pages
                    (id, tenant_id, customer_id, platform, page_name, page_url, external_page_id,
                     username, status, verification_status, metadata, connected_at, last_checked_at, created_at, created_by)
                VALUES
                    (@id, @tenant, @cid, 'facebook', @name, @url, @externalPageId,
                     @externalPageId, 'active', 'verified', '{}'::jsonb, now(), now(), now(), @by);
                """,
                new
                {
                    id = pageId.Value,
                    tenant = _tenant.TenantId,
                    cid = customerId,
                    name = pageName,
                    url = $"https://facebook.com/{externalPageId}",
                    externalPageId,
                    by = createdBy
                });
        }
        else
        {
            await conn.ExecuteAsync(
                """
                UPDATE social.customer_pages
                   SET page_name = @name,
                       page_url = COALESCE(page_url, @url),
                       username = COALESCE(username, @externalPageId),
                       status = 'active',
                       verification_status = 'verified',
                       connected_at = COALESCE(connected_at, now()),
                       last_checked_at = now(),
                       updated_at = now()
                 WHERE id = @id;
                """,
                new
                {
                    id = pageId.Value,
                    name = pageName,
                    url = $"https://facebook.com/{externalPageId}",
                    externalPageId
                });
        }

        if (!string.IsNullOrWhiteSpace(pageAccessToken))
        {
            await SaveAccessTokenAsync(
                pageId.Value,
                customerId,
                "facebook",
                pageAccessToken.Trim(),
                FacebookGraphService.Hint(pageAccessToken.Trim()),
                "valid",
                new[] { "oauth" });
        }
    }
}
