namespace TodoX.Web.Services.VideoRender;

public sealed class RVideoSceneDraftValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public static RVideoSceneDraftValidationResult Ok() => new() { IsValid = true };
    public static RVideoSceneDraftValidationResult Fail(string code, string message) => new() { IsValid = false, ErrorCode = code, ErrorMessage = message };
}

public interface IRVideoSceneDraftValidator
{
    RVideoSceneDraftValidationResult Validate(ScenePromptMetadata metadata);
    RVideoSceneDraftValidationResult Validate(ScenePromptMetadata metadata, int durationSeconds);
}

public sealed class RVideoSceneDraftValidator : IRVideoSceneDraftValidator
{
    private readonly IRVideoSceneVideoModelOptionsService _optionsService;
    public RVideoSceneDraftValidator(IRVideoSceneVideoModelOptionsService optionsService) => _optionsService = optionsService;

    public RVideoSceneDraftValidationResult Validate(ScenePromptMetadata metadata)
    {
        if (metadata is null) return RVideoSceneDraftValidationResult.Fail("SCENE_METADATA_NULL", "Scene metadata is null.");
        if (!ScenePromptMetadata.IsUsableImagePrompt(metadata.EffectiveImagePrompt))
            return RVideoSceneDraftValidationResult.Fail("SCENE_IMAGE_SOURCE_UNRESOLVED", "SCENE_IMAGE_SOURCE_UNRESOLVED: prompt anh la placeholder va khong co fallback dung duoc.");
        if (string.IsNullOrWhiteSpace(metadata.ImagePrompt) && string.IsNullOrWhiteSpace(metadata.EffectiveImagePrompt))
            return RVideoSceneDraftValidationResult.Fail("SCENE_IMAGE_PROMPT_EMPTY", "Image prompt is empty.");

        var cached = _optionsService.GetCachedOptions();
        if (cached is not null && cached.Count > 0)
        {
            if (!metadata.Extra.TryGetValue("video_model", out var modelKey) || string.IsNullOrWhiteSpace(modelKey))
                return RVideoSceneDraftValidationResult.Fail("VIDEO_MODEL_REQUIRED", "Chua chon model video.");
            var opt = cached.FirstOrDefault(x => string.Equals(x.Key, modelKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (opt is null)
                return RVideoSceneDraftValidationResult.Fail("VIDEO_MODEL_REQUIRED", "Model video khong hop le.");
            // Validate duration if present in metadata Extra or checked via duration stored elsewhere is handled by caller; here check Extra duration or assume DurationSeconds validation is done via metadata Extra video_duration if present
            // Strict: if metadata.Extra contains video_duration, validate; otherwise require caller to pass duration via Scene DurationSeconds check by inspecting Extra["video_duration"]
            // Fallback: also check video_duration override in Extra
            if (metadata.Extra.TryGetValue("video_duration", out var durStr) && int.TryParse(durStr, out var dur))
            {
                if (!opt.Durations.Contains(dur))
                    return RVideoSceneDraftValidationResult.Fail("VIDEO_DURATION_UNSUPPORTED", $"Duration {dur}s khong duoc ho tro boi model {opt.Key}.");
            }
            // If no explicit video_duration extra, we still require that any persisted duration checked by caller; if Extra video_model exists but duration not validated yet, we also check metadata.Extra fallback to ensure model exists - already checked.
            // Additional: if caller stored duration via DurationSeconds outside Extra, we allow; unsupported check will be performed when video_duration extra is populated.
        }

        return RVideoSceneDraftValidationResult.Ok();
    }

    public RVideoSceneDraftValidationResult Validate(ScenePromptMetadata metadata, int durationSeconds)
    {
        var baseResult = Validate(metadata);
        if (!baseResult.IsValid) return baseResult;
        var cached = _optionsService.GetCachedOptions();
        if (cached is null || cached.Count == 0) return RVideoSceneDraftValidationResult.Ok();
        if (!metadata.Extra.TryGetValue("video_model", out var modelKey) || string.IsNullOrWhiteSpace(modelKey)) return baseResult;
        var opt = cached.FirstOrDefault(x => string.Equals(x.Key, modelKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (opt is null) return baseResult;
        if (!opt.Durations.Contains(durationSeconds))
            return RVideoSceneDraftValidationResult.Fail("VIDEO_DURATION_UNSUPPORTED", $"Duration {durationSeconds}s khong duoc ho tro boi model {opt.Key}.");
        return RVideoSceneDraftValidationResult.Ok();
    }
}
