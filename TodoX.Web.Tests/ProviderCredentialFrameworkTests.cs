using System.Security.Cryptography;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ProviderCredentialFrameworkTests
{
    [Fact]
    public async Task Protector_EncryptsDecryptsWithRandomNonceAndStableFingerprint()
    {
        var protector = new ProviderCredentialProtector(new FixedKeyStore());
        var first = await protector.ProtectAsync("  redacted-unit-value  ");
        var second = await protector.ProtectAsync("redacted-unit-value");

        Assert.Equal("AES-256-GCM", first.EncryptionAlgorithm);
        Assert.Equal(12, first.Nonce.Length);
        Assert.Equal(16, first.AuthTag.Length);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual("redacted-unit-value", Convert.ToBase64String(first.Ciphertext));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("****alue", first.MaskedHint);

        var decrypted = await protector.UnprotectAsync(ToRecord(first));
        Assert.Equal("redacted-unit-value", decrypted);
    }

    [Fact]
    public async Task Protector_RejectsCorruptedCiphertextWithSanitizedError()
    {
        var protector = new ProviderCredentialProtector(new FixedKeyStore());
        var protectedCredential = await protector.ProtectAsync("redacted-unit-value");
        protectedCredential.Ciphertext[0] ^= 0x01;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => protector.UnprotectAsync(ToRecord(protectedCredential)));
        Assert.Equal("Credential could not be decrypted.", ex.Message);
        Assert.DoesNotContain("redacted-unit-value", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_UsesActiveMappingAndUpdatesLastUsedWithoutLeakingSecret()
    {
        var protector = new ProviderCredentialProtector(new FixedKeyStore());
        var protectedCredential = await protector.ProtectAsync("redacted-unit-value");
        var secure = ToRecord(protectedCredential);
        secure.Id = Guid.NewGuid();
        secure.ProviderAccountId = Guid.NewGuid();
        secure.CredentialRole = "access_token";

        var repo = new FakeProviderCredentialRepository
        {
            Account = new ProviderCredentialAccount
            {
                Id = secure.ProviderAccountId,
                ProviderCode = "79ai",
                Environment = "production",
                Enabled = true,
                IsDefault = true
            },
            Mapping = new ProviderCredentialMapping
            {
                ProviderAccountId = secure.ProviderAccountId,
                CredentialRole = "access_token",
                SecureCredentialId = secure.Id,
                Enabled = true
            },
            Secure = secure
        };
        var resolver = new ProviderCredentialResolver(repo, protector);

        var resolved = await resolver.ResolveAsync("79AI", "access_token");

        Assert.Equal("redacted-unit-value", resolved.Secret);
        Assert.Equal(secure.Id, repo.LastUsedId);
        Assert.DoesNotContain("redacted-unit-value", resolved.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_IgnoresDisabledMappingAndInactiveOrExpiredCredential()
    {
        var protector = new ProviderCredentialProtector(new FixedKeyStore());
        var secure = ToRecord(await protector.ProtectAsync("redacted-unit-value"));
        secure.Id = Guid.NewGuid();
        secure.ProviderAccountId = Guid.NewGuid();

        var repo = new FakeProviderCredentialRepository
        {
            Account = new ProviderCredentialAccount { Id = secure.ProviderAccountId, ProviderCode = "79ai", Enabled = true },
            Mapping = null,
            Secure = secure
        };
        var resolver = new ProviderCredentialResolver(repo, protector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("79ai", "access_token"));

        repo.Mapping = new ProviderCredentialMapping { SecureCredentialId = secure.Id, CredentialRole = "access_token", Enabled = true };
        secure.Status = "inactive";
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("79ai", "access_token"));

        secure.Status = "active";
        secure.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("79ai", "access_token"));
    }

    [Fact]
    public void SourceContracts_UseDbManagedKeyStoreAndSafeSchemaMappings()
    {
        var keyStore = ReadSource("TodoX.Web", "Services", "AiProviders", "ProviderCredentialKeyStore.cs");
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "ProviderCredentialRepository.cs");
        var migration = ReadSource("TodoX.Web", "Services", "AiProviders", "Ai79CredentialMigrationService.cs");
        var page = ReadSource("TodoX.Web", "Components", "Pages", "AiProviders.razor");
        var verifySql = ReadSource("database", "manual", "ai-provider-secure-credentials", "02_verify_79ai_secure_credential.sql");

        Assert.Contains("RandomNumberGenerator.GetBytes(32)", keyStore, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", keyStore, StringComparison.Ordinal);
        Assert.Contains("GetActiveKeyAsync", keyStore, StringComparison.Ordinal);
        Assert.Contains("GetKeyByVersionAsync", keyStore, StringComparison.Ordinal);

        Assert.Contains("system.ai_provider_credentials_secure", repository, StringComparison.Ordinal);
        Assert.Contains("public.todox_ai_provider_account_credential", repository, StringComparison.Ordinal);
        Assert.Contains("public.todox_ai_provider_account", repository, StringComparison.Ordinal);
        Assert.Contains("ORDER BY is_default DESC, priority ASC", repository, StringComparison.Ordinal);
        Assert.Contains("last_used_at = now()", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key_material", repository, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("TodoXAutomationConnectionFactory", migration, StringComparison.Ordinal);
        Assert.Contains("todox_video_79ai_provider_keys", migration, StringComparison.Ordinal);
        Assert.Contains("Bearer ", migration, StringComparison.Ordinal);
        Assert.Contains("UpsertMappingAsync", migration, StringComparison.Ordinal);
        Assert.Contains("SetProviderAccountEnabledDefaultAsync", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE public.todox_video_79ai_provider_keys", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM public.todox_video_79ai_provider_keys", migration, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Khởi tạo credential bảo mật", page, StringComparison.Ordinal);
        Assert.Contains("ShowMessageBoxAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind-Value=\"access", page, StringComparison.OrdinalIgnoreCase);

        foreach (var forbiddenVerificationColumn in new[] { "key_material", "ciphertext", "nonce", "auth_tag" })
        {
            Assert.DoesNotContain(forbiddenVerificationColumn, verifySql, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("SELECT access_token", verifySql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ai79Migration_NormalizesBearerPrefix()
    {
        Assert.Equal("abc", Ai79CredentialMigrationService.NormalizeLegacyToken("Bearer abc"));
        Assert.Equal("abc", Ai79CredentialMigrationService.NormalizeLegacyToken("bearer abc"));
        Assert.Equal("abc", Ai79CredentialMigrationService.NormalizeLegacyToken(" abc "));
    }

    private static ProviderSecureCredentialRecord ToRecord(ProtectedProviderCredential credential)
        => new()
        {
            Id = Guid.NewGuid(),
            ProviderAccountId = Guid.NewGuid(),
            CredentialRole = "access_token",
            Ciphertext = credential.Ciphertext,
            Nonce = credential.Nonce,
            AuthTag = credential.AuthTag,
            EncryptionAlgorithm = credential.EncryptionAlgorithm,
            KeyVersion = credential.KeyVersion,
            TokenFingerprint = credential.Fingerprint,
            MaskedHint = credential.MaskedHint,
            Status = "active",
            ValidFrom = DateTime.UtcNow.AddMinutes(-1)
        };

    private static string ReadSource(params string[] parts)
    {
        var file = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return File.ReadAllText(file);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }

    private sealed class FixedKeyStore : IProviderCredentialKeyStore
    {
        private static readonly byte[] Key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();

        public Task<ProviderCredentialKey> GetActiveKeyAsync(CancellationToken ct = default)
            => Task.FromResult(new ProviderCredentialKey(1, Key, "AES-256-GCM"));

        public Task<ProviderCredentialKey> GetKeyByVersionAsync(int keyVersion, CancellationToken ct = default)
            => Task.FromResult(new ProviderCredentialKey(keyVersion, Key, "AES-256-GCM"));
    }

    private sealed class FakeProviderCredentialRepository : IProviderCredentialRepository
    {
        public ProviderCredentialAccount? Account { get; set; }
        public ProviderCredentialMapping? Mapping { get; set; }
        public ProviderSecureCredentialRecord? Secure { get; set; }
        public Guid? LastUsedId { get; private set; }

        public Task<ProviderCredentialAccount?> GetPreferredAccountAsync(string providerCode, string environment = "production", CancellationToken ct = default)
            => Task.FromResult(Account);

        public Task<ProviderCredentialMapping?> GetActiveMappingAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(Mapping);

        public Task<ProviderSecureCredentialRecord?> GetSecureCredentialAsync(Guid secureCredentialId, CancellationToken ct = default)
            => Task.FromResult(Secure);

        public Task UpdateLastUsedAsync(Guid secureCredentialId, CancellationToken ct = default)
        {
            LastUsedId = secureCredentialId;
            return Task.CompletedTask;
        }

        public Task<ProviderCredentialAccount?> GetAccountByIdAsync(Guid providerAccountId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderSecureCredentialRecord?> GetActiveSecureCredentialAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid> InsertSecureCredentialAsync(Guid providerAccountId, string credentialRole, ProtectedProviderCredential protectedCredential, Guid? userId, string metadataJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeactivatePriorSecureCredentialsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, Guid? userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpsertMappingAsync(Guid providerAccountId, string credentialRole, Guid secureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeactivatePriorMappingsAsync(Guid providerAccountId, string credentialRole, Guid keepSecureCredentialId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetProviderAccountEnabledDefaultAsync(Guid providerAccountId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderAccountCredentialMetadata?> GetCredentialMetadataAsync(Guid providerAccountId, string credentialRole, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
