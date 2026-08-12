using System.Collections.Concurrent;
using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderSyncResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid? SyncId { get; set; }
    public int ModelInsertedCount { get; set; }
    public int ModelUpdatedCount { get; set; }
    public int ModelUnavailableCount { get; set; }
    public int PriceChangedCount { get; set; }
    public List<AiProviderSyncChangeDto> Changes { get; set; } = new();
}

public interface IAiProviderSyncService
{
    Task<AiProviderSyncResultDto> SyncProviderAsync(long providerId, CurrentUserSession? user = null, CancellationToken ct = default);
    Task<AiProviderSyncResultDto> SyncScheduledProviderAsync(long providerId, CancellationToken ct = default);
}

public sealed class AiProviderSyncService : IAiProviderSyncService
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> ProviderLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public Task<AiProviderSyncResultDto> SyncProviderAsync(long providerId, CurrentUserSession? user = null, CancellationToken ct = default)
        => SyncProviderCoreAsync(providerId, "manual", user?.UserId, ct);

    public Task<AiProviderSyncResultDto> SyncScheduledProviderAsync(long providerId, CancellationToken ct = default)
        => SyncProviderCoreAsync(providerId, "scheduled", null, ct);

    internal async Task<AiProviderSyncResultDto> SyncProviderCoreAsync(long providerId, string trigger, Guid? triggeredBy, CancellationToken ct = default)
    {
        var syncLock = ProviderLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        if (!await syncLock.WaitAsync(0, ct))
        {
            return new AiProviderSyncResultDto
            {
                Success = false,
                Message = "Provider sync is already running."
            };
        }

        try
        {
            return await SyncProviderUnlockedAsync(providerId, trigger, triggeredBy, ct);
        }
        finally
        {
            syncLock.Release();
        }
    }

    private async Task<AiProviderSyncResultDto> SyncProviderUnlockedAsync(long providerId, string trigger, Guid? triggeredBy, CancellationToken ct)
    {
        var provider = await _providers.GetProviderAsync(providerId, ct);
        if (provider is null)
        {
            return new AiProviderSyncResultDto { Success = false, Message = "Provider not found." };
        }

        var syncId = await _modelRepository.InsertSyncHeaderAsync(
            provider.Id,
            provider.ProviderCode,
            trigger,
            triggeredBy,
            "running",
            ct);

        var result = new AiProviderSyncResultDto { Success = true, SyncId = syncId };

        try
        {
            var catalog = await _catalogClient.FetchAsync(provider, ct);
            if (!catalog.Configured)
            {
                await _modelRepository.CompleteSyncHeaderAsync(
                    syncId, "failed", catalog.Message, 0, 0, 0, 0, 0, 0, 0,
                    BuildSummaryJson(result), ct);
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
            var policies = (await _pricingRepository.GetPoliciesAsync(provider.Id, ct)).ToList();
            var defaultPolicy = policies.FirstOrDefault(x => x.Enabled && x.IsDefault) ?? policies.FirstOrDefault(x => x.Enabled);

            foreach (var snapshot in catalog.Models)
            {
                if (string.IsNullOrWhiteSpace(snapshot.ProviderModelCode))
                {
                    continue;
                }

                var detail = BuildDetail(provider, snapshot);
                var currentDetail = existingByCode.TryGetValue(snapshot.ProviderModelCode, out var current)
                    ? await _models.GetModelAsync(current.Id, ct)
                    : null;

                PrepareIncomingPrices(detail.Prices, currentDetail?.Prices ?? new List<AiModelPriceDto>(), defaultPolicy);
                var changeCount = await InsertChangesAsync(syncId, currentDetail, detail, ct);
                result.PriceChangedCount += changeCount.PriceChanges;

                if (currentDetail is null)
                {
                    result.ModelInsertedCount++;
                    await _modelRepository.InsertSyncChangeAsync(syncId, "MODEL_ADDED", "model", snapshot.ProviderModelCode, null, detail.RawJson, ct);
                }
                else if (HasModelChanged(currentDetail, detail) || changeCount.ModelChanges > 0)
                {
                    result.ModelUpdatedCount++;
                }

                var modelId = await _modelRepository.UpsertModelAsync(detail, triggeredBy?.ToString(), ct);
                foreach (var price in detail.Prices)
                {
                    price.ModelId = modelId;
                }

                await UpsertPoliciesAsync(detail, triggeredBy?.ToString(), ct);
                await DeactivateMissingPricesAsync(syncId, modelId, currentDetail?.Prices ?? new List<AiModelPriceDto>(), detail.Prices, triggeredBy?.ToString(), ct);
            }

            var missingCodes = AiProviderSyncPlanner.GetMissingCodes(existingByCode.Keys.ToList(), incomingCodes);
            if (missingCodes.Count > 0)
            {
                result.ModelUnavailableCount += missingCodes.Count;
                await _modelRepository.MarkMissingAsDeprecatedAsync(provider.Id, incomingCodes, triggeredBy?.ToString(), ct);
                foreach (var code in missingCodes)
                {
                    await _modelRepository.InsertSyncChangeAsync(syncId, "MODEL_STATUS_CHANGED", "model", code, null, "{\"provider_status\":\"DEPRECATED\"}", ct);
                }
            }

            result.Changes = (await _modelRepository.GetSyncChangesAsync(syncId, 500, ct)).ToList();
            await _modelRepository.CompleteSyncHeaderAsync(
                syncId,
                "success",
                null,
                catalog.Models.Count,
                result.ModelInsertedCount,
                result.ModelUpdatedCount,
                result.ModelUnavailableCount,
                catalog.Models.Sum(x => x.Prices?.Count ?? 0),
                result.PriceChangedCount,
                0,
                BuildSummaryJson(result),
                ct);
            result.Message = result.ModelInsertedCount == 0 && result.ModelUpdatedCount == 0 && result.ModelUnavailableCount == 0 && result.PriceChangedCount == 0
                ? "No changes."
                : "Sync completed.";
            return result;
        }
        catch (Exception ex)
        {
            await _modelRepository.CompleteSyncHeaderAsync(syncId, "failed", ex.Message, 0, 0, 0, 0, 0, 0, 0, BuildSummaryJson(new AiProviderSyncResultDto()), ct);
            return new AiProviderSyncResultDto
            {
                Success = false,
                Message = ex.Message,
                SyncId = syncId
            };
        }
    }

    private async Task UpsertPoliciesAsync(AiProviderModelDetailDto model, string? userId, CancellationToken ct)
    {
        foreach (var policy in model.PricingPolicies)
        {
            await _pricingRepository.UpsertPolicyAsync(policy, userId, ct);
        }
    }

    private async Task DeactivateMissingPricesAsync(
        Guid syncId,
        long modelId,
        IReadOnlyList<AiModelPriceDto> before,
        IReadOnlyList<AiModelPriceDto> after,
        string? userId,
        CancellationToken ct)
    {
        var incomingKeys = after.Where(x => x.Active).Select(PriceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var oldPrice in before.Where(x => x.Active && !incomingKeys.Contains(PriceKey(x))))
        {
            if (oldPrice.Id > 0)
            {
                await _pricingRepository.MarkPriceInactiveAsync(oldPrice.Id, userId, ct);
            }

            await _modelRepository.InsertSyncChangeAsync(
                syncId,
                "PRICE_DISABLED",
                "price",
                $"{modelId}:{PriceKey(oldPrice)}",
                Serialize(oldPrice),
                null,
                ct);
        }
    }

    private static void PrepareIncomingPrices(
        IReadOnlyList<AiModelPriceDto> incoming,
        IReadOnlyList<AiModelPriceDto> existing,
        AiPricingPolicyDto? defaultPolicy)
    {
        var existingByKey = existing.ToDictionary(PriceKey, StringComparer.OrdinalIgnoreCase);
        foreach (var price in incoming)
        {
            price.Mode = NormalizeNullable(price.Mode);
            price.Resolution = NormalizeNullable(price.Resolution);
            price.Ratio = NormalizeNullable(price.Ratio);
            price.ProviderPriceUnit = NormalizeNullable(price.ProviderPriceUnit) ?? "79ai_credit";
            price.UnitType = NormalizeNullable(price.UnitType) ?? "scene";
            price.SellPriceMode = string.IsNullOrWhiteSpace(price.SellPriceMode) ? "AUTO" : price.SellPriceMode.Trim().ToUpperInvariant();
            price.PriceSource = NormalizeNullable(price.PriceSource) ?? "catalog";
            price.EffectiveFrom ??= DateTime.UtcNow;
            price.Active = true;

            var providerUnit = price.ProviderPrice ?? price.ProviderPriceDefault;
            if (providerUnit.HasValue && defaultPolicy is not null)
            {
                price.InternalCostPoints = AiPricingEngine.CalculateInternalUnitCostPoints(providerUnit.Value, defaultPolicy.ProviderCreditPerInternalPoint);
            }

            if (existingByKey.TryGetValue(PriceKey(price), out var existingPrice))
            {
                price.SellPoints = existingPrice.SellPoints;
                price.SellPriceMode = existingPrice.SellPriceMode;
                price.MarkupPercent = existingPrice.MarkupPercent;
                price.MinimumPoints = existingPrice.MinimumPoints;
                price.RoundingRule = existingPrice.RoundingRule;
                continue;
            }

            price.MarkupPercent ??= defaultPolicy?.DefaultMarkupPercent;
            price.MinimumPoints ??= defaultPolicy?.MinimumSellPoints;
            price.RoundingRule ??= defaultPolicy?.RoundingRule;
            if (price.InternalCostPoints.HasValue && defaultPolicy is not null)
            {
                price.SellPoints = AiPricingEngine.CalculateSellUnitPoints(price.InternalCostPoints.Value, price, defaultPolicy);
            }
        }
    }

    private async Task<(int ModelChanges, int PriceChanges)> InsertChangesAsync(
        Guid syncId,
        AiProviderModelDetailDto? before,
        AiProviderModelDetailDto after,
        CancellationToken ct)
    {
        if (before is null)
        {
            foreach (var price in after.Prices.Where(x => x.Active))
            {
                await _modelRepository.InsertSyncChangeAsync(syncId, "PRICE_ADDED", "price", $"{after.ProviderModelCode}:{PriceKey(price)}", null, Serialize(price), ct);
            }

            return (0, after.Prices.Count(x => x.Active));
        }

        var modelChanges = 0;
        var priceChanges = 0;

        if (!string.Equals(before.ProviderStatus, after.ProviderStatus, StringComparison.OrdinalIgnoreCase))
        {
            modelChanges++;
            await _modelRepository.InsertSyncChangeAsync(syncId, "MODEL_STATUS_CHANGED", "model", after.ProviderModelCode, before.ProviderStatus, after.ProviderStatus, ct);
        }

        foreach (var mode in after.SupportedModes.Except(before.SupportedModes, StringComparer.OrdinalIgnoreCase))
        {
            modelChanges++;
            await _modelRepository.InsertSyncChangeAsync(syncId, "MODE_ADDED", "model_option", after.ProviderModelCode, null, mode, ct);
        }

        foreach (var duration in after.SupportedDurations.Except(before.SupportedDurations))
        {
            modelChanges++;
            await _modelRepository.InsertSyncChangeAsync(syncId, "DURATION_ADDED", "model_option", after.ProviderModelCode, null, duration.ToString(), ct);
        }

        foreach (var duration in before.SupportedDurations.Except(after.SupportedDurations))
        {
            modelChanges++;
            await _modelRepository.InsertSyncChangeAsync(syncId, "DURATION_REMOVED", "model_option", after.ProviderModelCode, duration.ToString(), null, ct);
        }

        foreach (var resolution in after.SupportedResolutions.Except(before.SupportedResolutions, StringComparer.OrdinalIgnoreCase))
        {
            modelChanges++;
            await _modelRepository.InsertSyncChangeAsync(syncId, "RESOLUTION_ADDED", "model_option", after.ProviderModelCode, null, resolution, ct);
        }

        var beforePrices = before.Prices.Where(x => x.Active).ToDictionary(PriceKey, StringComparer.OrdinalIgnoreCase);
        foreach (var price in after.Prices.Where(x => x.Active))
        {
            var key = PriceKey(price);
            if (!beforePrices.TryGetValue(key, out var oldPrice))
            {
                priceChanges++;
                await _modelRepository.InsertSyncChangeAsync(syncId, "PRICE_ADDED", "price", $"{after.ProviderModelCode}:{key}", null, Serialize(price), ct);
                continue;
            }

            if (AiPricingEngine.IsProviderControlledPriceChanged(oldPrice, price))
            {
                priceChanges++;
                await _modelRepository.InsertSyncChangeAsync(
                    syncId,
                    "PRICE_CHANGED",
                    "price",
                    $"{after.ProviderModelCode}:{key}",
                    Serialize(oldPrice),
                    Serialize(price),
                    ct,
                    changedFields: new[] { "provider_price", "provider_price_default" });
            }
        }

        return (modelChanges, priceChanges);
    }

    private static string BuildSummaryJson(AiProviderSyncResultDto result)
        => JsonSerializer.Serialize(new
        {
            model_inserted_count = result.ModelInsertedCount,
            model_updated_count = result.ModelUpdatedCount,
            model_unavailable_count = result.ModelUnavailableCount,
            price_changed_count = result.PriceChangedCount
        });

    private static AiProviderModelDetailDto BuildDetail(AiProviderDetailDto provider, AiCatalogModelSnapshot snapshot)
    {
        var options = AiProviderModelOptionsNormalizer.Normalize(snapshot.SupportedModes, snapshot.SupportedDurations, snapshot.SupportedResolutions, snapshot.SupportedRatios, snapshot.Prices, snapshot.RawJson);
        return new AiProviderModelDetailDto
        {
            ProviderId = provider.Id,
            ProviderCode = provider.ProviderCode,
            ProviderModelCode = snapshot.ProviderModelCode,
            ProviderModelIdBase = snapshot.ProviderModelIdBase,
            DisplayName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.ProviderModelCode : snapshot.DisplayName,
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
            RawJson = SanitizeRawJson(snapshot.RawJson),
            SupportedModes = options.Modes,
            SupportedDurations = options.Durations,
            SupportedResolutions = options.Resolutions,
            SupportedRatios = options.Ratios,
            ModelCapabilities = BuildCapabilities(snapshot, options),
            Prices = snapshot.Prices.Select(price =>
            {
                price.ModelId = 0;
                return price;
            }).ToList(),
            PricingPolicies = snapshot.Policies
        };
    }

    private static List<AiProviderModelCapabilityDto> BuildCapabilities(AiCatalogModelSnapshot snapshot, AiProviderModelOptions options)
    {
        var capabilities = snapshot.Capabilities.ToList();
        if (capabilities.Count == 0)
        {
            capabilities.Add(new AiProviderModelCapabilityDto
            {
                CapabilityCode = string.Equals(snapshot.MediaType, "video", StringComparison.OrdinalIgnoreCase)
                    ? AiProviderCatalog.ImageToVideo
                    : "image_generation",
                Enabled = true,
                Source = "catalog"
            });
        }

        foreach (var capability in capabilities)
        {
            capability.ConfigJson = AiProviderModelOptionsNormalizer.ToConfigJson(options);
        }

        return capabilities;
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

    private static string PriceKey(AiModelPriceDto price)
        => string.Join(":", price.Mode ?? string.Empty, price.Resolution ?? string.Empty, price.DurationSeconds?.ToString() ?? string.Empty, price.Ratio ?? string.Empty);

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SanitizeRawJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return rawJson;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return SanitizeElement(doc.RootElement).GetRawText();
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private static JsonElement SanitizeElement(JsonElement element)
    {
        var sanitized = SanitizeValue(element);
        return JsonSerializer.SerializeToElement(sanitized, JsonOptions);
    }

    private static object? SanitizeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                x => x.Name,
                x => IsSecretName(x.Name) ? "***" : SanitizeValue(x.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool IsSecretName(string name)
        => name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
           || name.Contains("access_key", StringComparison.OrdinalIgnoreCase);

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
