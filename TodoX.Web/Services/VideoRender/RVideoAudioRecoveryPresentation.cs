namespace TodoX.Web.Services.VideoRender;

public static class RVideoAudioRecoveryPresentation
{
    public static string BuildSummary(RVideoAudioRecoveryResult result)
    {
        if (result.FailedScenes == 0 && result.SkippedScenes == 0)
        {
            return $"Đã yêu cầu khôi phục Voice cho {result.EnqueueRequested}/{result.TotalScenes} scene.";
        }

        var summary = $"Đã yêu cầu: {result.EnqueueRequested} • Bỏ qua: {result.SkippedScenes}";
        if (result.FailedScenes == 0)
        {
            return summary;
        }

        var errorCodes = result.Scenes
            .Where(scene => scene.Action == "failed" && !string.IsNullOrWhiteSpace(scene.ErrorCode))
            .Select(scene => scene.ErrorCode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var errorSuffix = errorCodes.Length == 0
            ? string.Empty
            : $" • Mã lỗi: {string.Join(", ", errorCodes)}";
        return $"{summary} • Lỗi: {result.FailedScenes}{errorSuffix}";
    }
}
