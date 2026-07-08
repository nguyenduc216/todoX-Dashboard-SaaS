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
    Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default);
    Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default);
    Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default);
    Task LogUsageAsync(AiProviderUsageLog log, CancellationToken ct = default);
}

public sealed class AiProviderService : IAiProviderService
{
    private readonly AiProviderRepository _repo;
    private readonly ILogger<AiProviderService> _logger;

    public AiProviderService(AiProviderRepository repo, ILogger<AiProviderService> logger)
    {
        _repo = repo;
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
            throw new InvalidOperationException("Tên provider không được để trống.");
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
               ?? throw new InvalidOperationException("Không đọc lại được provider vừa cập nhật.");
    }

    public Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default)
        => _repo.GetCapabilitiesAsync(providerId, capabilityCode, ct);

    public async Task<AiProviderCapabilityDto> UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("Tên hiển thị capability không được để trống.");
        }
        if (!AiProviderCatalog.UnitTypes.Contains(request.UnitType))
        {
            throw new InvalidOperationException("Đơn vị tính (unit_type) không hợp lệ.");
        }
        if (request.UnitCostPoints < 0)
        {
            throw new InvalidOperationException("Điểm mỗi đơn vị không được âm.");
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
               ?? throw new InvalidOperationException("Không đọc lại được capability vừa cập nhật.");
    }

    public Task SetDefaultCapabilityAsync(long capabilityId, CurrentUserSession user, CancellationToken ct = default)
        => _repo.SetDefaultCapabilityAsync(capabilityId, user.UserId.ToString(), ct);

    public Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default)
        => _repo.GetSelectableAsync(capabilityCode, ct);

    public Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default)
        => _repo.GetDefaultAsync(capabilityCode, ct);

    public async Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default)
    {
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
        if (byDefault is not null) return byDefault;

        var byPriority = await _repo.GetFirstByPriorityAsync(capabilityCode, ct);
        if (byPriority is not null) return byPriority;

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
            ("request_id", log.RequestId),
            ("job_id", log.JobId),
            ("status", log.Status));

        // Defensively clip string fields to their column limits before writing.
        log.ProviderCode = DbDiagnostics.Clip(_logger, table, "provider_code", log.ProviderCode);
        log.CapabilityCode = DbDiagnostics.Clip(_logger, table, "capability_code", log.CapabilityCode);
        log.FeatureCode = DbDiagnostics.Clip(_logger, table, "feature_code", log.FeatureCode);
        log.ModelName = DbDiagnostics.Clip(_logger, table, "model_name", log.ModelName);
        log.RequestId = DbDiagnostics.Clip(_logger, table, "request_id", log.RequestId);
        log.JobId = DbDiagnostics.Clip(_logger, table, "job_id", log.JobId);
        log.UnitType = DbDiagnostics.Clip(_logger, table, "unit_type", log.UnitType);
        log.Status = DbDiagnostics.Clip(_logger, table, "status", log.Status);

        try
        {
            await _repo.InsertUsageLogAsync(log, ct);
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
            throw new InvalidOperationException($"{field} không phải JSON hợp lệ.");
        }
    }
}
