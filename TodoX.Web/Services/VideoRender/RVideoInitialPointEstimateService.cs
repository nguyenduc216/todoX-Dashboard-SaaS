using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoInitialPointEstimateService
{
    Task<RVideoInitialPointEstimate> EstimateInitialRVideoPointsAsync(
        RVideoInitialPointEstimateRequest request,
        CancellationToken ct = default);
}

public sealed record RVideoInitialPointEstimateRequest(
    Guid BillingOperationId,
    Guid ParentRenderJobId,
    long ProjectId,
    Guid? ServiceId,
    Guid? CustomerId,
    VideoProjectDto Project,
    IReadOnlyList<VideoProjectSceneDto> BillingScenes,
    IReadOnlyList<VideoProjectSceneDto> ImageWorkScenes,
    RVideoJobSettingsDto? Settings,
    Func<VideoProjectSceneDto, CancellationToken, Task<SceneImageVersionDto?>>? SelectedImageResolver,
    string ImageQuality,
    string VideoQuality,
    string VoiceQuality);

public sealed record RVideoInitialPointEstimate(
    Guid BillingOperationId,
    Guid ParentRenderJobId,
    long ProjectId,
    Guid? ServiceId,
    int ImageCount,
    decimal ImageRate,
    decimal ImagePoints,
    int VideoSeconds,
    decimal VideoRate,
    decimal VideoPoints,
    int VoiceCount,
    decimal VoiceRate,
    decimal VoicePoints,
    decimal TotalPoints,
    decimal AvailablePoints,
    decimal RemainingPoints,
    decimal MissingPoints,
    bool CanStart,
    IReadOnlyList<PreRenderVideoScene> VideoScenes,
    PointPricingEstimate Pricing)
{
    public object ToSnapshot() => new
    {
        billing_operation_id = BillingOperationId,
        billingOperationId = BillingOperationId,
        parent_render_job_id = ParentRenderJobId,
        parentRenderJobId = ParentRenderJobId,
        project_id = ProjectId,
        projectId = ProjectId,
        service_id = ServiceId,
        serviceId = ServiceId,
        image = new
        {
            planned_count = ImageCount,
            plannedCount = ImageCount,
            rate = ImageRate,
            points = ImagePoints
        },
        video = new
        {
            scene_durations = VideoScenes.Select(x => new
            {
                scene_id = x.SceneId,
                sceneId = x.SceneId,
                duration_seconds = x.DurationSeconds,
                durationSeconds = x.DurationSeconds
            }).ToArray(),
            total_seconds = VideoSeconds,
            totalSeconds = VideoSeconds,
            rate = VideoRate,
            points = VideoPoints
        },
        voice = new
        {
            planned_count = VoiceCount,
            plannedCount = VoiceCount,
            rate = VoiceRate,
            points = VoicePoints
        },
        total_points = TotalPoints,
        totalPoints = TotalPoints,
        available_points = AvailablePoints,
        availablePoints = AvailablePoints,
        remaining_points = RemainingPoints,
        remainingPoints = RemainingPoints,
        missing_points = MissingPoints,
        missingPoints = MissingPoints,
        can_start = CanStart,
        canStart = CanStart
    };
}

public sealed class RVideoInitialPointEstimateService : IRVideoInitialPointEstimateService
{
    private readonly IPointPricingService _pointPricing;
    private readonly WalletService _wallets;
    private readonly TokenSettingsService _tokenSettings;

    public RVideoInitialPointEstimateService(IPointPricingService pointPricing, WalletService wallets, TokenSettingsService tokenSettings)
    {
        _pointPricing = pointPricing;
        _wallets = wallets;
        _tokenSettings = tokenSettings;
    }

    public async Task<RVideoInitialPointEstimate> EstimateInitialRVideoPointsAsync(
        RVideoInitialPointEstimateRequest request,
        CancellationToken ct = default)
    {
        var billingScenes = request.BillingScenes
            .OrderBy(x => x.SceneIndex)
            .ToArray();
        var imageSources = new List<RVideoEffectiveSceneImageSource>();
        foreach (var scene in request.ImageWorkScenes)
        {
            var selected = request.SelectedImageResolver is null
                ? null
                : await request.SelectedImageResolver(scene, ct);
            imageSources.Add(RVideoEffectiveSceneImageSourceResolver.Resolve(scene, request.Settings, selected, request.Project));
        }
        var staticInputCount = StaticImageBillingPolicy.ResolveRVideoStaticInputCount(imageSources);
        var chargeStaticImagePoints = await _tokenSettings.GetChargeStaticImagePointsAsync();
        var imageCount = StaticImageBillingPolicy.ResolveBillableStaticImageCount(staticInputCount, chargeStaticImagePoints);

        var videoScenes = billingScenes
            .Select(scene => new PreRenderVideoScene(scene.Id, scene.DurationSeconds))
            .ToArray();
        var voiceCount = request.Settings is not null
            ? billingScenes.Count(scene => RVideoRules.RequiresExternalVoice(scene, request.Settings)
                && !string.IsNullOrWhiteSpace(RVideoRules.ResolveSceneVoiceText(scene)))
            : 0;

        var plan = new PreRenderUsagePlan(
            request.ServiceId,
            imageCount,
            request.ImageQuality,
            videoScenes,
            request.VideoQuality,
            voiceCount,
            request.VoiceQuality,
            request.Settings is not null && RVideoRules.ResolveVoiceMode(request.Settings) == RVideoVoiceModes.Library)
            .Validate();
        var pricing = await _pointPricing.EstimateAsync(plan.ToPricingRequest(), ct);
        var available = request.CustomerId is Guid customerId
            ? await _wallets.GetBalanceAsync(customerId)
            : 0m;
        var remaining = Math.Max(0m, available - pricing.TotalPoints);
        var missing = Math.Max(0m, pricing.TotalPoints - available);

        return new RVideoInitialPointEstimate(
            request.BillingOperationId,
            request.ParentRenderJobId,
            request.ProjectId,
            request.ServiceId,
            plan.ImageCount,
            pricing.Image.Rate,
            pricing.Image.Points,
            plan.VideoSeconds,
            pricing.Video.Rate,
            pricing.Video.Points,
            plan.VoiceCount,
            pricing.Voice.Rate,
            pricing.Voice.Points,
            pricing.TotalPoints,
            available,
            remaining,
            missing,
            request.CustomerId is null || available >= pricing.TotalPoints,
            videoScenes,
            pricing);
    }
}

public static class RVideoParentBillingState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static Guid ResolveBillingOperationId(VideoProjectDto project, Guid parentRenderJobId)
        => project.CoreJobId ?? parentRenderJobId;

    public static bool HasCurrentOperationParentCharge(
        IEnumerable<VideoProjectEventDto> events,
        Guid billingOperationId)
        => TryFindCurrentOperationParentCharge(events, billingOperationId, out _);

    public static bool HasCurrentOperationParentVoiceCharge(
        IEnumerable<VideoProjectEventDto> events,
        Guid billingOperationId)
        => TryFindCurrentOperationParentCharge(events, billingOperationId, out _, requireVoice: true);

    public static bool TryFindCurrentOperationParentCharge(
        IEnumerable<VideoProjectEventDto> events,
        Guid billingOperationId,
        out Guid chargeReferenceId)
        => TryFindCurrentOperationParentCharge(events, billingOperationId, out chargeReferenceId, requireVoice: false);

    private static bool TryFindCurrentOperationParentCharge(
        IEnumerable<VideoProjectEventDto> events,
        Guid billingOperationId,
        out Guid chargeReferenceId,
        bool requireVoice)
    {
        foreach (var ev in events.Where(x => x.EventType == "RVIDEO_PARENT_BILLED").OrderByDescending(x => x.CreatedAt))
        {
            if (string.IsNullOrWhiteSpace(ev.DataJson))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(ev.DataJson);
                var root = doc.RootElement;
                var eventOperationId = ReadGuid(root, "billingOperationId") ?? ReadGuid(root, "billing_operation_id");
                var eventChargeReferenceId = ReadGuid(root, "chargeReferenceId") ?? ReadGuid(root, "charge_reference_id");
                var hasVoice = !requireVoice || ReadDecimal(root, "voicePoints") > 0 || ReadInt(root, "voiceCount") > 0 || ReadInt(root, "voice_planned_count") > 0;
                if (eventOperationId == billingOperationId
                    && eventChargeReferenceId is Guid referenceId)
                {
                    chargeReferenceId = referenceId;
                    if (!hasVoice)
                    {
                        continue;
                    }
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        chargeReferenceId = Guid.Empty;
        return false;
    }

    private static Guid? ReadGuid(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int ReadInt(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static decimal ReadDecimal(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var value))
        {
            return 0m;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed)
            ? parsed
            : 0m;
    }
}
