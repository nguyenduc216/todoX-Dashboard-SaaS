using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

// FEATURE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY
// CHANGE_ID: TODOX-20261028-RVIDEO-RERENDER-RESTORE-001
// An explicit new attempt may coexist with completed output; active/unknown work remains blocked.
internal static class RVideoRerenderRequestPolicy
{
    internal static bool CanProceed(VideoRenderEligibilityStatus status, PointBillingIntent intent)
        => status == VideoRenderEligibilityStatus.Eligible
           || (status == VideoRenderEligibilityStatus.AlreadyCompleted && intent == PointBillingIntent.UserRerender);

    internal static bool ApplySavedOverride(ScenePromptMetadata metadata, SceneVideoRenderInput input)
    {
        if (!metadata.Extra.TryGetValue("video_model", out var key) || string.IsNullOrWhiteSpace(key))
            return true;

        var policy = RVideoVideoModelPolicy.Models.FirstOrDefault(x =>
            string.Equals(RVideoSceneVideoModelOptionsService.BuildKey(x.ProviderCode, x.Model, x.Mode), key.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Model, key.Trim(), StringComparison.OrdinalIgnoreCase));
        if (policy is null) return false;

        input.ManualOverride = true;
        input.RequestedModelCode = policy.Model;
        input.RequestedMode = policy.Mode;
        if (metadata.Extra.TryGetValue("video_duration", out var value))
        {
            if (!int.TryParse(value, out var duration) || duration <= 0) return false;
            input.RequestedDurationSeconds = duration;
        }
        return true;
    }
}
