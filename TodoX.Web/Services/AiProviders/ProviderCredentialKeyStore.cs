using System.Security.Cryptography;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public interface IProviderCredentialKeyStore
{
    Task<ProviderCredentialKey> GetActiveKeyAsync(CancellationToken ct = default);
    Task<ProviderCredentialKey> GetKeyByVersionAsync(int keyVersion, CancellationToken ct = default);
}

public sealed class ProviderCredentialKeyStore : IProviderCredentialKeyStore
{
    private const long AdvisoryLockKey = 79000179;
    private readonly TodoXConnectionFactory _factory;

    public ProviderCredentialKeyStore(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ProviderCredentialKey> GetActiveKeyAsync(CancellationToken ct = default)
    {
        var existing = await TryLoadActiveKeyAsync(ct);
        if (existing is not null)
        {
            return existing;
        }

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(@lockKey);", new { lockKey = AdvisoryLockKey }, tx);

        var afterLock = await conn.QuerySingleOrDefaultAsync<ProviderCredentialKeyRow>(
            """
            SELECT key_version AS KeyVersion, key_material AS KeyMaterial, algorithm AS Algorithm
              FROM system.ai_provider_credential_master_key
             WHERE status = 'active'
             ORDER BY key_version DESC
             LIMIT 1;
            """, transaction: tx);

        if (afterLock is null)
        {
            var keyMaterial = RandomNumberGenerator.GetBytes(32);
            afterLock = new ProviderCredentialKeyRow { KeyVersion = 1, KeyMaterial = keyMaterial, Algorithm = "AES-256-GCM" };
            await conn.ExecuteAsync(
                """
                INSERT INTO system.ai_provider_credential_master_key
                    (key_version, key_material, algorithm, status, created_at)
                VALUES
                    (@KeyVersion, @KeyMaterial, @Algorithm, 'active', now());
                """, afterLock, tx);
        }

        tx.Commit();
        return ToKey(afterLock);
    }

    public async Task<ProviderCredentialKey> GetKeyByVersionAsync(int keyVersion, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ProviderCredentialKeyRow>(
            """
            SELECT key_version AS KeyVersion, key_material AS KeyMaterial, algorithm AS Algorithm
              FROM system.ai_provider_credential_master_key
             WHERE key_version = @keyVersion
             LIMIT 1;
            """, new { keyVersion });

        return row is null
            ? throw new InvalidOperationException("Credential encryption key is not available.")
            : ToKey(row);
    }

    private async Task<ProviderCredentialKey?> TryLoadActiveKeyAsync(CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ProviderCredentialKeyRow>(
            """
            SELECT key_version AS KeyVersion, key_material AS KeyMaterial, algorithm AS Algorithm
              FROM system.ai_provider_credential_master_key
             WHERE status = 'active'
             ORDER BY key_version DESC
             LIMIT 1;
            """);
        return row is null ? null : ToKey(row);
    }

    private static ProviderCredentialKey ToKey(ProviderCredentialKeyRow row)
    {
        if (row.KeyMaterial.Length != 32)
        {
            throw new InvalidOperationException("Credential encryption key is invalid.");
        }

        return new ProviderCredentialKey(row.KeyVersion, row.KeyMaterial, row.Algorithm);
    }

    private sealed class ProviderCredentialKeyRow
    {
        public int KeyVersion { get; set; }
        public byte[] KeyMaterial { get; set; } = Array.Empty<byte>();
        public string Algorithm { get; set; } = "AES-256-GCM";
    }
}
