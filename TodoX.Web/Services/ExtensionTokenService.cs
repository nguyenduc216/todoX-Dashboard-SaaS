using System.Security.Cryptography;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class ExtensionTokenService
{
    private readonly TodoXConnectionFactory _factory;

    public ExtensionTokenService(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ExtensionIssueResult> IssueAsync(Guid customerId, Guid userId, string? name = null, CancellationToken ct = default)
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var rawToken = $"tdx_{random}";
        var hash = HashToken(rawToken);
        var prefix = rawToken.Length <= 12 ? rawToken : rawToken[..12];

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO content.extension_tokens
                (id, customer_id, user_id, token_hash, token_prefix, name, is_active, created_at)
            VALUES
                (gen_random_uuid(), @customerId, @userId, @hash, @prefix, @name, true, now());
            """,
            new
            {
                customerId,
                userId,
                hash,
                prefix,
                name = string.IsNullOrWhiteSpace(name) ? "TodoX Chrome Extension" : name.Trim()
            });

        return new ExtensionIssueResult(rawToken, prefix);
    }

    public async Task<ExtensionTokenValidationResult> ValidateAsync(string? rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new ExtensionTokenValidationResult();
        }

        var hash = HashToken(rawToken.Trim());
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ExtensionTokenValidationResult>(
            """
            SELECT t.customer_id AS CustomerId,
                   t.user_id AS UserId,
                   c.company_name AS CustomerName,
                   COALESCE(u.email, u.username, u.display_name) AS UserEmail,
                   true AS IsValid
              FROM content.extension_tokens t
              LEFT JOIN crm.customers c ON c.id = t.customer_id
              LEFT JOIN auth.app_users u ON u.id = t.user_id
             WHERE t.token_hash = @hash
               AND t.is_active = true
               AND t.revoked_at IS NULL
               AND (t.expires_at IS NULL OR t.expires_at > now())
             LIMIT 1;
            """,
            new { hash });

        if (row is null)
        {
            return new ExtensionTokenValidationResult();
        }

        await conn.ExecuteAsync(
            "UPDATE content.extension_tokens SET last_used_at = now() WHERE token_hash = @hash;",
            new { hash });

        return row;
    }

    public static string? ReadToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth["Bearer ".Length..].Trim();
        }

        var fallback = request.Headers["X-TodoX-Extension-Token"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    private static string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
