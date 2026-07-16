using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
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
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }

    /// <summary>When true, only scenes without a successful image (or failed) are rendered.</summary>
    public bool OnlyMissingOrFailed { get; set; }
    public List<SceneImageBatchVersionInput> ImageVersions { get; set; } = new();
}

public sealed class SceneImageBatchVersionInput
{
    public long SceneId { get; set; }
    public Guid VersionId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string CompiledPrompt { get; set; } = string.Empty;
    public string? StorageKey { get; set; }
}

/// <summary>
/// Background handler that renders every scene's static image through the configured image provider
/// router. Concurrency is bounded by configuration so the browser can refresh or navigate away while
/// the batch continues server-side.
/// </summary>
public sealed class SceneImageBatchRenderHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_scene_images";
    public string JobType => JobTypeName;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VideoRenderRepository _repo;
    private readonly ISceneImageRenderService _sceneImages;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiCharacterService _characters;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneImageBatchRenderHandler> _logger;

    public SceneImageBatchRenderHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService sceneImages,
        ISceneMediaVersioningService versions,
        IAiCharacterService characters,
        IConfiguration config,
        ILogger<SceneImageBatchRenderHandler> logger)
    {
        _repo = repo;
        _sceneImages = sceneImages;
        _versions = versions;
        _characters = characters;
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
            $"Bắt đầu render {scenes.Count} ảnh tĩnh scene qua AI provider.",
            new { jobId = job.Id, sceneCount = scenes.Count, input.OnlyMissingOrFailed, input.CharacterId, capability = SceneImageRenderService.CapabilityCode }, ct);

        if (scenes.Count == 0)
        {
            return;
        }

        // Resolve the character master image once for both router URL references and legacy media references.
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
        var maxConcurrency = Math.Clamp(_config.GetValue("RenderQueue:SceneImageMaxConcurrency", 3), 1, 8);
        using var concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = scenes.Select(async scene =>
        {
            await concurrency.WaitAsync(ct);
            try
            {
                var ok = await RenderOneAsync(input, project, scene, referenceMediaId, referenceUrl, characterPrompt, job.Id, ct);
                if (!ok) Interlocked.Increment(ref failures);
            }
            finally
            {
                concurrency.Release();
            }
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
        var precreated = input.ImageVersions.FirstOrDefault(x => x.SceneId == scene.Id);
        var logicalRequestId = string.IsNullOrWhiteSpace(precreated?.LogicalRequestId)
            ? SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", scene.Id, jobId)
            : precreated!.LogicalRequestId;
        var versioningEnabled = await _versions.IsEnabledAsync(SceneMediaVersioningFlags.SceneImages, ct);
        var compiledPrompt = string.IsNullOrWhiteSpace(precreated?.CompiledPrompt)
            ? SceneImagePromptBuilder.Build(scene, characterPrompt)
            : precreated!.CompiledPrompt;
        SceneImageVersionDto? imageVersion = precreated is null
            ? null
            : new SceneImageVersionDto
            {
                Id = precreated.VersionId,
                ProjectId = input.ProjectId,
                SceneId = scene.Id,
                LogicalRequestId = logicalRequestId,
                StorageKey = precreated.StorageKey,
                CompiledImagePromptSnapshot = compiledPrompt,
                Status = "queued"
            };
        if (versioningEnabled)
        {
            imageVersion ??= await _versions.CreateQueuedImageVersionAsync(new SceneImageVersionCreateRequest(
                input.ProjectId,
                scene.Id,
                input.UserId,
                input.CustomerId,
                jobId,
                logicalRequestId,
                scene.ImagePrompt,
                compiledPrompt,
                scene.VideoPrompt,
                NegativePromptSnapshot: null,
                SceneSnapshot: new
                {
                    scene.Id,
                    scene.ProjectId,
                    scene.SceneIndex,
                    scene.Title,
                    scene.DurationSeconds,
                    scene.ScenePrompt,
                    scene.ImagePrompt,
                    scene.VideoPrompt
                },
                ReferenceSnapshot: new
                {
                    characterId = input.CharacterId,
                    referenceMediaId,
                    referenceUrl,
                    characterPrompt
                },
                RenderConfigSnapshot: new
                {
                    capability = SceneImageRenderService.CapabilityCode,
                    aspectRatio = "9:16",
                    outputFormat = "png",
                    source = "scene_image_batch"
                }), ct);
        }

        var context = new SceneImageRenderContext
        {
            ProjectId = input.ProjectId,
            SceneId = scene.Id,
            SceneIndex = scene.SceneIndex,
            Prompt = imageVersion?.CompiledImagePromptSnapshot ?? compiledPrompt,
            CharacterId = input.CharacterId,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            TrustedPayerContext = input.TrustedPayerContext,
            CreatedBy = input.CreatedBy,
            RenderJobId = jobId,
            LogicalRequestId = logicalRequestId,
            OutputObjectKey = imageVersion?.StorageKey,
            CharacterReferenceMediaId = referenceMediaId,
            CharacterReferenceUrl = referenceUrl
        };

        try
        {
            // We already hold a concurrency slot here (acquired by the caller). Announce the render start
            // so the UI shows "Đang tạo ảnh..." for exactly this scene.
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_START", "info",
                $"Scene {scene.SceneIndex} bắt đầu render qua AI provider.",
                new { jobId, projectId = input.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, capability = SceneImageRenderService.CapabilityCode, startedAt = DateTime.UtcNow }, ct);

            var startedAt = DateTime.UtcNow;
            var outcome = await _sceneImages.RenderSceneImageAsync(context, ct);

            var completedAt = DateTime.UtcNow;

            if (outcome.Success)
            {
                if (imageVersion is not null)
                {
                    await _versions.CompleteImageVersionAsync(imageVersion.Id, new SceneImageVersionCompleteRequest(
                        outcome.ImageUrl,
                        outcome.ObjectKey,
                        outcome.ProviderCode,
                        outcome.ModelName,
                        outcome.ProviderCapabilityId,
                        outcome.ProviderTaskId,
                        outcome.ResultMediaId,
                        outcome.BillingLogicalRequestId,
                        outcome.EstimatedUsd,
                        outcome.ActualUsd,
                        outcome.ChargedPoints,
                        outcome.RefundedPoints,
                        outcome.ProviderUsageJson,
                        MimeType: "image/png",
                        CostSource: outcome.CostSource), ct);
                }

                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.ImageReady,
                    imageUrl: outcome.ImageUrl, imagePath: outcome.ObjectKey, errorMessage: null,
                    title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
                await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_READY", "info",
                    $"Scene {scene.SceneIndex} image ready.",
                    new
                    {
                        jobId,
                        projectId = input.ProjectId,
                        sceneId = scene.Id,
                        sceneIndex = scene.SceneIndex,
                        characterId = input.CharacterId,
                        provider = outcome.ProviderCode,
                        model = outcome.ModelName,
                        imageUrl = outcome.ImageUrl,
                        queuedAt,
                        startedAt,
                        completedAt,
                        durationMs = (completedAt - startedAt).TotalMilliseconds
                    }, ct);
                return true;
            }

            if (imageVersion is not null)
            {
                await _versions.FailImageVersionAsync(imageVersion.Id, outcome.QuotaError ? "quota" : "provider_error", outcome.Error, ct);
            }

            await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
                errorMessage: outcome.Error, title: scene.Title, scenePrompt: scene.ScenePrompt,
                imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_RENDER_FAILED", "error",
                $"Render ảnh scene {scene.SceneIndex} thất bại.",
                new
                {
                    jobId,
                    projectId = input.ProjectId,
                    sceneId = scene.Id,
                    sceneIndex = scene.SceneIndex,
                    characterId = input.CharacterId,
                    provider = outcome.ProviderCode,
                    model = outcome.ModelName,
                    error = outcome.Error,
                    quota = outcome.QuotaError,
                    queuedAt,
                    completedAt
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
            if (imageVersion is not null)
            {
                await _versions.FailImageVersionAsync(imageVersion.Id, ex.GetType().Name, ex.Message, ct);
            }

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
