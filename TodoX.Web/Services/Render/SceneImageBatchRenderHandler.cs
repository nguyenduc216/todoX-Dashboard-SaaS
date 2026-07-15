using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.Render;

/// <summary>Input payload for the <see cref="SceneImageBatchRenderHandler"/> (serialised into the job's input_json).</summary>
public sealed class SceneImageBatchInput
{
    public long ProjectId { get; set; }
    public long? CharacterId { get; set; }
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>When true, only scenes without a successful image (or failed) are rendered.</summary>
    public bool OnlyMissingOrFailed { get; set; }
}

/// <summary>
/// Background handler that renders every scene's static image for a video project on Google Cloud
/// (Vertex) via <see cref="ISceneImageRenderService"/>. Fans scenes out with a bounded degree of
/// parallelism (max 3, enforced globally by <see cref="GoogleVertexRateLimiter"/>) so at most three
/// scenes hit Vertex at once; the rest wait. HTTP 429 / quota errors are retried with backoff by the
/// limiter and do NOT immediately mark a scene failed. This runs entirely server-side, so the browser
/// can refresh or navigate away while the batch continues.
/// </summary>
public sealed class SceneImageBatchRenderHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_scene_images";
    public string JobType => JobTypeName;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VideoRenderRepository _repo;
    private readonly ISceneImageRenderService _sceneImages;
    private readonly IAiCharacterService _characters;
    private readonly GoogleVertexRateLimiter _rateLimiter;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneImageBatchRenderHandler> _logger;

    public SceneImageBatchRenderHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService sceneImages,
        IAiCharacterService characters,
        GoogleVertexRateLimiter rateLimiter,
        IConfiguration config,
        ILogger<SceneImageBatchRenderHandler> logger)
    {
        _repo = repo;
        _sceneImages = sceneImages;
        _characters = characters;
        _rateLimiter = rateLimiter;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneImageBatchInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image batch job input invalid.");
        if (input.ProjectId <= 0)
        {
            throw new InvalidOperationException("Thiếu projectId trong job input.");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy project video.");

        var scenes = project.Scenes
            .OrderBy(x => x.SceneIndex)
            .Where(scene => ShouldRenderScene(scene, input.OnlyMissingOrFailed))
            .ToList();

        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_BATCH_STARTED", "info",
            $"Bắt đầu render {scenes.Count} ảnh tĩnh scene trên Google Cloud.",
            new { jobId = job.Id, sceneCount = scenes.Count, input.OnlyMissingOrFailed, input.CharacterId }, ct);

        if (scenes.Count == 0)
        {
            return;
        }

        // Resolve the character master image once and turn it into a Vertex reference media id.
        var (referenceMediaId, referenceUrl, characterPrompt) = await ResolveCharacterReferenceAsync(input, ct);

        // Emit a QUEUED event for every scene up-front so the UI can render "Đang chờ" per scene
        // (and restore that state after a refresh) before any slot opens.
        foreach (var scene in scenes)
        {
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_QUEUED", "info",
                $"Scene {scene.SceneIndex} đang chờ render ảnh.",
                new { jobId = job.Id, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, characterId = input.CharacterId }, ct);
        }

        var failures = 0;

        var tasks = scenes.Select(async scene =>
        {
            var ok = await RenderOneAsync(input, project, scene, referenceMediaId, referenceUrl, characterPrompt, job.Id, ct);
            if (!ok) Interlocked.Increment(ref failures);
        }).ToList();

        await Task.WhenAll(tasks);

        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_BATCH_COMPLETED",
            failures == 0 ? "info" : "warning",
            $"Hoàn tất render ảnh tĩnh scene. Thành công {scenes.Count - failures}/{scenes.Count}.",
            new { jobId = job.Id, total = scenes.Count, failed = failures }, ct);
    }

    private async Task<(Guid? MediaId, string? Url, string? CharacterPrompt)> ResolveCharacterReferenceAsync(SceneImageBatchInput input, CancellationToken ct)
    {
        if (input.CharacterId is not long characterId)
        {
            return (null, null, null);
        }

        try
        {
            var user = new CurrentUserSession { UserId = input.UserId, CustomerId = input.CustomerId };
            var character = await _characters.GetCharacterAsync(user, characterId, ct);
            var url = character?.MasterImageUrl;
            var mediaId = await _sceneImages.ResolveCharacterReferenceMediaIdAsync(input.ProjectId, url, input.UserId, input.CustomerId, ct);
            return (mediaId, url, character?.NormalizedPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCENE_IMAGE_BATCH_CHARACTER_RESOLVE_FAILED projectId={ProjectId} characterId={CharacterId}", input.ProjectId, characterId);
            return (null, null, null);
        }
    }

    public static bool ShouldRenderScene(VideoProjectSceneDto scene, bool onlyMissingOrFailed)
        => !onlyMissingOrFailed
           || string.IsNullOrWhiteSpace(scene.StaticImageUrl)
           || string.Equals(scene.Status, VideoSceneStatuses.Failed, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> RenderOneAsync(
        SceneImageBatchInput input,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        Guid? referenceMediaId,
        string? referenceUrl,
        string? characterPrompt,
        Guid jobId,
        CancellationToken ct)
    {
        var queuedAt = DateTime.UtcNow;
        var context = new SceneImageRenderContext
        {
            ProjectId = input.ProjectId,
            SceneId = scene.Id,
            SceneIndex = scene.SceneIndex,
            Prompt = SceneImagePromptBuilder.Build(scene, characterPrompt),
            CharacterId = input.CharacterId,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            CreatedBy = input.CreatedBy,
            CharacterReferenceMediaId = referenceMediaId,
            CharacterReferenceUrl = referenceUrl
        };

        try
        {
            // We already hold a concurrency slot here (acquired by the caller). Announce the render start
            // so the UI shows "Đang tạo ảnh..." for exactly this scene.
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_START", "info",
                $"Scene {scene.SceneIndex} bắt đầu render trên Google Cloud.",
                new { jobId, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, attempt = 1, provider = "todox_image", startedAt = DateTime.UtcNow }, ct);

            var startedAt = DateTime.UtcNow;
            var outcome = await _rateLimiter.ExecuteAsync(
                renderAsync: async attempt =>
                {
                    if (attempt == 1)
                    {
                        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_START", "info",
                            $"Scene {scene.SceneIndex} bat dau render tren Google Cloud.",
                            new { jobId, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, attempt, provider = "todox_image", startedAt = DateTime.UtcNow }, ct);
                    }

                    return await _sceneImages.RenderSceneImageWithVertexAsync(context, attempt, ct);
                },
                isQuotaError: r => !r.Success && r.QuotaError,
                onQuotaWait: async (attempt, wait) =>
                {
                    // 429 → the scene is retrying, NOT failed. Emit QUOTA_WAIT then RETRY_START so the UI
                    // shows "Đang chờ quota..." and can restore it after a refresh.
                    await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_QUOTA_WAIT", "warning",
                        $"Scene {scene.SceneIndex} chờ quota Google Cloud {wait.TotalSeconds:0}s (lần {attempt}).",
                        new { jobId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, attempt, waitSeconds = wait.TotalSeconds, provider = "todox_image", model = "vertex_scene_image" }, ct);
                    await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RETRY_SCHEDULED", "info",
                        $"Scene {scene.SceneIndex} thử lại render (lần {attempt + 1}).",
                        new { jobId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, attempt = attempt + 1, provider = "todox_image", timestamp = DateTime.UtcNow }, ct);
                },
                onRetryStart: async attempt =>
                {
                    await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RETRY_START", "info",
                        $"Scene {scene.SceneIndex} thu lai render (lan {attempt}).",
                        new { jobId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, attempt, provider = "todox_image", timestamp = DateTime.UtcNow }, ct);
                },
                context: $"project={input.ProjectId} scene={scene.SceneIndex}",
                ct: ct);

            var completedAt = DateTime.UtcNow;

            if (outcome.Success)
            {
                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.ImageReady,
                    imageUrl: outcome.ImageUrl, imagePath: outcome.ObjectKey, errorMessage: null,
                    title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
                await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_READY", "info",
                    $"Scene {scene.SceneIndex} image ready.",
                    new
                    {
                        jobId, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex,
                        characterId = input.CharacterId, provider = outcome.ProviderCode, model = outcome.ModelName,
                        imageUrl = outcome.ImageUrl, queuedAt, startedAt, completedAt,
                        durationMs = (completedAt - startedAt).TotalMilliseconds
                    }, ct);
                return true;
            }

            await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
                errorMessage: outcome.Error, title: scene.Title, scenePrompt: scene.ScenePrompt,
                imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_FAILED", "error",
                $"Render ảnh scene {scene.SceneIndex} thất bại.",
                new
                {
                    jobId, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex,
                    characterId = input.CharacterId, provider = outcome.ProviderCode, model = outcome.ModelName,
                    error = outcome.Error, quota = outcome.QuotaError, queuedAt, completedAt
                }, ct);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown/cancellation is not a scene failure — do not mark the scene Failed. Let the worker
            // reclaim the job later; propagate so Task.WhenAll and the worker see the cancellation.
            _logger.LogInformation("SCENE_IMAGE_BATCH_SCENE_CANCELLED projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex}",
                input.ProjectId, scene.Id, scene.SceneIndex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SCENE_IMAGE_BATCH_SCENE_EXCEPTION projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex}",
                input.ProjectId, scene.Id, scene.SceneIndex);
            await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
                errorMessage: ex.Message, title: scene.Title, scenePrompt: scene.ScenePrompt,
                imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_FAILED", "error",
                $"Render ảnh scene {scene.SceneIndex} lỗi.",
                new { jobId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, error = ex.Message }, ct);
            return false;
        }
    }
}
