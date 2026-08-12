using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public interface IAiPricingService
{
    Task<IReadOnlyList<AiPricingPolicyDto>> GetPoliciesAsync(long providerId, CancellationToken ct = default);
    Task<IReadOnlyList<AiModelPriceDto>> GetPricesAsync(long modelId, CancellationToken ct = default);
    Task SavePriceAsync(AiModelPriceDto price, string? userId, CancellationToken ct = default);
    Task<EstimateCostResponseDto> EstimateAsync(EstimateCostRequestDto request, CancellationToken ct = default);
}

public sealed class AiPricingService : IAiPricingService
{
    private readonly AiPricingRepository _pricingRepo;
    private readonly IAiProviderModelService _models;
    private readonly IAiProviderService _providers;

    public AiPricingService(AiPricingRepository pricingRepo, IAiProviderModelService models, IAiProviderService providers)
    {
        _pricingRepo = pricingRepo;
        _models = models;
        _providers = providers;
    }

    public Task<IReadOnlyList<AiPricingPolicyDto>> GetPoliciesAsync(long providerId, CancellationToken ct = default)
        => _pricingRepo.GetPoliciesAsync(providerId, ct);

    public Task<IReadOnlyList<AiModelPriceDto>> GetPricesAsync(long modelId, CancellationToken ct = default)
        => _pricingRepo.GetPricesAsync(modelId, ct);

    public async Task SavePriceAsync(AiModelPriceDto price, string? userId, CancellationToken ct = default)
    {
        var model = price.ModelId > 0 ? await _models.GetModelAsync(price.ModelId, ct) : null;
        if (!AiModelPriceNormalizer.NormalizeForManualSave(price, model?.ProviderCode ?? string.Empty, out var ignoredReason))
        {
            throw new InvalidOperationException(ignoredReason ?? "Price row khong hop le.");
        }

        await _pricingRepo.UpsertPriceAsync(price, userId, ct);
    }

    public async Task<EstimateCostResponseDto> EstimateAsync(EstimateCostRequestDto request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            throw new InvalidOperationException("quantity must be greater than zero.");
        }

        var model = request.ProviderModelId is long modelId
            ? await _models.GetModelAsync(modelId, ct)
            : request.ProviderId is long providerId && !string.IsNullOrWhiteSpace(request.ProviderModelCode)
                ? await _models.GetModelByCodeAsync(providerId, request.ProviderModelCode!, ct)
                : !string.IsNullOrWhiteSpace(request.ProviderCode) && !string.IsNullOrWhiteSpace(request.ProviderModelCode)
                    ? await ResolveByProviderCodeAsync(request.ProviderCode!, request.ProviderModelCode!, ct)
                    : null;

        if (model is null)
        {
            return new EstimateCostResponseDto
            {
                Success = false,
                ErrorCode = "model_not_found",
                Message = "model_not_found"
            };
        }

        var policy = (await _pricingRepo.GetPoliciesAsync(model.ProviderId, ct)).FirstOrDefault(x => x.Enabled && x.IsDefault);
        if (policy is null)
        {
            return new EstimateCostResponseDto
            {
                Success = false,
                ErrorCode = "price_not_configured",
                Message = "price_not_configured",
                ProviderModel = model,
                PricingPolicy = null
            };
        }

        var prices = (await _pricingRepo.GetPricesAsync(model.Id, ct)).Where(x => x.Active).ToList();
        var matched = AiPricingEngine.FindExactPrice(prices, request.Mode, request.Resolution, request.DurationSeconds, request.Ratio);
        var estimate = AiPricingEngine.BuildEstimate(model, policy, matched, request.Quantity);
        if (!estimate.Success)
        {
            return estimate;
        }

        return estimate;
    }

    private async Task<AiProviderModelDetailDto?> ResolveByProviderCodeAsync(string providerCode, string providerModelCode, CancellationToken ct)
    {
        var provider = await _providers.GetProviderByCodeAsync(providerCode, ct);
        return provider is null
            ? null
            : await _models.GetModelByCodeAsync(provider.Id, providerModelCode, ct);
    }

    private static string NormalizeSellMode(string? mode)
        => string.IsNullOrWhiteSpace(mode) ? "AUTO" : mode.Trim().ToUpperInvariant();
}
