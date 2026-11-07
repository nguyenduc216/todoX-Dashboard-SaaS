using System.Diagnostics;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

// FEATURE: RVideo linked scenes ("Video liên kết"). A linked scene starts from the last frame of the
// previous scene's selected video, so consecutive clips join without a visual jump. The link is stored
// per scene in ScenePromptMetadata.Extra (no schema change):
//   link_mode            = "regenerate" (new image from last frame + scene image prompt) | "frame" (use the frame as is)
//   linked_video_version = id of the previous scene video version the current image was built from
public static class RVideoSceneLinkModes
{
    public const string Regenerate = "regenerate";
    public const string Frame = "frame";

    public static string? Normalize(string? value)
        => string.Equals(value?.Trim(), Regenerate, StringComparison.OrdinalIgnoreCase) ? Regenerate
            : string.Equals(value?.Trim(), Frame, StringComparison.OrdinalIgnoreCase) ? Frame
            : null;
}

public static class RVideoSceneLink
{
    public const string ModeKey = "link_mode";
    public const string SourceKey = "linked_video_version";

    public static string? GetMode(ScenePromptMetadata metadata)
        => metadata.Extra.TryGetValue(ModeKey, out var value) ? RVideoSceneLinkModes.Normalize(value) : null;

    public static string? GetMode(VideoProjectSceneDto scene) => GetMode(ScenePromptMetadata.FromScene(scene));

    public static Guid? GetSource(VideoProjectSceneDto scene)
        => ScenePromptMetadata.FromScene(scene).Extra.TryGetValue(SourceKey, out var value) && Guid.TryParse(value, out var id)
            ? id
            : null;

    /// <summary>Linked scenes are every scene except the first one that carries a link mode.</summary>
    public static bool IsLinked(VideoProjectSceneDto scene, IReadOnlyList<VideoProjectSceneDto> orderedScenes)
        => GetMode(scene) is not null && Previous(scene, orderedScenes) is not null;

    public static VideoProjectSceneDto? Previous(VideoProjectSceneDto scene, IReadOnlyList<VideoProjectSceneDto> orderedScenes)
        => orderedScenes.Where(x => x.SceneIndex < scene.SceneIndex).OrderByDescending(x => x.SceneIndex).FirstOrDefault();

    /// <summary>
    /// A linked scene may only start its video once its image was built from the previous scene's
    /// currently selected, completed video.
    /// </summary>
    public static bool IsLinkCurrent(VideoProjectSceneDto scene, SceneVideoVersionDto? previousVideo)
        => previousVideo is not null
           && IsCompleted(previousVideo)
           && GetSource(scene) == previousVideo.Id;

    public static bool IsCompleted(SceneVideoVersionDto? video)
        => video is not null && string.Equals(video.Status, "completed", StringComparison.OrdinalIgnoreCase);

    public static string ContinuationInstruction
        => "CONTINUITY: The reference image is the exact last frame of the previous shot. Continue seamlessly from it: "
           + "keep the same characters, faces, outfits, props, lighting, colour grade and camera framing unless the scene "
           + "description below explicitly changes them.";
}

public sealed record RVideoSceneLinkResult(bool Started, string Reason);

public interface IRVideoSceneLinkService
{
    /// <summary>
    /// AUTO lifecycle step: for every linked scene whose previous scene has a completed selected video,
    /// build the linked start image once. Scenes that already have a video are never relinked
    /// automatically (the user is asked instead, because a new video costs points).
    /// </summary>
    Task AdvanceAsync(VideoProjectDto project, RVideoJobSettingsDto setting, CancellationToken ct = default);

    /// <summary>Explicit (user-confirmed) relink of one scene from the previous scene's selected video.</summary>
    Task<RVideoSceneLinkResult> RelinkSceneAsync(VideoProjectDto project, RVideoJobSettingsDto setting, long sceneId, CancellationToken ct = default);

    /// <summary>Linked scenes whose image is stale because the previous scene video changed after they got a video.</summary>
    Task<IReadOnlyList<long>> FindStaleLinkedScenesAsync(VideoProjectDto project, CancellationToken ct = default);

    // User-facing (access-checked) operations used by the dashboard.
    Task<RVideoSceneLinkResult> SetLinkModeAsync(long projectId, long sceneId, string? mode, CurrentUserSession user, CancellationToken ct = default);
    Task<RVideoSceneLinkResult> SetProjectLinkedAsync(long projectId, bool linked, CurrentUserSession user, CancellationToken ct = default);
    Task<RVideoSceneLinkResult> RelinkSceneForUserAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class RVideoSceneLinkService : IRVideoSceneLinkService
{
    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IMediaFileService _media;
    private readonly IRenderJobService _jobs;
    private readonly TenantContext _tenant;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RVideoSceneLinkService> _logger;
    private readonly RVideoJobSettingsRepository? _settings;

    public RVideoSceneLinkService(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IMediaFileService media,
        IRenderJobService jobs,
        TenantContext tenant,
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<RVideoSceneLinkService> logger,
        RVideoJobSettingsRepository? settings = null)
    {
        _settings = settings;
        _repo = repo;
        _versions = versions;
        _media = media;
        _jobs = jobs;
        _tenant = tenant;
        _env = env;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task AdvanceAsync(VideoProjectDto project, RVideoJobSettingsDto setting, CancellationToken ct = default)
    {
        if (setting.UseReferenceImageForAllScenes) return; // videos use the shared image, not scene images
        var ordered = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
        foreach (var scene in ordered.Where(x => RVideoSceneLink.IsLinked(x, ordered)))
        {
            var previous = RVideoSceneLink.Previous(scene, ordered)!;
            var previousVideo = await _versions.GetSelectedVideoVersionAsync(previous.Id, ct);
            if (!RVideoSceneLink.IsCompleted(previousVideo)) continue;             // wait for the previous clip
            if (RVideoSceneLink.IsLinkCurrent(scene, previousVideo)) continue;     // already linked
            if (RVideoSceneLink.IsCompleted(await _versions.GetSelectedVideoVersionAsync(scene.Id, ct)))
                continue;                                                          // ask the user (UI), never auto-rerender
            if (await _versions.HasActiveImageVersionAsync(scene.Id, ct)) continue;
            try
            {
                await LinkAsync(project, setting, scene, previousVideo!, "RVIDEO_LIFECYCLE", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "RVIDEO_SCENE_LINK_FAILED projectId={ProjectId} sceneId={SceneId}", project.Id, scene.Id);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_SCENE_LINK_FAILED", "error",
                    $"Scene {scene.SceneIndex}: could not build the linked start image from the previous scene.",
                    new { sceneId = scene.Id, previousSceneId = previous.Id, previousVideoVersionId = previousVideo!.Id, error = ex.Message }, ct);
            }
        }
    }

    public async Task<RVideoSceneLinkResult> RelinkSceneAsync(VideoProjectDto project, RVideoJobSettingsDto setting, long sceneId, CancellationToken ct = default)
    {
        var ordered = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
        var scene = ordered.FirstOrDefault(x => x.Id == sceneId);
        if (scene is null) return new(false, "Không tìm thấy cảnh.");
        var previous = RVideoSceneLink.Previous(scene, ordered);
        if (previous is null) return new(false, "Cảnh đầu tiên không có cảnh trước để nối.");
        if (setting.UseReferenceImageForAllScenes)
            return new(false, "Dự án đang dùng chung một ảnh tham chiếu cho mọi cảnh nên không nối khung hình được.");
        var previousVideo = await _versions.GetSelectedVideoVersionAsync(previous.Id, ct);
        if (!RVideoSceneLink.IsCompleted(previousVideo)) return new(false, "Cảnh trước chưa có video hoàn tất.");
        if (await _versions.HasActiveImageVersionAsync(scene.Id, ct)) return new(false, "Cảnh đang tạo ảnh, vui lòng chờ.");
        await LinkAsync(project, setting, scene, previousVideo!, "USER_RELINK", ct);
        return new(true, "Đã bắt đầu tạo ảnh nối tiếp từ khung cuối của cảnh trước.");
    }

    public async Task<IReadOnlyList<long>> FindStaleLinkedScenesAsync(VideoProjectDto project, CancellationToken ct = default)
    {
        var ordered = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
        var stale = new List<long>();
        foreach (var scene in ordered.Where(x => RVideoSceneLink.IsLinked(x, ordered)))
        {
            var previousVideo = await _versions.GetSelectedVideoVersionAsync(RVideoSceneLink.Previous(scene, ordered)!.Id, ct);
            if (!RVideoSceneLink.IsCompleted(previousVideo) || RVideoSceneLink.IsLinkCurrent(scene, previousVideo)) continue;
            if (RVideoSceneLink.IsCompleted(await _versions.GetSelectedVideoVersionAsync(scene.Id, ct)))
                stale.Add(scene.Id);
        }
        return stale;
    }

    public async Task<RVideoSceneLinkResult> SetLinkModeAsync(long projectId, long sceneId, string? mode, CurrentUserSession user, CancellationToken ct = default)
    {
        var project = await LoadAccessibleAsync(projectId, user, ct);
        var scene = project?.Scenes.FirstOrDefault(x => x.Id == sceneId);
        if (project is null || scene is null) return new(false, "Không tìm thấy cảnh hoặc bạn không có quyền.");
        var metadata = ScenePromptMetadata.FromScene(scene);
        if (metadata.Extra.ContainsKey(RVideoSceneSplit.SplitFromKey) && RVideoSceneLinkModes.Normalize(mode) is null)
            return new(false, "Cảnh được tách ra luôn nối tiếp cảnh trước, không thể tắt nối.");
        SetMode(metadata, mode);
        await _repo.UpdateScenePromptOnlyAsync(scene.Id, metadata.Serialize(), ct);
        return new(true, "Đã lưu cách nối cảnh.");
    }

    public async Task<RVideoSceneLinkResult> SetProjectLinkedAsync(long projectId, bool linked, CurrentUserSession user, CancellationToken ct = default)
    {
        var project = await LoadAccessibleAsync(projectId, user, ct);
        if (project is null) return new(false, "Không tìm thấy dự án hoặc bạn không có quyền.");
        var ordered = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
        foreach (var scene in ordered.Skip(1))
        {
            var metadata = ScenePromptMetadata.FromScene(scene);
            if (metadata.Extra.ContainsKey(RVideoSceneSplit.SplitFromKey)) continue; // split parts stay linked
            var current = RVideoSceneLink.GetMode(metadata);
            if (linked && current is not null) continue;
            if (!linked && current is null) continue;
            SetMode(metadata, linked ? RVideoSceneLinkModes.Regenerate : null);
            await _repo.UpdateScenePromptOnlyAsync(scene.Id, metadata.Serialize(), ct);
        }
        return new(true, linked ? "Đã bật Video liên kết cho các cảnh." : "Đã tắt Video liên kết.");
    }

    public async Task<RVideoSceneLinkResult> RelinkSceneForUserAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default)
    {
        var project = await LoadAccessibleAsync(projectId, user, ct);
        if (project is null || _settings is null) return new(false, "Không tìm thấy dự án hoặc bạn không có quyền.");
        var setting = await _settings.GetAsync(projectId, ct);
        if (setting is null) return new(false, "Dự án chưa có cấu hình RVideo.");
        return await RelinkSceneAsync(project, setting, sceneId, ct);
    }

    /// <summary>Applies link_mode to scenes 2..N of a scene list before it is saved (new projects).</summary>
    public static void ApplyLinkedToNewScenes(IReadOnlyList<VideoProjectSceneDto> scenes)
    {
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex).Skip(1))
        {
            var metadata = ScenePromptMetadata.FromScene(scene);
            if (RVideoSceneLink.GetMode(metadata) is not null) continue;
            SetMode(metadata, RVideoSceneLinkModes.Regenerate);
            scene.ScenePrompt = metadata.Serialize();
        }
    }

    private static void SetMode(ScenePromptMetadata metadata, string? mode)
    {
        var normalized = RVideoSceneLinkModes.Normalize(mode);
        if (normalized is null)
        {
            metadata.Extra.Remove(RVideoSceneLink.ModeKey);
            metadata.Extra.Remove(RVideoSceneLink.SourceKey);
        }
        else
        {
            metadata.Extra[RVideoSceneLink.ModeKey] = normalized;
        }
    }

    private async Task<VideoProjectDto?> LoadAccessibleAsync(long projectId, CurrentUserSession user, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        return project is not null && VideoRenderRepository.CanAccessProject(project, user) ? project : null;
    }

    private async Task LinkAsync(VideoProjectDto project, RVideoJobSettingsDto setting, VideoProjectSceneDto scene,
        SceneVideoVersionDto previousVideo, string trigger, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var mode = RVideoSceneLink.GetMode(scene) ?? RVideoSceneLinkModes.Regenerate;
        var frame = await ExtractLastFrameAsync(project, scene, previousVideo, ct);

        // Record the source first: a regenerate job may complete (and trigger the video) before we return.
        var metadata = ScenePromptMetadata.FromScene(scene);
        metadata.Extra[RVideoSceneLink.ModeKey] = mode;
        metadata.Extra[RVideoSceneLink.SourceKey] = previousVideo.Id.ToString();
        await _repo.UpdateScenePromptOnlyAsync(scene.Id, metadata.Serialize(), ct);

        if (mode == RVideoSceneLinkModes.Frame)
        {
            var logicalRequestId = $"rvideo_linked_frame:{scene.Id}:{previousVideo.Id:N}";
            var version = await _versions.CreateQueuedImageVersionAsync(new SceneImageVersionCreateRequest(
                project.Id, scene.Id, project.UserId, project.CustomerId, null, logicalRequestId,
                scene.ImagePrompt, scene.ImagePrompt, scene.VideoPrompt, null,
                new { scene.Id, scene.SceneIndex, scene.DurationSeconds },
                new { linkedFromVideoVersionId = previousVideo.Id, frameMediaId = frame.Id, mode },
                new { source = "rvideo_linked_last_frame", trigger }), ct);
            await _versions.TryCompleteImageVersionAsync(version.Id, new SceneImageVersionCompleteRequest(
                frame.PublicUrl ?? frame.FileUrl, frame.ObjectKey, "todox", "linked_last_frame", null, null, frame.Id,
                null, null, null, 0m, 0m, null, "image/jpeg", "rvideo_linked_last_frame"), ct);
        }
        else
        {
            var reference = RVideoSceneImageReferenceSelection.Resolve(setting);
            var renderSettings = RVideoRules.ResolveRenderSettings(project.OriginalPrompt);
            var userId = project.UserId ?? Guid.Empty;
            await _jobs.EnqueueAsync(new RenderJobCreateModel
            {
                JobType = SceneImageBatchRenderHandler.JobTypeName,
                UserId = userId,
                CustomerId = project.CustomerId,
                Input = new SceneImageBatchInput
                {
                    ProjectId = project.Id,
                    CapabilityCode = SceneImageRenderContext.RVideoCapabilityCode,
                    ReferenceSource = reference.Source,
                    AspectRatio = renderSettings.AspectRatio,
                    CharacterId = reference.CharacterId,
                    CharacterReferenceObjectKey = reference.ObjectKey,
                    CharacterReferenceUrl = reference.Url,
                    UserId = userId,
                    CustomerId = project.CustomerId,
                    OnlyMissingOrFailed = false,
                    SceneIds = [scene.Id],
                    LinkedReferenceMediaId = frame.Id,
                    LinkedReferenceObjectKey = frame.ObjectKey,
                    LinkedReferenceUrl = frame.PublicUrl ?? frame.FileUrl
                },
                Prompt = new { projectId = project.Id, sceneId = scene.Id, source = "rvideo_linked_scene", trigger },
                LogCode = $"video-image-link-{scene.Id}",
                ProviderCode = SceneImageBatchRenderHandler.RoutingProviderCode,
                ModelCode = SceneImageBatchRenderHandler.RoutingModelCode,
                MaxAttempts = 1,
                PointCostEstimate = 0,
                PointStatus = RenderPointStatuses.NotRequired
            }, ct);
        }

        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_SCENE_LINK_STARTED", "info",
            $"Scene {scene.SceneIndex}: start image linked from the last frame of the previous scene ({mode}).",
            new { sceneId = scene.Id, previousVideoVersionId = previousVideo.Id, frameMediaId = frame.Id, mode, trigger }, ct);
    }

    private async Task<MediaFileDto> ExtractLastFrameAsync(VideoProjectDto project, VideoProjectSceneDto scene,
        SceneVideoVersionDto previousVideo, CancellationToken ct)
    {
        var objectKey = $"rvideo/{_tenant.TenantId}/project-{project.Id}/scene-{scene.Id}/linked/{previousVideo.Id:N}.jpg";
        var existing = await _media.GetByObjectKeyAsync(objectKey, ct);
        if (existing is not null) return existing;

        var input = await ResolveLocalVideoAsync(previousVideo, ct);
        var output = Path.Combine(Path.GetTempPath(), $"todox-lastframe-{Guid.NewGuid():N}.jpg");
        try
        {
            var ffmpeg = _configuration["VideoRender:FfmpegPath"] ?? _configuration["RenderQueue:FfmpegPath"] ?? "ffmpeg";
            await RunFfmpegAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-sseof", "-0.5", "-i", input,
                "-frames:v", "1", "-update", "1", "-q:v", "2", "-y", output], ct);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("RVIDEO_LAST_FRAME_EMPTY");
            var bytes = await File.ReadAllBytesAsync(output, ct);
            return await _media.SaveBinaryAtObjectKeyAsync(bytes, objectKey, $"scene-{scene.Id}-linked-frame.jpg", "image/jpeg",
                "video_scene_image", project.UserId, project.CustomerId, _tenant.TenantId, ct);
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
        }
    }

    private async Task<string> ResolveLocalVideoAsync(SceneVideoVersionDto video, CancellationToken ct)
    {
        if (VideoRenderMergeHandler.LocalMediaResolver.TryResolveExistingLocalFile(video.SourceFilePath, _env.ContentRootPath, _configuration, out var path))
            return path;
        if (VideoRenderMergeHandler.LocalMediaResolver.TryResolveExistingLocalFile(video.StorageKey, _env.ContentRootPath, _configuration, out path))
            return path;
        if (!string.IsNullOrWhiteSpace(video.PublicUrl) && !string.IsNullOrWhiteSpace(video.StorageKey))
        {
            var localized = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(video.PublicUrl!, video.StorageKey!,
                "video_scene_video", "video/mp4", null, null, _tenant.TenantId, ct);
            if (VideoRenderMergeHandler.LocalMediaResolver.TryResolveExistingLocalFile(localized.ObjectKey, _env.ContentRootPath, _configuration, out path))
                return path;
        }
        throw new InvalidOperationException($"RVIDEO_LINK_SOURCE_VIDEO_UNAVAILABLE videoVersionId={video.Id}");
    }

    internal static async Task RunFfmpegAsync(string ffmpeg, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("RVIDEO_FFMPEG_START_FAILED");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        _ = process.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"RVIDEO_FFMPEG_FAILED exit={process.ExitCode} {(await stderr).Trim()}");
    }
}
