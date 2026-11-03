using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services.VideoRender;

// FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-RVIDEO-PRICING-SNAPSHOT-002.
// Strings contain serialized value snapshots, never references to mutable scene/editor DTOs.
public sealed record RVideoSceneVideoOperationSettings(
    long ProjectId, long SceneId, string ProviderCode, string ModelCode, string ModelDisplayName,
    string? Mode, int DurationSeconds, string AspectRatio, string Resolution,
    Guid? SelectedImageVersionId, string VideoPrompt, string SceneConfigJson, string WorkItemJson,
    Guid? ServiceId, string QualityTier, decimal VideoRate, string RateUnit, string RateSource,
    decimal EstimatedPoints)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SceneVideoRenderWorkItemInput CreateWorkItem()
        => JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(WorkItemJson, JsonOptions)
            ?? throw new InvalidOperationException("Invalid confirmed video settings.");

    public bool MatchesRate(PointPricingRate rate)
        => rate.ResourceType == PointPricingResourceTypes.Video && rate.QualityTier == QualityTier
            && rate.Rate == VideoRate && rate.Unit == RateUnit && rate.Source == RateSource
            && RVideoSceneVideoCompletionService.CalculateActualVideoPoints(DurationSeconds, rate.Rate) == EstimatedPoints;

    public async Task<bool> IsPriceCurrentAsync(IPointPricingService? pricing, CancellationToken ct = default)
        => pricing is not null && MatchesRate(await pricing.ResolveRateAsync(
            ServiceId, PointPricingResourceTypes.Video, QualityTier, ct));

    public static string CaptureSceneConfig(VideoProjectSceneDto scene)
        => JsonSerializer.Serialize(new { scene.ScenePrompt, scene.ImagePrompt, scene.VideoPrompt, scene.DurationSeconds,
            scene.VoiceText, scene.VoiceInstruction, scene.StaticImageUrl, scene.StaticImagePath }, JsonOptions);

    public static RVideoSceneVideoOperationSettings Capture(VideoProjectSceneDto scene,
        SceneVideoRenderWorkItemInput input, string modelDisplayName, Guid? serviceId, PointPricingRate rate)
        => new(input.ProjectId, input.SceneId, input.ProviderCode, input.ModelName!, modelDisplayName,
            input.RequestedMode, input.DurationSeconds, input.AspectRatio, input.Resolution,
            input.SelectedSourceImageVersionId, input.VideoPrompt!,
            CaptureSceneConfig(scene),
            JsonSerializer.Serialize(input, JsonOptions), serviceId, rate.QualityTier, rate.Rate,
            rate.Unit, rate.Source, RVideoSceneVideoCompletionService.CalculateActualVideoPoints(input.DurationSeconds, rate.Rate));
}
