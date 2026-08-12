using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public interface IProviderCredentialRepository
{
    Task<ProviderCredentialAccount?> GetPreferredAccountAsync(string providerCode, string environment = "production", CancellationToken ct = default);
    Task<ProviderCredentialAccount?> GetAccountByIdAsync(Guid providerAccountId, CancellationToken ct = default);
    Task<ProviderCredentialMapping?> GetActiveMappingAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default);
    Task<ProviderSecureCredentialRecord?> GetSecureCredentialAsync(Guid secureCredentialId, CancellationToken ct = default);
    Task<ProviderSecureCredentialRecord?> GetActiveSecureCredentialAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default);
    Task<Guid> InsertSecureCredentialAsync(Guid providerAccountId, string credentialRole, ProtectedProviderCredential protectedCredential, Guid? userId, string metadataJson, CancellationToken ct = default);
    Task DeactivatePriorSecureCredentialsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, Guid? userId, CancellationToken ct = default);
    Task UpsertMappingAsync(Guid providerAccountId, string credentialRole, Guid secureCredentialId, CancellationToken ct = default);
    Task DeactivatePriorMappingsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, CancellationToken ct = default);
    Task SetProviderAccountEnabledDefaultAsync(Guid providerAccountId, CancellationToken ct = default);
    Task UpdateLastUsedAsync(Guid secureCredentialId, CancellationToken ct = default);
    Task<ProviderAccountCredentialMetadata?> GetCredentialMetadataAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default);
}

public sealed class ProviderCredentialRepository : IProviderCredentialRepository
{
    private readonly TodoXConnectionFactory _factory;

    public ProviderCredentialRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ProviderCredentialAccount?> GetPreferredAccountAsync(string providerCode, string environment = "production", CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderCredentialAccount>(
            """
            SELECT id AS Id, provider_id AS ProviderId, provider_code AS ProviderCode,
                   account_code AS AccountCode, account_name AS AccountName, environment AS Environment,
                   enabled AS Enabled, is_default AS IsDefault, priority AS Priority, config_json::text AS ConfigJson
              FROM public.todox_ai_provider_account
             WHERE lower(btrim(provider_code)) = lower(btrim(@providerCode))
               AND environment = @environment
               AND enabled = true
             ORDER BY is_default DESC, priority ASC, account_name ASC
             LIMIT 1;
            """, new { providerCode, environment });
    }

    public async Task<ProviderCredentialAccount?> GetAccountByIdAsync(Guid providerAccountId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderCredentialAccount>(
            """
            SELECT id AS Id, provider_id AS ProviderId, provider_code AS ProviderCode,
                   account_code AS AccountCode, account_name AS AccountName, environment AS Environment,
                   enabled AS Enabled, is_default AS IsDefault, priority AS Priority, config_json::text AS ConfigJson
              FROM public.todox_ai_provider_account
             WHERE id = @providerAccountId
             LIMIT 1;
            """, new { providerAccountId });
    }

    public async Task<ProviderCredentialMapping?> GetActiveMappingAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderCredentialMapping>(
            """
            SELECT id AS Id, provider_account_id AS ProviderAccountId, secure_credential_id AS SecureCredentialId,
                   credential_role AS CredentialRole, enabled AS Enabled, priority AS Priority
              FROM public.todox_ai_provider_account_credential
             WHERE provider_account_id = @providerAccountId
               AND credential_role = @credentialRole
               AND enabled = true
               AND secure_credential_id IS NOT NULL
             ORDER BY priority ASC, updated_at DESC
             LIMIT 1;
            """, new { providerAccountId, credentialRole });
    }

    public async Task<ProviderSecureCredentialRecord?> GetSecureCredentialAsync(Guid secureCredentialId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderSecureCredentialRecord>(
            """
            SELECT id AS Id, provider_account_id AS ProviderAccountId, credential_role AS CredentialRole,
                   ciphertext AS Ciphertext, nonce AS Nonce, auth_tag AS AuthTag,
                   encryption_algorithm AS EncryptionAlgorithm, key_version AS KeyVersion,
                   token_fingerprint AS TokenFingerprint, masked_hint AS MaskedHint, status AS Status,
                   valid_from AS ValidFrom, expires_at AS ExpiresAt, last_used_at AS LastUsedAt
              FROM system.ai_provider_credentials_secure
             WHERE id = @secureCredentialId
             LIMIT 1;
            """, new { secureCredentialId });
    }

    public async Task<ProviderSecureCredentialRecord?> GetActiveSecureCredentialAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderSecureCredentialRecord>(
            """
            SELECT id AS Id, provider_account_id AS ProviderAccountId, credential_role AS CredentialRole,
                   ciphertext AS Ciphertext, nonce AS Nonce, auth_tag AS AuthTag,
                   encryption_algorithm AS EncryptionAlgorithm, key_version AS KeyVersion,
                   token_fingerprint AS TokenFingerprint, masked_hint AS MaskedHint, status AS Status,
                   valid_from AS ValidFrom, expires_at AS ExpiresAt, last_used_at AS LastUsedAt
              FROM system.ai_provider_credentials_secure
             WHERE provider_account_id = @providerAccountId
               AND credential_role = @credentialRole
               AND status = 'active'
               AND valid_from <= now()
               AND (expires_at IS NULL OR expires_at > now())
             ORDER BY created_at DESC
             LIMIT 1;
            """, new { providerAccountId, credentialRole });
    }

    public async Task<Guid> InsertSecureCredentialAsync(Guid providerAccountId, string credentialRole, ProtectedProviderCredential protectedCredential, Guid? userId, string metadataJson, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO system.ai_provider_credentials_secure
                (provider_account_id, credential_role, ciphertext, nonce, auth_tag,
                 encryption_algorithm, key_version, token_fingerprint, masked_hint, status,
                 valid_from, created_by, updated_by, metadata_json, created_at, updated_at)
            VALUES
                (@providerAccountId, @credentialRole, @Ciphertext, @Nonce, @AuthTag,
                 @EncryptionAlgorithm, @KeyVersion, @Fingerprint, @MaskedHint, 'active',
                 now(), @userId, @userId, CAST(@metadataJson AS jsonb), now(), now())
            RETURNING id;
            """, new
            {
                providerAccountId,
                credentialRole,
                protectedCredential.Ciphertext,
                protectedCredential.Nonce,
                protectedCredential.AuthTag,
                protectedCredential.EncryptionAlgorithm,
                protectedCredential.KeyVersion,
                protectedCredential.Fingerprint,
                protectedCredential.MaskedHint,
                userId,
                metadataJson
            });
    }

    public async Task DeactivatePriorSecureCredentialsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, Guid? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE system.ai_provider_credentials_secure
               SET status = 'inactive', updated_by = @userId, updated_at = now()
             WHERE provider_account_id = @providerAccountId
               AND credential_role = @credentialRole
               AND id <> @keepSecureCredentialId
               AND status = 'active';
            """, new { providerAccountId, credentialRole, keepSecureCredentialId, userId });
    }

    public async Task UpsertMappingAsync(Guid providerAccountId, string credentialRole, Guid secureCredentialId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var updated = await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account_credential
               SET secure_credential_id = @secureCredentialId,
                   enabled = true,
                   priority = LEAST(priority, 1),
                   updated_at = now()
             WHERE id = (
                   SELECT id
                     FROM public.todox_ai_provider_account_credential
                    WHERE provider_account_id = @providerAccountId
                      AND credential_role = @credentialRole
                    ORDER BY enabled DESC, priority ASC, updated_at DESC
                    LIMIT 1
             );
            """, new { providerAccountId, credentialRole, secureCredentialId }, tx);

        if (updated == 0)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.todox_ai_provider_account_credential
                    (provider_account_id, credential_role, secure_credential_id, enabled, priority, metadata_json, created_at, updated_at)
                VALUES
                    (@providerAccountId, @credentialRole, @secureCredentialId, true, 1, '{}'::jsonb, now(), now());
                """, new { providerAccountId, credentialRole, secureCredentialId }, tx);
        }

        tx.Commit();
    }

    public async Task DeactivatePriorMappingsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account_credential
               SET enabled = false, updated_at = now()
             WHERE provider_account_id = @providerAccountId
               AND credential_role = @credentialRole
               AND secure_credential_id IS DISTINCT FROM @keepSecureCredentialId;
            """, new { providerAccountId, credentialRole, keepSecureCredentialId });
    }

    public async Task SetProviderAccountEnabledDefaultAsync(Guid providerAccountId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var account = await conn.QuerySingleAsync<ProviderCredentialAccount>(
            """
            SELECT id AS Id, provider_id AS ProviderId, provider_code AS ProviderCode,
                   account_code AS AccountCode, account_name AS AccountName, environment AS Environment,
                   enabled AS Enabled, is_default AS IsDefault, priority AS Priority, config_json::text AS ConfigJson
              FROM public.todox_ai_provider_account
             WHERE id = @providerAccountId
             LIMIT 1;
            """, new { providerAccountId }, tx);

        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account
               SET is_default = false, updated_at = now()
             WHERE provider_code = @ProviderCode
               AND environment = @Environment
               AND id <> @Id;
            """, account, tx);

        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_account
               SET enabled = true,
                   is_default = true,
                   health_status = 'unknown',
                   updated_at = now()
             WHERE id = @Id;
            """, account, tx);

        tx.Commit();
    }

    public async Task UpdateLastUsedAsync(Guid secureCredentialId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE system.ai_provider_credentials_secure
               SET last_used_at = now(), updated_at = now()
             WHERE id = @secureCredentialId;
            """, new { secureCredentialId });
    }

    public async Task<ProviderAccountCredentialMetadata?> GetCredentialMetadataAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderAccountCredentialMetadata>(
            """
            SELECT a.id AS ProviderAccountId, a.provider_code AS ProviderCode, a.account_code AS AccountCode,
                   a.account_name AS AccountName, a.environment AS Environment, a.enabled AS AccountEnabled,
                   a.is_default AS AccountDefault, m.enabled AS MappingEnabled,
                   m.secure_credential_id AS SecureCredentialId, COALESCE(m.credential_role, @credentialRole) AS CredentialRole,
                   s.token_fingerprint AS TokenFingerprint, s.masked_hint AS MaskedHint,
                   s.status AS SecureStatus, s.encryption_algorithm AS EncryptionAlgorithm,
                   s.key_version AS KeyVersion, s.valid_from AS ValidFrom, s.expires_at AS ExpiresAt,
                   s.last_used_at AS LastUsedAt
              FROM public.todox_ai_provider_account a
              LEFT JOIN public.todox_ai_provider_account_credential m
                ON m.provider_account_id = a.id AND m.credential_role = @credentialRole AND m.enabled = true
              LEFT JOIN system.ai_provider_credentials_secure s
                ON s.id = m.secure_credential_id
             WHERE a.id = @providerAccountId
             ORDER BY m.priority ASC NULLS LAST, s.created_at DESC NULLS LAST
             LIMIT 1;
            """, new { providerAccountId, credentialRole });
    }
}
