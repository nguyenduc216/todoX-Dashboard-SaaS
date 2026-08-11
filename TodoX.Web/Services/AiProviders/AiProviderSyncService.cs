using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderSyncResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? SyncId { get; set; }
    public int ModelInsertedCount { get; set; }
    public int ModelUpdatedCount { get; set; }
    public int ModelUnavailableCount { get; set; }
    public int PriceChangedCount { get; set; }
    public List<AiProviderSyncChangeDto> Changes { get; set; } = new();
}

public interface IAiProviderSyncService
{
    Task<AiProviderSyncResultDto> SyncProviderAsync(long providerId, CurrentUserSession? user = null, CancellationToken ct = default);
}

public sealed class AiProviderSyncService : IAiProviderSyncService
{
    private readonly IAiProviderService _providers;
    private readonly IAiProviderModelService _models;
    private readonly AiProviderModelRepository _modelRepository;
    private readonly AiPricingRepository _pricingRepository;
    private readonly IAi79CatalogClient _catalogClient;

    public AiProviderSyncService(
        IAiProviderService providers,
        IAiProviderModelService models,
        AiProviderModelRepository modelRepository,
        AiPricingRepository pricingRepository,
        IAi79CatalogClient catalogClient)
    {
        _providers = providers;
        _models = models;
        _modelRepository = modelRepository;
        _pricingRepository = pricingRepository;
        _catalogClient = catalogClient;
    }

    public async Task<AiProviderSyncResultDto> SyncProviderAsync(long providerId, CurrentUserSession? user = null, CancellationToken ct = default)
    {
        var provider = await _providers.GetProviderAsync(providerId, ct);
        if (provider is null)
        {
            return new AiProviderSyncResultDto { Success = false, Message = "Provider not found." };
        }

        var syncId = await _modelRepository.InsertSyncHeaderAsync(
            provider.Id,
            provider.ProviderCode,
            "manual",
            user?.UserId.ToString(),
            GetCatalogEndpoint(provider.ConfigJson),
            "running",
            null,
            ct);

        try
        {
            var catalog = await _catalogClient.FetchAsync(provider, ct);
            if (!catalog.Configured)
            {
                await _modelRepository.CompleteSyncHeaderAsync(syncId, "failed", catalog.Message, 0, 0, 0, 0, ct);
                return new AiProviderSyncResultDto
                {
                    Success = false,
                    Message = catalog.Message,
                    SyncId = syncId
                };
            }

            var existing = (await _models.GetModelsAsync(provider.ProviderCode, ct: ct)).ToList();
            var existingByCode = existing.ToDictionary(x => x.ProviderModelCode, StringComparer.OrdinalIgnoreCase);
            var incomingCodes = catalog.Models
                .Where(x => !string.IsNullOrWhiteSpace(x.ProviderModelCode))
                .Select(x => x.ProviderModelCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new AiProviderSyncResultDto { Success = true, SyncId = syncId };

            foreach (var snapshot in catalog.Models)
            {
                if (string.IsNullOrWhiteSpace(snapshot.ProviderModelCode))
                {
                    continue;
                }

                var detail = BuildDetail(provider, snapshot);
                if (!existingByCode.TryGetValue(snapshot.ProviderModelCode, out var current))
                {
                    result.ModelInsertedCount++;
                    await _modelRepository.InsertSyncChangeAsync(syncId, "insert", "model", snapshot.ProviderModelCode, null, detail.RawJson, ct);
                    await _modelRepository.UpsertModelAsync(detail, user?.UserId.ToString(), ct);
                    await UpsertPoliciesAndPricesAsync(detail, user?.UserId.ToString(), ct);
                    continue;
                }

                var currentDetail = await _models.GetModelAsync(current.Id, ct);
                if (currentDetail is null)
                {
                    continue;
                }

                var beforeJson = currentDetail.RawJson ?? JsonSerializer.Serialize(currentDetail);
                var afterJson = detail.RawJson;
                var changed = HasModelChanged(currentDetail, detail);
                if (changed)
                {
                    result.ModelUpdatedCount++;
                    await _modelRepository.InsertSyncChangeAsync(syncId, "update", "model", snapshot.ProviderModelCode, beforeJson, afterJson, ct);
                    await _modelRepository.UpsertModelAsync(detail, user?.UserId.ToString(), ct);
                    await UpsertPoliciesAndPricesAsync(detail, user?.UserId.ToString(), ct);
                }

                var beforePrices = currentDetail.Prices.Where(x => x.Active).ToList();
                var afterPrices = detail.Prices.Where(x => x.Active).ToList();
                if (HasPriceChange(beforePrices, afterPrices))
                {
                    result.PriceChangedCount++;
                    await _modelRepository.InsertSyncChangeAsync(syncId, "price_change", "price", snapshot.ProviderModelCode, JsonSerializer.Serialize(beforePrices), JsonSerializer.Serialize(afterPrices), ct);
                }
            }

            var missingCodes = existingByCode.Keys
                .Where(code => !incomingCodes.Contains(code))
                .ToList();
            if (missingCodes.Count > 0)
            {
                result.ModelUnavailableCount += missingCodes.Count;
                await _modelRepository.MarkMissingAsDeprecatedAsync(provider.Id, incomingCodes, user?.UserId.ToString(), ct);
                foreach (var code in missingCodes)
                {
                    await _modelRepository.InsertSyncChangeAsync(syncId, "unavailable", "model", code, null, null, ct);
                }
            }

            result.Changes = (await _modelRepository.GetSyncChangesAsync(syncId, 500, ct)).ToList();
            await _modelRepository.CompleteSyncHeaderAsync(
                syncId,
                "success",
                result.Message,
                result.ModelInsertedCount,
                result.ModelUpdatedCount,
                result.ModelUnavailableCount,
                result.PriceChangedCount,
                ct);
            result.SyncId = syncId;
            result.Message = result.ModelInsertedCount == 0 && result.ModelUpdatedCount == 0 && result.ModelUnavailableCount == 0 && result.PriceChangedCount == 0
                ? "No changes."
                : "Sync completed.";
            return result;
        }
        catch (Exception ex)
        {
            await _modelRepository.CompleteSyncHeaderAsync(syncId, "failed", ex.Message, 0, 0, 0, 0, ct);
            return new AiProviderSyncResultDto
            {
                Success = false,
                Message = ex.Message,
                SyncId = syncId
            };
        }
    }

    private async Task UpsertPoliciesAndPricesAsync(AiProviderModelDetailDto model, string? userId, CancellationToken ct)
    {
        foreach (var policy in model.PricingPolicies)
        {
            await _pricingRepository.UpsertPolicyAsync(policy, userId, ct);
        }

        foreach (var price in model.Prices)
        {
            await _pricingRepository.UpsertPriceAsync(new AiModelPriceDto
            {
                ModelId = model.Id,
                Mode = price.Mode,
                Resolution = price.Resolution,
                DurationSeconds = price.DurationSeconds,
                Ratio = price.Ratio,
                RateType = price.RateType,
                UnitType = price.UnitType,
                ProviderPrice = price.ProviderPrice,
                ProviderPriceDefault = price.ProviderPriceDefault,
                ProviderPriceUnit = price.ProviderPriceUnit,
                InternalCostPoints = price.InternalCostPoints,
                SellPoints = price.SellPoints,
                SellPriceMode = price.SellPriceMode,
                MarkupPercent = price.MarkupPercent,
                MinimumPoints = price.MinimumPoints,
                RoundingRule = price.RoundingRule,
                PriceSource = price.PriceSource,
                EffectiveFrom = price.EffectiveFrom,
                EffectiveTo = price.EffectiveTo,
                Active = price.Active
            }, userId, ct);
        }
    }

    private static AiProviderModelDetailDto BuildDetail(AiProviderDetailDto provider, AiCatalogModelSnapshot snapshot)
    {
        return new AiProviderModelDetailDto
        {
            ProviderId = provider.Id,
            ProviderCode = provider.ProviderCode,
            ProviderModelCode = snapshot.ProviderModelCode,
            ProviderModelIdBase = snapshot.ProviderModelIdBase,
            DisplayName = snapshot.DisplayName,
            MediaType = snapshot.MediaType,
            ServerCode = snapshot.ServerCode,
            ProviderStatus = snapshot.ProviderStatus,
            StatusMessage = snapshot.StatusMessage,
            RateType = snapshot.RateType,
            BaseProviderPrice = snapshot.BaseProviderPrice,
            ProviderPriceUnit = snapshot.ProviderPriceUnit,
            Description = snapshot.Description,
            Enabled = snapshot.Enabled,
            AllowUserSelect = snapshot.AllowUserSelect,
            IsDeprecated = snapshot.IsDeprecated,
            Source = snapshot.Source,
            LastProviderSyncAt = snapshot.LastProviderSyncAt ?? DateTime.UtcNow,
            LastHealthCheckAt = snapshot.LastHealthCheckAt,
            LastSuccessAt = snapshot.LastSuccessAt,
            LastFailureAt = snapshot.LastFailureAt,
            FailureCount = snapshot.FailureCount,
            RawJson = snapshot.RawJson,
            ModelCapabilities = snapshot.Capabilities,
            Prices = snapshot.Prices.Select(price =>
            {
                price.ModelId = 0;
                return price;
            }).ToList(),
            PricingPolicies = snapshot.Policies
        };
    }

    private static bool HasModelChanged(AiProviderModelDetailDto before, AiProviderModelDetailDto after)
    {
        return !string.Equals(before.DisplayName, after.DisplayName, StringComparison.Ordinal)
               || !string.Equals(before.MediaType, after.MediaType, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(before.ProviderStatus, after.ProviderStatus, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(before.StatusMessage, after.StatusMessage, StringComparison.Ordinal)
               || before.Enabled != after.Enabled
               || before.AllowUserSelect != after.AllowUserSelect
               || before.IsDeprecated != after.IsDeprecated
               || before.BaseProviderPrice != after.BaseProviderPrice
               || !string.Equals(before.ProviderPriceUnit, after.ProviderPriceUnit, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(before.ServerCode, after.ServerCode, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(before.ProviderModelIdBase, after.ProviderModelIdBase, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPriceChange(IReadOnlyList<AiModelPriceDto> before, IReadOnlyList<AiModelPriceDto> after)
    {
        var beforeMap = before.ToDictionary(x => PriceKey(x), x => x, StringComparer.OrdinalIgnoreCase);
        var afterMap = after.ToDictionary(x => PriceKey(x), x => x, StringComparer.OrdinalIgnoreCase);
        if (beforeMap.Count != afterMap.Count)
        {
            return true;
        }

        foreach (var pair in beforeMap)
        {
            if (!afterMap.TryGetValue(pair.Key, out var afterPrice))
            {
                return true;
            }

            if (AiPricingEngine.IsPriceChanged(pair.Value, afterPrice))
            {
                return true;
            }
        }

        return false;
    }

    private static string PriceKey(AiModelPriceDto price)
        => string.Join(":", price.Mode ?? string.Empty, price.Resolution ?? string.Empty, price.DurationSeconds?.ToString() ?? string.Empty, price.Ratio ?? string.Empty);

    private static string? GetCatalogEndpoint(string? providerConfigJson)
    {
        if (string.IsNullOrWhiteSpace(providerConfigJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(providerConfigJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = doc.RootElement;
            var catalog = root.TryGetProperty("catalog", out var catalogElement) && catalogElement.ValueKind == JsonValueKind.Object
                ? catalogElement
                : root;
            var image = catalog.TryGetProperty("image_models_path", out var imagePath) ? imagePath.GetString() : null;
            var video = catalog.TryGetProperty("video_models_path", out var videoPath) ? videoPath.GetString() : null;
            var parts = new[] { image, video }.Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(" | ", parts);
        }
        catch
        {
            return null;
        }
    }
}
