using Dapper;
using Microsoft.AspNetCore.DataProtection;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public interface IAiProviderCredentialStore
{
    Task<string?> GetSecretAsync(long providerId, Guid? providerAccountId, string secretName, CancellationToken ct = default);
    Task SaveSecretAsync(long providerId, Guid? providerAccountId, string secretName, string secretValue, CancellationToken ct = default);
}

public sealed class AiProviderCredentialStore : IAiProviderCredentialStore
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IDataProtector _protector;

    public AiProviderCredentialStore(TodoXConnectionFactory factory, IDataProtectionProvider protectionProvider)
    {
        _factory = factory;
        _protector = protectionProvider.CreateProtector("TodoX.Web.AiProviderCredentialStore.v1");
    }

    public async Task<string?> GetSecretAsync(long providerId, Guid? providerAccountId, string secretName, CancellationToken ct = default)
    {
        if (providerAccountId is null || string.IsNullOrWhiteSpace(secretName))
        {
            return null;
        }

        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CredentialSecretRow>(
            """
            SELECT secret_value_protected AS SecretValueProtected
              FROM public.todox_ai_provider_account_secret
             WHERE provider_id = @providerId
               AND provider_account_id = @providerAccountId
               AND secret_name = @secretName
               AND enabled = true
             ORDER BY updated_at DESC, created_at DESC
             LIMIT 1;
            """,
            new { providerId, providerAccountId, secretName });

        if (row is null || string.IsNullOrWhiteSpace(row.SecretValueProtected))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(row.SecretValueProtected);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveSecretAsync(long providerId, Guid? providerAccountId, string secretName, string secretValue, CancellationToken ct = default)
    {
        if (providerAccountId is null || string.IsNullOrWhiteSpace(secretName))
        {
            throw new InvalidOperationException("AI_PROVIDER_SECRET_TARGET_REQUIRED");
        }

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO public.todox_ai_provider_account_secret
                (id, provider_id, provider_account_id, secret_name, secret_value_protected, enabled, created_at, updated_at)
            VALUES
                (gen_random_uuid(), @providerId, @providerAccountId, @secretName, @secretValueProtected, true, now(), now())
            ON CONFLICT (provider_id, provider_account_id, secret_name)
            DO UPDATE SET secret_value_protected = EXCLUDED.secret_value_protected,
                          enabled = true,
                          updated_at = now();
            """,
            new
            {
                providerId,
                providerAccountId,
                secretName,
                secretValueProtected = _protector.Protect(secretValue)
            });
    }

    private sealed class CredentialSecretRow
    {
        public string? SecretValueProtected { get; init; }
    }
}
