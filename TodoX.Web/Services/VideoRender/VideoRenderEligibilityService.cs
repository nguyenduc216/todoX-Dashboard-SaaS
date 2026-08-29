using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public enum VideoRenderEligibilityStatus
{
    Eligible,
    AlreadyCompleted,
    AlreadyActive,
    MissingImage,
    InvalidPrompt
}

public sealed record VideoRenderEligibilityResult(
    long SceneId,
    int SceneIndex,
    VideoRenderEligibilityStatus Status,
    string ErrorCode,
    string Message,
    Guid? SelectedImageVersionId = null,
    Guid? SelectedVideoVersionId = null,
    Guid? ActiveVideoVersionId = null,
    Guid? ActiveRenderJobId = null);

public sealed record VideoRenderEligibilityReport(
    long ProjectId,
    IReadOnlyList<VideoRenderEligibilityResult> Results)
{
    public long[] EligibleSceneIds => Results
        .Where(x => x.Status == VideoRenderEligibilityStatus.Eligible)
        .Select(x => x.SceneId)
        .ToArray();
}

public interface IVideoRenderEligibilityService
{
    Task<VideoRenderEligibilityReport> GetVideoRenderEligibilityAsync(
        long projectId,
        IReadOnlyCollection<long> requestedSceneIds,
        CancellationToken ct = default);
}

public sealed class VideoRenderEligibilityService : IVideoRenderEligibilityService
{
    private static readonly HashSet<string> ActiveVideoStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "submitted",
        "pending_reconciliation",
        "video_rendering",
        "rendering"
    };

    private readonly VideoRenderRepository _repo;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IVideoPromptValidator _validator;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public VideoRenderEligibilityService(
        VideoRenderRepository repo,
        RVideoJobSettingsRepository settings,
        ISceneMediaVersioningService versions,
        IVideoPromptValidator validator,
        TodoXConnectionFactory factory,
        TenantContext tenant)
    {
        _repo = repo;
        _settings = settings;
        _versions = versions;
        _validator = validator;
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<VideoRenderEligibilityReport> GetVideoRenderEligibilityAsync(
        long projectId,
        IReadOnlyCollection<long> requestedSceneIds,
        CancellationToken ct = default)
    {
        if (requestedSceneIds.Count == 0)
        {
            throw new InvalidOperationException("RVIDEO_VIDEO_SCENE_IDS_REQUIRED");
        }

        var project = await _repo.GetProjectAsync(projectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");

        var requested = requestedSceneIds.ToHashSet();
        var eligible = new List<VideoRenderEligibilityResult>();
        var activeJobIds = await LoadActiveRenderJobIdsAsync(projectId, requested, ct);
        var settings = await _settings.GetAsync(projectId, ct);
        var usesSharedReferenceImage = settings?.UseReferenceImageForAllScenes ?? false;
        RVideoSceneImageReferenceSelection? sharedReference = null;
        if (usesSharedReferenceImage && settings is not null)
        {
            try
            {
                sharedReference = RVideoSceneImageReferenceSelection.Resolve(settings);
            }
            catch (InvalidOperationException)
            {
                sharedReference = null;
            }
        }

        foreach (var scene in project.Scenes.Where(x => requested.Contains(x.Id)).OrderBy(x => x.SceneIndex))
        {
            var imageVersion = usesSharedReferenceImage
                ? ResolveSharedReferenceImageVersion(sharedReference)
                : await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
            if (imageVersion is null || imageVersion.Id == Guid.Empty || string.IsNullOrWhiteSpace(imageVersion.PublicUrl))
            {
                eligible.Add(new VideoRenderEligibilityResult(
                    scene.Id,
                    scene.SceneIndex,
                    VideoRenderEligibilityStatus.MissingImage,
                    "RVIDEO_VIDEO_IMAGE_REQUIRED",
                    $"Scene {scene.SceneIndex:00}: cần ảnh nguồn đã chọn trước khi render video."));
                continue;
            }

            var prompt = scene.VideoPrompt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                eligible.Add(new VideoRenderEligibilityResult(
                    scene.Id,
                    scene.SceneIndex,
                    VideoRenderEligibilityStatus.InvalidPrompt,
                    "RVIDEO_VIDEO_PROMPT_REQUIRED",
                    $"Scene {scene.SceneIndex:00}: prompt video không được để trống.",
                    imageVersion.Id));
                continue;
            }

            var promptValidation = _validator.Validate(scene.VideoPrompt, RVideoVideoModelPolicy.GetInitial().Model, null, scene.SceneIndex);
            if (!promptValidation.IsValid)
            {
                eligible.Add(new VideoRenderEligibilityResult(
                    scene.Id,
                    scene.SceneIndex,
                    VideoRenderEligibilityStatus.InvalidPrompt,
                    promptValidation.ErrorCode ?? "RVIDEO_VIDEO_PROMPT_INVALID",
                    promptValidation.Message ?? $"Scene {scene.SceneIndex:00}: prompt video không hợp lệ.",
                    imageVersion.Id));
                continue;
            }

            var versions = await _versions.ListSceneVideoVersionsAsync(scene.Id, 0, 100, ct);
            var selectedCompleted = versions.FirstOrDefault(x => x.IsSelected && x.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
            if (selectedCompleted is not null)
            {
                eligible.Add(new VideoRenderEligibilityResult(
                    scene.Id,
                    scene.SceneIndex,
                    VideoRenderEligibilityStatus.AlreadyCompleted,
                    "RVIDEO_VIDEO_ALREADY_COMPLETED",
                    $"Scene {scene.SceneIndex:00} đã có video hoàn tất.",
                    imageVersion.Id,
                    selectedCompleted.Id));
                continue;
            }

            var activeVersion = versions.FirstOrDefault(x => x.Status is not null && ActiveVideoStatuses.Contains(x.Status));
            if (activeVersion is not null || activeJobIds.Contains(scene.Id))
            {
                eligible.Add(new VideoRenderEligibilityResult(
                    scene.Id,
                    scene.SceneIndex,
                    VideoRenderEligibilityStatus.AlreadyActive,
                    "RVIDEO_VIDEO_ALREADY_ACTIVE",
                    $"Scene {scene.SceneIndex:00} đang render video.",
                    imageVersion.Id,
                    activeVersion?.Id,
                    activeVersion?.Id,
                    null));
                continue;
            }

            eligible.Add(new VideoRenderEligibilityResult(
                scene.Id,
                scene.SceneIndex,
                VideoRenderEligibilityStatus.Eligible,
                string.Empty,
                string.Empty,
                imageVersion.Id));
        }

        return new VideoRenderEligibilityReport(projectId, eligible);
    }

    private static SceneImageVersionDto? ResolveSharedReferenceImageVersion(RVideoSceneImageReferenceSelection? sharedReference)
    {
        if (sharedReference is null || !sharedReference.ReferenceRequested)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sharedReference.Url) && string.IsNullOrWhiteSpace(sharedReference.ObjectKey))
        {
            return null;
        }

        return new SceneImageVersionDto
        {
            Id = Guid.Empty,
            PublicUrl = sharedReference.Url,
            StorageKey = sharedReference.ObjectKey,
            Status = "completed",
            IsSelected = true
        };
    }

    private async Task<HashSet<long>> LoadActiveRenderJobIdsAsync(long projectId, IReadOnlyCollection<long> requestedSceneIds, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<long>(
            """
            SELECT DISTINCT (input_json->>'sceneId')::bigint AS scene_id
              FROM render.render_jobs
             WHERE tenant_id=@tenant
               AND job_type='render_scene_video'
               AND status IN ('queued','preparing','rendering','post_processing','pending_reconciliation')
               AND (input_json->>'projectId')::bigint = @projectId
               AND (input_json->>'sceneId') IS NOT NULL
               AND (input_json->>'sceneId') <> ''
               AND (input_json->>'sceneId')::bigint = ANY(@sceneIds);
            """,
            new { tenant = _tenant.TenantId, projectId, sceneIds = requestedSceneIds.ToArray() });
        return rows.ToHashSet();
    }
}
