using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageRenderRequest
{
    public long? CustomerId { get; set; }
    public Guid? CustomerGuid { get; set; }
    public Guid? UserId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public long? ProviderCapabilityId { get; set; }
    /// <summary>True when the selection originated from an end-user UI (enforces allow_user_select).</summary>
    public bool FromUser { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string[] ReferenceImageUrls { get; set; } = Array.Empty<string>();
    public Guid[] ReferenceMediaIds { get; set; } = Array.Empty<Guid>();
    public string AspectRatio { get; set; } = "1:1";
    public string OutputFormat { get; set; } = "png";
    public string Quality { get; set; } = "high";
    public string Resolution { get; set; } = "1K";
    public long? Seed { get; set; }
    public string FileCategory { get; set; } = "ai_image";
    public string? RequestId { get; set; }
    public string? JobId { get; set; }
    public string? LogicalRequestId { get; set; }
    public string? RenderJobId { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public object? Metadata { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class AiImageRenderResult
{
    public bool Success { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ObjectKey { get; set; }
    public Guid? ResultMediaId { get; set; }
    public string? MimeType { get; set; }
    public string? ProviderCode { get; set; }
    public long? ProviderId { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ModelName { get; set; }
    public string UnitType { get; set; } = "image";
    public decimal UnitCostPoints { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal TotalPoints { get; set; }
    public decimal? ProviderRawCost { get; set; }
    public string? BillingLogicalRequestId { get; set; }
    public decimal? EstimatedUsd { get; set; }
    public decimal? ActualUsd { get; set; }
    public decimal ChargedPoints { get; set; }
    public decimal RefundedPoints { get; set; }
    public string? CostSource { get; set; }
    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }
    public string? UsageJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAiImageRenderRouter
{
    Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the provider/model/cost for an image capability from the database, reserves billing before
/// provider submit, delegates rendering, completes or releases the billing hold, and records usage.
/// Provider/model/endpoint/unit cost are data-driven from todox_ai_provider_capability.
/// </summary>
public sealed class AiImageRenderRouter : IAiImageRenderRouter
{
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _repo;
    private readonly IAiImageProviderFactory _imageProviders;
    private readonly IAiImageBillingService _billing;
    private readonly IAiProviderAccountRepository _accounts;
    private readonly IAiProviderCredentialResolver _credentials;
    private readonly ILogger<AiImageRenderRouter> _logger;

    public AiImageRenderRouter(
        IAiProviderService providers,
        AiProviderRepository repo,
        IAiImageProviderFactory imageProviders,
        IAiImageBillingService billing,
        IAiProviderAccountRepository accounts,
        IAiProviderCredentialResolver credentials,
        ILogger<AiImageRenderRouter> logger)
    {
        _providers = providers;
        _repo = repo;
        _imageProviders = imageProviders;
        _billing = billing;
        _accounts = accounts;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken cancellationToken = default)
    {
        var option = await _providers.ResolveProviderForCapabilityAsync(
            request.CapabilityCode, request.ProviderCapabilityId, request.FromUser, cancellationToken);

        var detail = await _repo.GetProviderAsync(option.ProviderId, cancellationToken);
        var capability = detail?.Capabilities.FirstOrDefault(c => c.Id == option.ProviderCapabilityId);
        var factoryKey = ProviderCodeMap.ToFactoryKey(option.ProviderCode);
        var provider = _imageProviders.GetProvider(factoryKey);

        var (cfgResolution, cfgQuality, cfgFormat) = ParseImageConfig(capability?.ConfigJson);
        var resolution = factoryKey.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase)
            ? FirstNonBlank(cfgResolution, request.Resolution) ?? "1K"
            : HighestResolution(cfgResolution, request.Resolution);
        var quality = FirstNonBlank(cfgQuality, request.Quality) ?? "high";
        var outputFormat = FirstNonBlank(cfgFormat, request.OutputFormat) ?? "png";
        var quantity = 1m;
        var unitCost = option.UnitCostPoints;
        var logicalRequestId = ResolveLogicalRequestId(request);
        request.RequestId ??= logicalRequestId;
        request.JobId ??= request.RenderJobId;
        var providerAccount = await ClaimProviderAccountAsync(request, option, cancellationToken);
        ResolvedAiProviderCredential? credential = null;
        if (providerAccount.ProviderAccountId is Guid providerAccountId)
        {
            credential = await _credentials.ResolveAsync(providerAccountId, ct: cancellationToken);
        }
        var billingCost = _billing.BuildConfiguredCost(unitCost, quantity);
        var tariffSnapshotJson = BuildTariffSnapshotJson(detail?.Capabilities, option.CapabilityCode, billingCost.ExchangeRateVndPerUsd, billingCost.TodoXVndPerPoint);
        ValidateYEScaleTariffCoverage(factoryKey, option.ModelName, detail?.ConfigJson, capability?.ConfigJson, tariffSnapshotJson);

        _logger.LogInformation(
            "AI_IMAGE_ROUTER_RESOLVED capability={CapabilityCode} feature={FeatureCode} provider={ProviderCode} model={ModelName} resolution={Resolution} logicalRequestId={LogicalRequestId}",
            request.CapabilityCode, request.FeatureCode, option.ProviderCode, option.ModelName, resolution, logicalRequestId);

        var reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = logicalRequestId,
            RenderJobId = request.RenderJobId ?? request.JobId,
            CustomerId = request.CustomerGuid,
            UserId = request.UserId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderAccountId = providerAccount.ProviderAccountId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            FeatureCode = request.FeatureCode,
            RequestedModel = option.ModelName,
            Cost = billingCost,
            TrustedPayerContext = request.TrustedPayerContext,
            TariffSnapshotJson = tariffSnapshotJson,
            Metadata = request.Metadata,
            CreatedBy = request.CreatedBy
        }, cancellationToken);

        if (!reservation.Ok || !reservation.ShouldSubmitProvider)
        {
            await ReleaseProviderAccountAsync(providerAccount, reservation.Ok ? "already_reserved" : "billing_blocked", CancellationToken.None);
            _logger.LogWarning(
                "AI_IMAGE_BILLING_BLOCKED capability={CapabilityCode} feature={FeatureCode} logicalRequestId={LogicalRequestId} status={Status} error={Error}",
                request.CapabilityCode, request.FeatureCode, logicalRequestId, reservation.Status, reservation.ErrorMessage);
            return new AiImageRenderResult
            {
                Success = false,
                ProviderCode = option.ProviderCode,
                ProviderId = option.ProviderId,
                ProviderCapabilityId = option.ProviderCapabilityId,
                ModelName = option.ModelName,
                UnitType = option.UnitType,
                UnitCostPoints = unitCost,
                Quantity = quantity,
                TotalPoints = unitCost * quantity,
                BillingLogicalRequestId = logicalRequestId,
                EstimatedUsd = billingCost.ProviderEstimatedCostUsd,
                ActualUsd = null,
                ChargedPoints = reservation.ChargedPoints,
                RefundedPoints = 0,
                CostSource = billingCost.ProviderCostSource,
                ErrorMessage = reservation.ErrorMessage ?? "Image billing reservation did not allow provider submit."
            };
        }

        OpenRouterImageResponse response;
        var providerSubmitted = false;
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = logicalRequestId,
                    Success = false,
                    ActualModel = option.ModelName,
                    ErrorMessage = "Image render was canceled before provider submit."
                }, CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }

            providerSubmitted = true;
            response = await provider.GenerateImageAsync(new OpenRouterImageRequest
            {
                UserId = request.UserId,
                CustomerId = null,
                Model = option.ModelName ?? string.Empty,
                Prompt = request.Prompt,
                AspectRatio = request.AspectRatio,
                OutputFormat = outputFormat,
                Quality = quality,
                Resolution = resolution,
                Seed = request.Seed,
                Count = 1,
                FileCategory = request.FileCategory,
                ReferenceImageUrls = request.ReferenceImageUrls,
                ReferenceMediaIds = request.ReferenceMediaIds,
                BaseUrlOverride = detail?.BaseUrl,
                EndpointPath = capability?.EndpointPath,
                ApiKeyConfigName = detail?.ApiKeyConfigName,
                ProviderAccountId = providerAccount.ProviderAccountId,
                ApiKey = credential?.SecretValue,
                ProviderConfigJson = detail?.ConfigJson,
                CapabilityConfigJson = capability?.ConfigJson
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ReleaseProviderAccountAsync(providerAccount, "submit_failed", CancellationToken.None);
            await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = logicalRequestId,
                Success = false,
                ActualModel = option.ModelName,
                ErrorMessage = ex.Message
            }, cancellationToken);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            await ReleaseProviderAccountAsync(providerAccount, "cancelled", CancellationToken.None);
            if (providerSubmitted)
            {
                await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
                {
                    LogicalRequestId = logicalRequestId,
                    ActualModel = option.ModelName,
                    ErrorMessage = ex.Message
                }, CancellationToken.None);
            }
            else
            {
                await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = logicalRequestId,
                    Success = false,
                    ActualModel = option.ModelName,
                    ErrorMessage = ex.Message
                }, CancellationToken.None);
            }

            throw;
        }

        var finalModel = string.IsNullOrWhiteSpace(response.ModelName) ? option.ModelName : response.ModelName;
        var providerTaskId = TryReadTaskId(response.UsageJson);
        AiImageBillingCompletion billingCompletion;
        try
        {
            billingCompletion = await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = logicalRequestId,
                Success = response.Success,
                ActualModel = finalModel,
                ProviderTaskId = providerTaskId,
                ProviderActualCostUsd = response.UsageCost,
                ProviderUsageJson = response.UsageJson,
                TariffSnapshotJson = tariffSnapshotJson,
                ErrorMessage = response.Success ? null : response.ErrorMessage
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AI_IMAGE_BILLING_COMPLETE_EXCEPTION capability={CapabilityCode} feature={FeatureCode} logicalRequestId={LogicalRequestId} providerTaskId={ProviderTaskId}",
                request.CapabilityCode, request.FeatureCode, logicalRequestId, providerTaskId);
            billingCompletion = await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
            {
                LogicalRequestId = logicalRequestId,
                ActualModel = finalModel,
                ProviderTaskId = providerTaskId,
                ProviderUsageJson = response.UsageJson,
                TariffSnapshotJson = tariffSnapshotJson,
                ErrorMessage = ex.Message
            }, CancellationToken.None);
        }
        if (!billingCompletion.Ok)
        {
            await ReleaseProviderAccountAsync(providerAccount, "billing_completion_failed", CancellationToken.None);
            _logger.LogError(
                "AI_IMAGE_BILLING_COMPLETE_FAILED capability={CapabilityCode} feature={FeatureCode} logicalRequestId={LogicalRequestId} status={Status} error={Error}",
                request.CapabilityCode, request.FeatureCode, logicalRequestId, billingCompletion.Status, billingCompletion.ErrorMessage);
            return new AiImageRenderResult
            {
                Success = false,
                ProviderCode = option.ProviderCode,
                ProviderId = option.ProviderId,
                ProviderCapabilityId = option.ProviderCapabilityId,
                ModelName = finalModel,
                UnitType = option.UnitType,
                UnitCostPoints = unitCost,
                Quantity = quantity,
                TotalPoints = reservation.ChargedPoints,
                ProviderRawCost = response.UsageCost,
                ProviderTaskId = providerTaskId,
                BillingLogicalRequestId = logicalRequestId,
                EstimatedUsd = billingCost.ProviderEstimatedCostUsd,
                ActualUsd = response.UsageCost,
                ChargedPoints = reservation.ChargedPoints,
                RefundedPoints = 0,
                CostSource = billingCost.ProviderCostSource,
                RawRequestJson = response.RawRequestJson,
                RawResponseJson = response.RawResponseJson,
                UsageJson = response.UsageJson,
                ErrorMessage = billingCompletion.ErrorMessage ?? "Image billing completion failed."
            };
        }

        var result = new AiImageRenderResult
        {
            Success = response.Success,
            ImageBytes = response.ImageBytes,
            ImageUrl = response.ImageUrl,
            ObjectKey = response.ObjectKey,
            ResultMediaId = response.ResultMediaId,
            MimeType = response.MimeType,
            ProviderCode = option.ProviderCode,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ModelName = finalModel,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            Quantity = quantity,
            TotalPoints = reservation.ChargedPoints,
            ProviderRawCost = response.UsageCost,
            ProviderTaskId = providerTaskId,
            BillingLogicalRequestId = logicalRequestId,
            EstimatedUsd = billingCost.ProviderEstimatedCostUsd,
            ActualUsd = response.UsageCost,
            ChargedPoints = reservation.ChargedPoints,
            RefundedPoints = 0,
            CostSource = billingCost.ProviderCostSource,
            RawRequestJson = response.RawRequestJson,
            RawResponseJson = response.RawResponseJson,
            UsageJson = response.UsageJson,
            ErrorMessage = response.Success ? null : response.ErrorMessage
        };

        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = request.CustomerId,
            CustomerGuid = request.CustomerGuid,
            UserId = request.UserId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderAccountId = providerAccount.ProviderAccountId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            FeatureCode = request.FeatureCode,
            OperationType = request.FileCategory,
            ModelName = finalModel,
            ProviderTaskId = providerTaskId,
            RequestId = request.RequestId,
            RenderJobId = Guid.TryParse(request.RenderJobId ?? request.JobId, out var renderJobId) ? renderJobId : null,
            JobId = request.JobId,
            Quantity = quantity,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            TotalPoints = reservation.ChargedPoints,
            ProviderRawCost = response.UsageCost,
            ProviderCostCurrency = response.UsageCost is null ? null : "usd",
            Status = response.Success ? "success" : "failed",
            ErrorMessage = response.Success ? null : response.ErrorMessage,
            RequestJson = response.RawRequestJson,
            ResponseJson = response.RawResponseJson,
            ProviderUsageJson = response.UsageJson,
            MetadataJson = SerializeMetadata(new
            {
                request = request.Metadata,
                billing = new
                {
                    logicalRequestId,
                    reservation.Status,
                    completionStatus = billingCompletion.Status,
                    reservation.PayerType,
                    billingCost.ProviderEstimatedCostUsd,
                    billingCost.ProviderActualCostUsd,
                    billingCost.ProviderCostSource,
                    billingCost.ExchangeRateVndPerUsd,
                    billingCost.TodoXVndPerPoint,
                    billingCost.ProviderCostPoints,
                    reservation.ChargedPoints,
                    billingCompletion.WalletTransactionId,
                    providerTaskId
                }
            }, response.UsageJson),
            CreatedBy = request.CreatedBy
        }, cancellationToken);

        await ReleaseProviderAccountAsync(providerAccount, response.Success ? "completed" : "failed", CancellationToken.None);

        return result;
    }

    private async Task<AiProviderAccountSelectionResult> ClaimProviderAccountAsync(
        AiImageRenderRequest request,
        ProviderOptionDto option,
        CancellationToken ct)
    {
        var factoryKey = ProviderCodeMap.ToFactoryKey(option.ProviderCode);
        if (factoryKey.Equals("todox_image", StringComparison.OrdinalIgnoreCase))
        {
            return new AiProviderAccountSelectionResult(false, null, null, option.ProviderId, option.ProviderCapabilityId, option.ProviderCode, null, null, "LOCAL_PROVIDER_NO_LEASE_REQUIRED");
        }

        if (!Guid.TryParse(request.RenderJobId ?? request.JobId, out var renderJobId))
        {
            throw new InvalidOperationException("AI_PROVIDER_RENDER_JOB_REQUIRED_FOR_ACCOUNT_LEASE");
        }

        var claim = await _accounts.ClaimAccountAsync(new AiProviderAccountSelectionRequest(
            renderJobId,
            option.ProviderCode,
            option.CapabilityCode,
            request.FileCategory,
            option.ModelName,
            "ai-image-router",
            TimeSpan.FromMinutes(15)), ct);
        if (!claim.Claimed || claim.ProviderAccountId is null)
        {
            throw new InvalidOperationException(claim.Reason ?? "AI_PROVIDER_ACCOUNT_CLAIM_FAILED");
        }

        return claim;
    }

    private async Task ReleaseProviderAccountAsync(AiProviderAccountSelectionResult claim, string reason, CancellationToken ct)
    {
        if (claim.LeaseId is Guid leaseId)
        {
            await _accounts.ReleaseLeaseAsync(leaseId, "ai-image-router", reason, ct);
        }
    }

    private static string? SerializeMetadata(object? metadata, string? providerUsageJson)
    {
        try
        {
            using var providerUsage = string.IsNullOrWhiteSpace(providerUsageJson) ? null : JsonDocument.Parse(providerUsageJson);
            if (metadata is null && providerUsage is null) return null;
            return JsonSerializer.Serialize(new
            {
                request = metadata,
                providerUsage = providerUsage?.RootElement
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return metadata is null ? null : JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }

    private static string? BuildTariffSnapshotJson(
        IEnumerable<AiProviderCapabilityDto>? capabilities,
        string capabilityCode,
        decimal exchangeRateVndPerUsd,
        decimal todoxVndPerPoint)
    {
        if (capabilities is null) return null;
        var tariffs = capabilities
            .Where(c => c.Enabled
                        && string.Equals(c.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(c.ModelName))
            .Select(c =>
            {
                var estimatedUsd = TryReadDecimal(c.ConfigJson, "provider_estimated_cost_usd")
                    ?? AiImageBillingCost.ToUsd(c.UnitCostPoints, exchangeRateVndPerUsd, todoxVndPerPoint);
                return new
                {
                    model = c.ModelName,
                    providerCapabilityId = c.Id,
                    unitCostPoints = c.UnitCostPoints,
                    providerEstimatedCostUsd = estimatedUsd,
                    costSource = TryReadString(c.ConfigJson, "cost_source") ?? "configured_tariff",
                    configJson = c.ConfigJson,
                    capturedAtUtc = DateTimeOffset.UtcNow
                };
            })
            .ToArray();

        return tariffs.Length == 0
            ? null
            : JsonSerializer.Serialize(new { tariffs }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void ValidateYEScaleTariffCoverage(
        string factoryKey,
        string? requestedModel,
        string? providerConfigJson,
        string? capabilityConfigJson,
        string? tariffSnapshotJson)
    {
        if (!factoryKey.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var config = YEScaleImageModelMapper.ParseConfig(providerConfigJson, capabilityConfigJson);
        var chain = YEScaleImageModelMapper.BuildAttemptChain(requestedModel ?? string.Empty, config);
        var snapshot = AiImageTariffSnapshot.Parse(tariffSnapshotJson);
        var missing = chain.Where(model => snapshot.Find(model) is null).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"YEScale image tariff snapshot missing for model(s): {string.Join(", ", missing)}.");
        }
    }

    private static decimal? TryReadDecimal(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number)
                ? number
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadString(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadString(doc.RootElement, name);
        }
        catch
        {
            return null;
        }
    }

    private static (string? Resolution, string? Quality, string? OutputFormat) ParseImageConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, null);
            return (ReadString(root, "resolution"), ReadString(root, "quality"), ReadString(root, "output_format"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()
            : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string HighestResolution(params string?[] values)
    {
        var best = "2K";
        foreach (var value in values)
        {
            var normalized = OpenRouterImageService.NormalizeResolution(value);
            if (ResolutionRank(normalized) > ResolutionRank(best))
            {
                best = normalized;
            }
        }

        return best;
    }

    private static int ResolutionRank(string resolution)
        => resolution.Trim().ToUpperInvariant() switch
        {
            "1K" => 1,
            "2K" => 2,
            "4K" => 4,
            "8K" => 8,
            _ => 2
        };

    private static string ResolveLogicalRequestId(AiImageRenderRequest request)
        => FirstNonBlank(request.LogicalRequestId, request.RequestId, request.JobId)
           ?? $"image-{Guid.NewGuid():N}";

    private static string? TryReadTaskId(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(usageJson);
            var root = doc.RootElement;
            return ReadString(root, "taskId") ?? ReadString(root, "task_id");
        }
        catch
        {
            return null;
        }
    }
}
