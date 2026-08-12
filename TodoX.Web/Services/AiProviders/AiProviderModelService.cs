using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public interface IAiProviderModelService
{
    Task<IReadOnlyList<AiProviderModelListItemDto>> GetModelsAsync(string? providerCode = null, string? mediaType = null, string? status = null, bool? enabled = null, string? search = null, CancellationToken ct = default);
    Task<AiProviderModelDetailDto?> GetModelAsync(long id, CancellationToken ct = default);
    Task<AiProviderModelDetailDto?> GetModelByCodeAsync(long providerId, string providerModelCode, CancellationToken ct = default);
    Task UpdateAdminFieldsAsync(long id, string displayName, bool enabled, bool allowUserSelect, string? description, string? userId, CancellationToken ct = default);
    Task UpdateSyncFieldsAsync(AiProviderModelDetailDto model, string? userId, CancellationToken ct = default);
    Task<IReadOnlyList<AiProviderSyncHeaderDto>> GetSyncHistoryAsync(long providerId, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<AiProviderSyncChangeDto>> GetSyncChangesAsync(Guid syncId, int limit = 200, CancellationToken ct = default);
}

public sealed class AiProviderModelService : IAiProviderModelService
{
    private readonly AiProviderModelRepository _repo;

    public AiProviderModelService(AiProviderModelRepository repo)
    {
        _repo = repo;
    }

    public Task<IReadOnlyList<AiProviderModelListItemDto>> GetModelsAsync(string? providerCode = null, string? mediaType = null, string? status = null, bool? enabled = null, string? search = null, CancellationToken ct = default)
        => _repo.ListModelsAsync(providerCode, mediaType, status, enabled, search, ct);

    public Task<AiProviderModelDetailDto?> GetModelAsync(long id, CancellationToken ct = default)
        => _repo.GetModelAsync(id, ct);

    public Task<AiProviderModelDetailDto?> GetModelByCodeAsync(long providerId, string providerModelCode, CancellationToken ct = default)
        => _repo.GetModelByCodeAsync(providerId, providerModelCode, ct);

    public Task UpdateAdminFieldsAsync(long id, string displayName, bool enabled, bool allowUserSelect, string? description, string? userId, CancellationToken ct = default)
        => _repo.UpdateAdminFieldsAsync(id, displayName, enabled, allowUserSelect, description, userId, ct);

    public Task UpdateSyncFieldsAsync(AiProviderModelDetailDto model, string? userId, CancellationToken ct = default)
        => _repo.UpdateSyncFieldsAsync(
            model.ProviderId,
            model.ProviderModelCode,
            model.ProviderModelIdBase,
            model.DisplayName,
            model.MediaType,
            model.ServerCode,
            model.ProviderStatus,
            model.StatusMessage,
            model.RateType,
            model.BaseProviderPrice,
            model.ProviderPriceUnit,
            model.Source,
            model.LastProviderSyncAt,
            model.LastHealthCheckAt,
            model.LastSuccessAt,
            model.LastFailureAt,
            model.FailureCount,
            model.RawJson,
            userId,
            ct);

    public Task<IReadOnlyList<AiProviderSyncHeaderDto>> GetSyncHistoryAsync(long providerId, int limit = 20, CancellationToken ct = default)
        => _repo.GetSyncHistoryAsync(providerId, limit, ct);

    public Task<IReadOnlyList<AiProviderSyncChangeDto>> GetSyncChangesAsync(Guid syncId, int limit = 200, CancellationToken ct = default)
        => _repo.GetSyncChangesAsync(syncId, limit, ct);
}
