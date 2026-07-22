using System.Text.Json;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public interface IAiProviderService
{
    Task<IReadOnlyList<AiProviderListItemDto>> GetProvidersAsync(CancellationToken ct = default);
    Task<AiProviderDetailDto?> GetProviderAsync(long id, CancellationToken ct = default);
    Task<AiProviderDetailDto?> GetProviderByCodeAsync(string providerCode, CancellationToken ct = default);
    Task<AiProviderDetailDto> UpdateProviderAsync(long id, UpdateAiProviderRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default);
    Task<AiProviderCapabilityDto> UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task SetDefaultCapabilityAsync(long capabilityId, CurrentUserSession user, CancellationToken ct = default);
    Task SetDefaultCapabilitiesAsync(IReadOnlyList<long> capabilityIds, CurrentUserSession user, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default);
    Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default);
    Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default);
    Task LogUsageAsync(AiProviderUsageLog log, CancellationToken ct = default);
}

public sealed class AiProviderService : IAiProviderService
{
    private readonly AiProviderRepository _repo;
    private readonly IAiProviderUsageService _usage;
    private readonly ILogger<AiProviderService> _logger;

    public AiProviderService(AiProviderRepository repo, IAiProviderUsageService usage, ILogger<AiProviderService> logger)
    {
        _repo = repo;
        _usage = usage;
        _logger = logger;
    }

    public Task<IReadOnlyList<AiProviderListItemDto>> GetProvidersAsync(CancellationToken ct = default)
        => _repo.ListProvidersAsync(ct);

    public Task<AiProviderDetailDto?> GetProviderAsync(long id, CancellationToken ct = default)
        => _repo.GetProviderAsync(id, ct);

    public Task<AiProviderDetailDto?> GetProviderByCodeAsync(string providerCode, CancellationToken ct = default)
        => _repo.GetProviderByCodeAsync(providerCode, ct);

    public async Task<AiProviderDetailDto> UpdateProviderAsync(long id, UpdateAiProviderRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderName))
        {
            throw new InvalidOperationException("TÃªn provider khÃ´ng Ä‘Æ°á»£c Ä‘á»ƒ trá»‘ng.");
        }
        ValidateJson(request.ConfigJson, "config_json");

        try
        {
            await _repo.UpdateProviderAsync(id, request, user.UserId.ToString(), ct);
        }
        catch (Exception ex)
        {
            DbDiagnostics.LogPostgresException(_logger, ex, "provider_update");
            throw;
        }
        return await _repo.GetProviderAsync(id, ct)
               ?? throw new InvalidOperationException("KhÃ´ng Ä‘á»c láº¡i Ä‘Æ°á»£c provider vá»«a cáº­p nháº­t.");
    }

    public Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default)
        => _repo.GetCapabilitiesAsync(providerId, capabilityCode, ct);

    public async Task<AiProviderCapabilityDto> UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("TÃªn hiá»ƒn thá»‹ capability khÃ´ng Ä‘Æ°á»£c Ä‘á»ƒ trá»‘ng.");
        }
        if (!AiProviderCatalog.UnitTypes.Contains(request.UnitType))
        {
            throw new InvalidOperationException("ÄÆ¡n vá»‹ tÃ­nh (unit_type) khÃ´ng há»£p lá»‡.");
        }
        if (request.UnitCostPoints < 0)
        {
            throw new InvalidOperationException("Äiá»ƒm má»—i Ä‘Æ¡n vá»‹ khÃ´ng Ä‘Æ°á»£c Ã¢m.");
        }
        ValidateJson(request.ConfigJson, "config_json");

        const string table = "todox_ai_provider_capability";
        DbDiagnostics.LogFieldLengths(_logger, "capability_update",
            ("display_name", request.DisplayName),
            ("model_name", request.ModelName),
            ("unit_type", request.UnitType));
        request.DisplayName = DbDiagnostics.Clip(_logger, table, "display_name", request.DisplayName)!;
        request.ModelName = DbDiagnostics.Clip(_logger, table, "model_name", request.ModelName);
        request.UnitType = DbDiagnostics.Clip(_logger, table, "unit_type", request.UnitType)!;

        try
        {
            await _repo.UpdateCapabilityAsync(id, request, user.UserId.ToString(), ct);

            // Setting is_default is a separate, guarded operation (one default per capability_code).
            if (request.IsDefault)
            {
                await _repo.SetDefaultCapabilityAsync(id, user.UserId.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            DbDiagnostics.LogPostgresException(_logger, ex, "capability_update");
            throw;
        }

        return await _repo.GetCapabilityAsync(id, ct)
               ?? throw new InvalidOperationException("KhÃ´ng Ä‘á»c láº¡i Ä‘Æ°á»£c capability vá»«a cáº­p nháº­t.");
    }

    public Task SetDefaultCapabilityAsync(long capabilityId, CurrentUserSession user, CancellationToken ct = default)
        => _repo.SetDefaultCapabilityAsync(capabilityId, user.UserId.ToString(), ct);

    public Task SetDefaultCapabilitiesAsync(IReadOnlyList<long> capabilityIds, CurrentUserSession user, CancellationToken ct = default)
        => _repo.SetDefaultCapabilitiesAsync(capabilityIds, user.UserId.ToString(), ct);

    public Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default)
        => _repo.GetSelectableAsync(capabilityCode, ct);

    public Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default)
        => _repo.GetDefaultAsync(capabilityCode, ct);

    public async Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Resolving AI provider for capability {CapabilityCode}, providerCapabilityId={ProviderCapabilityId}, fromUser={FromUser}",
            capabilityCode, providerCapabilityId, fromUser);

        if (providerCapabilityId is long id)
        {
            var chosen = await _repo.GetOptionByCapabilityIdAsync(id, ct)
                ?? throw new InvalidOperationException("Provider này chưa được bật hoặc chưa được cấu hình cho chức năng hiện tại.");

            if (!chosen.Enabled || !string.Equals(chosen.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase)
                || (fromUser && !chosen.AllowUserSelect))
            {
                throw new InvalidOperationException("Provider này chưa được bật hoặc chưa được cấu hình cho chức năng hiện tại.");
            }
            return chosen;
        }

        var byDefault = await _repo.GetDefaultAsync(capabilityCode, ct);
        _logger.LogInformation(
            "Default provider lookup for {CapabilityCode}: {Found}",
            capabilityCode,
            byDefault is not null ? $"ProviderId={byDefault.ProviderId}, ProviderCode={byDefault.ProviderCode}, CapabilityId={byDefault.ProviderCapabilityId}" : "not found");
        if (byDefault is not null) return byDefault;

        var byPriority = await _repo.GetFirstByPriorityAsync(capabilityCode, ct);
        _logger.LogInformation(
            "Priority provider lookup for {CapabilityCode}: {Found}",
            capabilityCode,
            byPriority is not null ? $"ProviderId={byPriority.ProviderId}, ProviderCode={byPriority.ProviderCode}, CapabilityId={byPriority.ProviderCapabilityId}" : "not found");
        if (byPriority is not null) return byPriority;

        _logger.LogWarning(
            "No AI provider configured for capability {CapabilityCode}. providerCapabilityId={ProviderCapabilityId}, fromUser={FromUser}",
            capabilityCode, providerCapabilityId, fromUser);
        throw new InvalidOperationException("Chưa cấu hình AI Provider cho chức năng này.");
    }
    public async Task LogUsageAsync(AiProviderUsageLog log, CancellationToken ct = default)
    {
        const string table = "todox_ai_provider_usage_log";

        // Log field lengths so a length overflow can be pinpointed (no secrets are logged).
        DbDiagnostics.LogFieldLengths(_logger, "usage_log_insert",
            ("provider_code", log.ProviderCode),
            ("capability_code", log.CapabilityCode),
            ("feature_code", log.FeatureCode),
            ("model_name", log.ModelName),
            ("logical_request_id", log.RequestId),
            ("render_job_id", log.RenderJobId?.ToString("N") ?? log.JobId),
            ("status", log.Status));

        // Defensively clip string fields to their column limits before writing.
        log.ProviderCode = DbDiagnostics.Clip(_logger, table, "provider_code", log.ProviderCode);
        log.CapabilityCode = DbDiagnostics.Clip(_logger, table, "capability_code", log.CapabilityCode);
        log.FeatureCode = DbDiagnostics.Clip(_logger, table, "feature_code", log.FeatureCode);
        log.ModelName = DbDiagnostics.Clip(_logger, table, "model_name", log.ModelName);
        log.RequestId = DbDiagnostics.Clip(_logger, table, "logical_request_id", log.RequestId);
        log.UnitType = DbDiagnostics.Clip(_logger, table, "unit_type", log.UnitType);
        log.Status = DbDiagnostics.Clip(_logger, table, "status", log.Status);
        log.ErrorCode = DbDiagnostics.Clip(_logger, table, "error_code", log.ErrorCode);
        log.ErrorMessage = DbDiagnostics.Clip(_logger, table, "error_message", log.ErrorMessage);
        log.CreatedBy = DbDiagnostics.Clip(_logger, table, "created_by", log.CreatedBy);

        try
        {
            await _usage.RecordAsync(log, ct);
        }
        catch (Exception ex)
        {
            // Usage logging must never break a successful render, but the failure must be diagnosable.
            if (!DbDiagnostics.LogPostgresException(_logger, ex, "usage_log_insert"))
            {
                _logger.LogWarning(ex, "AI_PROVIDER_USAGE_LOG_FAILED capability={CapabilityCode} feature={FeatureCode}",
                    log.CapabilityCode, log.FeatureCode);
            }
        }
    }

    private static void ValidateJson(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch
        {
            throw new InvalidOperationException($"{field} khÃ´ng pháº£i JSON há»£p lá»‡.");
        }
    }
}

