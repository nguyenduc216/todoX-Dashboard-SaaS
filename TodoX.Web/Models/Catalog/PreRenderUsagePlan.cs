namespace TodoX.Web.Models.Catalog;

public sealed record PreRenderVideoScene(long SceneId, int DurationSeconds)
{
    public PreRenderVideoScene Validate()
        => DurationSeconds > 0
            ? this
            : throw new InvalidOperationException("VIDEO_SCENE_DURATION_REQUIRED");
}

public sealed record PreRenderUsagePlan(
    Guid? ServiceId,
    int ImageCount,
    string ImageQuality,
    IReadOnlyList<PreRenderVideoScene> VideoScenes,
    string VideoQuality,
    int VoiceCount,
    string VoiceQuality,
    bool VoiceEnabled)
{
    public int VideoSeconds => VideoScenes.Sum(scene => scene.Validate().DurationSeconds);

    public PreRenderUsagePlan Validate()
    {
        if (ImageCount < 0 || VoiceCount < 0)
        {
            throw new InvalidOperationException("PRE_RENDER_USAGE_INVALID");
        }

        _ = VideoSeconds;
        return this;
    }

    public PointPricingEstimateRequest ToPricingRequest()
        => new(ServiceId, ImageCount, ImageQuality, VideoSeconds, VideoQuality,
            VoiceCount, VoiceQuality, VoiceEnabled);
}
