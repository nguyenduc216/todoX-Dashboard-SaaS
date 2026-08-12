using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public interface IAi79CredentialMigrationService
{
    Task<Ai79CredentialMigrationResult> MigrateAsync(Guid? userId = null, CancellationToken ct = default);
    Task<ProviderAccountCredentialMetadata?> GetMetadataAsync(CancellationToken ct = default);
}

public sealed class Ai79CredentialMigrationService : IAi79CredentialMigrationService
{
    public static readonly Guid TargetAccountId = Guid.Parse("5ab72966-c0a7-40b0-b8db-c5c85b39e407");
    private const string ProviderCode = "79ai";
    private const string CredentialRole = "access_token";
    private readonly TodoXAutomationConnectionFactory _automationFactory;
    private readonly IProviderCredentialProtector _protector;
    private readonly IProviderCredentialRepository _repository;

    public Ai79CredentialMigrationService(
        TodoXAutomationConnectionFactory automationFactory,
        IProviderCredentialProtector protector,
        IProviderCredentialRepository repository)
    {
        _automationFactory = automationFactory;
        _protector = protector;
        _repository = repository;
    }

    public Task<ProviderAccountCredentialMetadata?> GetMetadataAsync(CancellationToken ct = default)
        => _repository.GetCredentialMetadataAsync(TargetAccountId, CredentialRole, ct);

    public async Task<Ai79CredentialMigrationResult> MigrateAsync(Guid? userId = null, CancellationToken ct = default)
    {
        var legacy = await LoadLegacyCredentialAsync(ct);
        if (legacy is null)
        {
            return new Ai79CredentialMigrationResult
            {
                Success = false,
                Status = "missing_legacy",
                Message = "Không tìm thấy credential 79AI đang hoạt động.",
                Metadata = await GetMetadataAsync(ct)
            };
        }

        var normalizedToken = NormalizeLegacyToken(legacy.AccessToken);
        var fingerprint = _protector.Fingerprint(normalizedToken);
        var current = await _repository.GetActiveSecureCredentialAsync(TargetAccountId, CredentialRole, ct);

        if (current is not null && string.Equals(current.TokenFingerprint, fingerprint, StringComparison.Ordinal))
        {
            await _repository.UpsertMappingAsync(TargetAccountId, CredentialRole, current.Id, ct);
            await _repository.DeactivatePriorMappingsAsync(TargetAccountId, CredentialRole, current.Id, ct);
            await _repository.SetProviderAccountEnabledDefaultAsync(TargetAccountId, ct);
            return new Ai79CredentialMigrationResult
            {
                Success = true,
                Status = "already_migrated",
                Message = "Credential 79AI đã được khởi tạo.",
                Metadata = await GetMetadataAsync(ct)
            };
        }

        var protectedCredential = await _protector.ProtectAsync(normalizedToken, ct);
        var secureCredentialId = await _repository.InsertSecureCredentialAsync(
            TargetAccountId,
            CredentialRole,
            protectedCredential,
            userId,
            BuildMetadataJson(legacy.Domain),
            ct);

        await _repository.UpsertMappingAsync(TargetAccountId, CredentialRole, secureCredentialId, ct);
        await _repository.DeactivatePriorMappingsAsync(TargetAccountId, CredentialRole, secureCredentialId, ct);
        await _repository.DeactivatePriorSecureCredentialsAsync(TargetAccountId, CredentialRole, secureCredentialId, userId, ct);
        await _repository.SetProviderAccountEnabledDefaultAsync(TargetAccountId, ct);

        return new Ai79CredentialMigrationResult
        {
            Success = true,
            Status = current is null ? "migrated" : "rotated",
            Message = current is null ? "Đã khởi tạo credential bảo mật 79AI." : "Đã cập nhật credential bảo mật 79AI.",
            Metadata = await GetMetadataAsync(ct)
        };
    }

    public static string NormalizeLegacyToken(string accessToken)
    {
        var token = string.IsNullOrWhiteSpace(accessToken)
            ? throw new InvalidOperationException("Credential 79AI không hợp lệ.")
            : accessToken.Trim();
        const string bearer = "Bearer ";
        return token.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? token[bearer.Length..].Trim()
            : token;
    }

    private async Task<LegacyCredential?> LoadLegacyCredentialAsync(CancellationToken ct)
    {
        using var conn = await _automationFactory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<LegacyCredential>(
            """
            SELECT access_token AS AccessToken, domain AS Domain
              FROM public.todox_video_79ai_provider_keys
             WHERE enabled = true
               AND upper(btrim(provider_code)) = '79AI'
               AND NULLIF(btrim(access_token), '') IS NOT NULL
             ORDER BY priority ASC, updated_at DESC, id DESC
             LIMIT 1;
            """);
    }

    private static string BuildMetadataJson(string? domain)
        => JsonSerializer.Serialize(new
        {
            migrated_from = "todox_video_79ai_provider_keys",
            provider_code = ProviderCode,
            domain = string.IsNullOrWhiteSpace(domain) ? "79ai.net" : domain.Trim()
        });

    private sealed class LegacyCredential
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? Domain { get; set; }
    }
}
