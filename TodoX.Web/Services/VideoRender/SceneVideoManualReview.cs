using System.Text.Json;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public static class SceneVideoManualReview
{
    public const string ErrorCode = "SCENE_VIDEO_MANUAL_REVIEW_REQUIRED";
    public const string Message = "Cần đối soát nhà cung cấp trước khi thử lại video.";

    public static bool RequiresReview(RenderJobDto job)
    {
        if (!string.Equals(job.JobType, RenderJobTypes.RenderSceneVideo, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(job.ErrorCode, ErrorCode, StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var json = JsonDocument.Parse(job.InputJson);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("sceneVideoManualReviewAt", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}