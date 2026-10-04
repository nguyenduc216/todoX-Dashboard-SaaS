using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoSceneActionResult(bool Success, string? ErrorCode, string? ErrorMessage, long? SceneId = null, int? SceneIndex = null)
{
    public static RVideoSceneActionResult Ok(long? sceneId = null, int? sceneIndex = null) => new(true, null, null, sceneId, sceneIndex);
    public static RVideoSceneActionResult Fail(string code, string message) => new(false, code, message);
}

public interface IRVideoSceneCollectionAction
{
    Task<RVideoSceneActionResult> AddSceneAsync(long projectId, VideoProjectAddSceneRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<RVideoSceneActionResult> DeleteSceneAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default);
    Task<RVideoSceneActionResult> MoveSceneAsync(long projectId, long sceneId, int direction, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class RVideoSceneCollectionAction : IRVideoSceneCollectionAction
{
    private readonly VideoRenderRepository _repo;
    public RVideoSceneCollectionAction(VideoRenderRepository repo) => _repo = repo;

    public async Task<RVideoSceneActionResult> AddSceneAsync(long projectId, VideoProjectAddSceneRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            var scene = await _repo.AddSceneAsync(projectId, request, user, ct);
            return RVideoSceneActionResult.Ok(scene.Id, scene.SceneIndex);
        }
        catch (Exception ex) { return RVideoSceneActionResult.Fail("ADD_SCENE_FAILED", ex.Message); }
    }

    public async Task<RVideoSceneActionResult> DeleteSceneAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default)
    {
        try { await _repo.DeleteSceneAsync(projectId, sceneId, user, ct); return RVideoSceneActionResult.Ok(); }
        catch (Exception ex) { return RVideoSceneActionResult.Fail("DELETE_SCENE_FAILED", ex.Message); }
    }

    public async Task<RVideoSceneActionResult> MoveSceneAsync(long projectId, long sceneId, int direction, CurrentUserSession user, CancellationToken ct = default)
    {
        if (direction is not -1 and not 1) return RVideoSceneActionResult.Fail("INVALID_DIRECTION", "Direction must be -1 or 1.");
        try { await _repo.MoveSceneAsync(projectId, sceneId, direction, user, ct); return RVideoSceneActionResult.Ok(); }
        catch (Exception ex) { return RVideoSceneActionResult.Fail("MOVE_SCENE_FAILED", ex.Message); }
    }
}
