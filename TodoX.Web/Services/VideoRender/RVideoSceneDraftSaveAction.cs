using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneDraftSaveAction
{
    Task<RVideoSceneActionResult> SaveAsync(VideoProjectSaveSceneDraftRequest request, Guid? selectedBy, ScenePromptMetadata metadata, CancellationToken ct = default);
}

public sealed class RVideoSceneDraftSaveAction : IRVideoSceneDraftSaveAction
{
    private readonly VideoRenderRepository _repo;
    private readonly IRVideoSceneDraftValidator _validator;
    public RVideoSceneDraftSaveAction(VideoRenderRepository repo, IRVideoSceneDraftValidator validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<RVideoSceneActionResult> SaveAsync(VideoProjectSaveSceneDraftRequest request, Guid? selectedBy, ScenePromptMetadata metadata, CancellationToken ct = default)
    {
        var validation = _validator.Validate(metadata);
        if (!validation.IsValid)
            return RVideoSceneActionResult.Fail(validation.ErrorCode ?? "VALIDATION_FAILED", validation.ErrorMessage ?? "Validation failed.");

        // normalize: trim prompts
        request.ScenePrompt = metadata.Serialize();
        request.ImagePrompt = metadata.ImagePrompt;
        request.VideoPrompt = metadata.MotionPrompt;

        try
        {
            await _repo.SaveSceneDraftAsync(request, selectedBy, ct);
            return RVideoSceneActionResult.Ok(request.SceneId, request.SceneIndex);
        }
        catch (Exception ex)
        {
            return RVideoSceneActionResult.Fail("SAVE_DRAFT_FAILED", ex.Message);
        }
    }
}
