using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoAudioRecoveryService
{
    Task<RVideoAudioRecoveryResult?> RecoverAsync(
        long projectId,
        CurrentUserSession user,
        CancellationToken ct = default);
}

public sealed record RVideoAudioRecoverySceneResult(
    long SceneId,
    int SceneIndex,
    string Action,
    string? ErrorCode = null);

public sealed record RVideoAudioRecoveryResult(
    long ProjectId,
    int TotalScenes,
    int EligibleScenes,
    int EnqueueRequested,
    int SkippedScenes,
    int FailedScenes,
    IReadOnlyList<RVideoAudioRecoverySceneResult> Scenes);

public sealed class RVideoAudioRecoveryService : IRVideoAudioRecoveryService
{
    public const string TriggerSource = "ADMIN_AUDIO_RECOVERY";

    private readonly VideoRenderRepository _projects;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRVideoSceneAudioAutoChainService _audioAutoChain;

    public RVideoAudioRecoveryService(
        VideoRenderRepository projects,
        RVideoJobSettingsRepository settings,
        IRVideoSceneAudioAutoChainService audioAutoChain)
    {
        _projects = projects;
        _settings = settings;
        _audioAutoChain = audioAutoChain;
    }

    public async Task<RVideoAudioRecoveryResult?> RecoverAsync(
        long projectId,
        CurrentUserSession user,
        CancellationToken ct = default)
    {
        var project = await _projects.GetProjectAsync(projectId, user, ct);
        if (project is null)
        {
            return null;
        }

        var settings = await _settings.GetAsync(projectId, ct);
        var result = await RecoverEligibleScenesAsync(
            project,
            settings,
            scene => _audioAutoChain.TryEnqueueSceneAudioAsync(projectId, scene.Id, TriggerSource, ct),
            ct);

        await _projects.AddProjectEventAsync(
            projectId,
            "PROJECT_AUDIO_RECOVERY_REQUESTED",
            result.FailedScenes > 0 ? "warning" : "info",
            "Missing external scene audio recovery was evaluated.",
            new
            {
                projectId,
                eligibleSceneCount = result.EligibleScenes,
                requestedCount = result.EnqueueRequested,
                skippedCount = result.SkippedScenes,
                failedCount = result.FailedScenes,
                triggerSource = TriggerSource
            },
            ct);

        return result;
    }

    public static async Task<RVideoAudioRecoveryResult> RecoverEligibleScenesAsync(
        VideoProjectDto project,
        RVideoJobSettingsDto? settings,
        Func<VideoProjectSceneDto, Task<bool>> tryEnqueue,
        CancellationToken ct = default)
    {
        var results = new List<RVideoAudioRecoverySceneResult>(project.Scenes.Count);
        foreach (var scene in project.Scenes.OrderBy(x => x.SceneIndex))
        {
            ct.ThrowIfCancellationRequested();
            if (!RVideoRules.RequiresExternalVoice(scene, settings))
            {
                results.Add(new(scene.Id, scene.SceneIndex, "skipped_not_external_voice"));
                continue;
            }

            try
            {
                var requested = await tryEnqueue(scene);
                results.Add(new(scene.Id, scene.SceneIndex, requested ? "enqueue_requested" : "skipped"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new(scene.Id, scene.SceneIndex, "failed", ToSafeErrorCode(ex)));
            }
        }

        var eligible = results.Count(x => x.Action != "skipped_not_external_voice");
        var requestedCount = results.Count(x => x.Action == "enqueue_requested");
        var failed = results.Count(x => x.Action == "failed");
        return new(
            project.Id,
            project.Scenes.Count,
            eligible,
            requestedCount,
            results.Count - requestedCount - failed,
            failed,
            results);
    }

    public static string ToSafeErrorCode(Exception exception)
    {
        var candidate = exception.Message.ReplaceLineEndings(string.Empty).Trim();
        return candidate.Length is > 0 and <= 100
               && candidate.All(ch => char.IsAsciiLetterUpper(ch) || char.IsDigit(ch) || ch == '_')
            ? candidate
            : "RVIDEO_AUDIO_RECOVERY_FAILED";
    }
}
