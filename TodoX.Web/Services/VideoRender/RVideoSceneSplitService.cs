using System.Text.RegularExpressions;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

// FEATURE: split a scene whose video cannot be produced (e.g. a long scene after every model failed)
// into two consecutive scenes. Part 2 always continues from the last frame of part 1's video
// (link_mode=frame), so the two clips join seamlessly. Part 1 keeps its id and full video history.
public static class RVideoSceneSplit
{
    public const int MinPartSeconds = 4;
    public const string SplitFromKey = "split_from_scene";

    public static bool CanSplit(int durationSeconds) => durationSeconds >= MinPartSeconds * 2;

    /// <summary>Halves the duration; an odd second goes to the first part (13 → 7 + 6).</summary>
    public static (int First, int Second) SplitDuration(int durationSeconds)
        => ((durationSeconds + 1) / 2, durationSeconds / 2);

    /// <summary>Splits narration at the sentence boundary that best balances both halves (words if one sentence).</summary>
    public static (string? First, string? Second) SplitVoice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (text, text);
        var value = text.Trim();
        var units = Regex.Split(value, @"(?<=[\.\!\?…。！？;；])\s+").Where(x => x.Length > 0).ToList();
        var separator = " ";
        if (units.Count < 2)
        {
            units = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (units.Count < 2) return (value, null);
        }
        var best = 1;
        var bestDiff = int.MaxValue;
        for (var cut = 1; cut < units.Count; cut++)
        {
            var left = string.Join(separator, units.Take(cut)).Length;
            var right = string.Join(separator, units.Skip(cut)).Length;
            var diff = Math.Abs(left - right);
            if (diff < bestDiff) { bestDiff = diff; best = cut; }
        }
        return (string.Join(separator, units.Take(best)), string.Join(separator, units.Skip(best)));
    }

    public static string PartPrompt(string? motionPrompt, int part)
    {
        var baseText = (motionPrompt ?? string.Empty).Trim();
        var note = part == 1
            ? "(Part 1 of 2: show the first half of this action; end on a natural, continuous pose so the next shot can continue it.)"
            : "(Part 2 of 2: continue seamlessly from the previous shot's final frame and show the second half of this action.)";
        return string.IsNullOrEmpty(baseText) ? note : $"{baseText} {note}";
    }
}

public interface IRVideoSceneSplitService
{
    Task<RVideoSceneActionResult> SplitAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class RVideoSceneSplitService : IRVideoSceneSplitService
{
    private readonly VideoRenderRepository _repo;
    private readonly ILogger<RVideoSceneSplitService> _logger;

    public RVideoSceneSplitService(VideoRenderRepository repo, ILogger<RVideoSceneSplitService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<RVideoSceneActionResult> SplitAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            var project = await _repo.GetProjectAsync(projectId, ct);
            var scene = project?.Scenes.FirstOrDefault(x => x.Id == sceneId);
            if (project is null || scene is null) return RVideoSceneActionResult.Fail("SCENE_NOT_FOUND", "Không tìm thấy cảnh.");
            if (!RVideoSceneSplit.CanSplit(scene.DurationSeconds))
                return RVideoSceneActionResult.Fail("SCENE_TOO_SHORT", $"Cảnh cần tối thiểu {RVideoSceneSplit.MinPartSeconds * 2} giây để tách thành 2 cảnh.");

            var metadata = ScenePromptMetadata.FromScene(scene);
            var (firstSeconds, secondSeconds) = RVideoSceneSplit.SplitDuration(scene.DurationSeconds);
            var (firstVoice, secondVoice) = RVideoSceneSplit.SplitVoice(scene.VoiceText ?? metadata.Voice);
            var motion = metadata.MotionPrompt ?? scene.VideoPrompt;
            var title = string.IsNullOrWhiteSpace(scene.Title) ? $"Scene {scene.SceneIndex:00}" : scene.Title!.Trim();

            var second = await _repo.AddSceneAsync(projectId, new VideoProjectAddSceneRequest
            {
                InsertAfterSceneIndex = scene.SceneIndex,
                Title = $"{title} (2/2)",
                Purpose = metadata.ScenePurpose,
                DurationSeconds = secondSeconds,
                ImagePrompt = scene.ImagePrompt,
                VideoPrompt = RVideoSceneSplit.PartPrompt(motion, 2),
                Voice = secondVoice,
                VoiceInstruction = metadata.VoiceInstruction,
                AspectRatio = metadata.Extra.TryGetValue("aspect_ratio", out var aspect) ? aspect : null
            }, user, ct);

            var secondMetadata = ScenePromptMetadata.FromScene(second);
            foreach (var key in new[] { "resolution", "video_model" })
                if (metadata.Extra.TryGetValue(key, out var value)) secondMetadata.Extra[key] = value;
            secondMetadata.Extra["video_duration"] = secondSeconds.ToString();
            secondMetadata.Extra[RVideoSceneLink.ModeKey] = RVideoSceneLinkModes.Frame; // mandatory continuation
            secondMetadata.Extra[RVideoSceneSplit.SplitFromKey] = scene.Id.ToString();
            secondMetadata.TtsRate = metadata.TtsRate;
            await _repo.UpdateScenePromptOnlyAsync(second.Id, secondMetadata.Serialize(), ct);

            metadata.MotionPrompt = RVideoSceneSplit.PartPrompt(motion, 1);
            metadata.Voice = firstVoice;
            metadata.Extra["video_duration"] = firstSeconds.ToString();
            await _repo.UpdateSceneForSplitAsync(scene.Id, firstSeconds, metadata.MotionPrompt, firstVoice, metadata.Serialize(), $"{title} (1/2)", ct);

            await _repo.AddProjectEventAsync(projectId, "RVIDEO_SCENE_SPLIT", "info",
                $"Scene {scene.SceneIndex} split into {firstSeconds}s + {secondSeconds}s; part 2 continues from part 1's last frame.",
                new { sceneId = scene.Id, newSceneId = second.Id, firstSeconds, secondSeconds, originalSeconds = scene.DurationSeconds }, ct);
            return RVideoSceneActionResult.Ok(second.Id, second.SceneIndex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RVIDEO_SCENE_SPLIT_FAILED projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return RVideoSceneActionResult.Fail("SPLIT_SCENE_FAILED", ex.Message);
        }
    }
}
