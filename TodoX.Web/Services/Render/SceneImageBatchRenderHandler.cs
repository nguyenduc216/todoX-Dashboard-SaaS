using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.Render;

public sealed class SceneImageBatchInput
{
    public string CapabilityCode { get; set; } = SceneImageRenderContext.DefaultCapabilityCode;
    public long ProjectId { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public long? CharacterId { get; set; }
    public string? CharacterReferenceObjectKey { get; set; }
    public string? CharacterReferenceUrl { get; set; }
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public bool OnlyMissingOrFailed { get; set; }
    public long[]? SceneIds { get; set; }
}

public sealed class SceneImageRenderWorkItemInput
{
    public Guid ParentJobId { get; set; }
    public Guid ImageVersionId { get; set; }
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int SceneIndex { get; set; }
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public long? CharacterId { get; set; }
    public Guid? ReferenceMediaId { get; set; }
    public string? ReferenceObjectKey { get; set; }
    public string? ReferenceUrl { get; set; }
    public string CapabilityCode { get; set; } = SceneImageRenderContext.RVideoCapabilityCode;
    public string LogicalRequestId { get; set; } = string.Empty;
    public string? RequestedModel { get; set; }
    public int ModelAttemptIndex { get; set; }
}

public sealed record RVideoImageModelPolicyEntry(
    int AttemptIndex,
    string Model,
    string DisplayName,
    string Mode,
    string Resolution);

public static class RVideoImageModelPolicy
{
    public static readonly IReadOnlyList<RVideoImageModelPolicyEntry> Models =
    [
        new(0, "google_image_gen_banana_2", "Nano Banana 2", "vip", "1k"),
        new(1, "imagegen_2_0", "GPT Image 2", "low_basic", "1k"),
        new(2, "seedream_4_5", "Seedream 4.5", "vip", "2k")
    ];

    public static RVideoImageModelPolicyEntry GetInitial() => Models[0];

    public static RVideoImageModelPolicyEntry? GetByAttemptIndex(int attemptIndex)
        => Models.FirstOrDefault(x => x.AttemptIndex == attemptIndex);

    public static RVideoImageModelPolicyEntry? GetNext(int currentAttemptIndex)
        => Models.FirstOrDefault(x => x.AttemptIndex == currentAttemptIndex + 1);
}

public sealed class SceneImageBatchRenderHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_scene_images";
    public const string RoutingProviderCode = "configured_image_router";
    public const string RoutingModelCode = "scene_image_default";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VideoRenderRepository _repo;
    private readonly ISceneImageRenderService _sceneImages;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiCharacterService _characters;
    private readonly IRenderJobService _jobs;
    private readonly ILogger<SceneImageBatchRenderHandler> _logger;

    public SceneImageBatchRenderHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService sceneImages,
        ISceneMediaVersioningService versions,
        IAiCharacterService characters,
        IRenderJobService jobs,
        IConfiguration config,
        ILogger<SceneImageBatchRenderHandler> logger)
    {
        _repo = repo;
        _sceneImages = sceneImages;
        _versions = versions;
        _characters = characters;
        _jobs = jobs;
        _logger = logger;
    }

    public string JobType => JobTypeName;

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneImageBatchInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image batch job input invalid.");
        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scenes = project.Scenes.OrderBy(x => x.SceneIndex)
            .Where(x => input.SceneIds is null || input.SceneIds.Contains(x.Id))
            .Where(x => ShouldRenderScene(x, input.OnlyMissingOrFailed))
            .ToList();
        var activeSceneIds = new HashSet<long>();
        foreach (var scene in scenes)
        {
            if (await _versions.HasActiveImageVersionAsync(scene.Id, ct))
            {
                activeSceneIds.Add(scene.Id);
            }
        }
        scenes = scenes.Where(scene => !activeSceneIds.Contains(scene.Id)).ToList();
        if (scenes.Count == 0) return;

        var (referenceMediaId, referenceUrl, referenceObjectKey, characterPrompt) =
            await ResolveCharacterReferenceAsync(input, ct);

        foreach (var scene in scenes)
        {
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_QUEUED", "info",
                $"Scene {scene.SceneIndex} image queued.",
                new { jobId = job.Id, projectId = input.ProjectId, sceneId = scene.Id }, ct);
            await EnqueueSceneAsync(input, scene, referenceMediaId, referenceUrl, referenceObjectKey,
                characterPrompt, job.Id, ct);
        }

        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_BATCH_COMPLETED", "info",
            "Scene image child jobs have been queued for persisted provider processing.",
            new { jobId = job.Id, total = scenes.Count }, ct);
    }

    public static bool ShouldRenderScene(VideoProjectSceneDto scene, bool onlyMissingOrFailed)
        => !onlyMissingOrFailed
           || string.IsNullOrWhiteSpace(scene.StaticImageUrl)
           || string.Equals(scene.Status, VideoSceneStatuses.Failed, StringComparison.OrdinalIgnoreCase);

    private async Task EnqueueSceneAsync(
        SceneImageBatchInput input,
        VideoProjectSceneDto scene,
        Guid? referenceMediaId,
        string? referenceUrl,
        string? referenceObjectKey,
        string? characterPrompt,
        Guid parentJobId,
        CancellationToken ct)
    {
        _ = _sceneImages;
        var model = RVideoImageModelPolicy.GetInitial();
        var logicalRequestId = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", scene.Id, parentJobId);
        var compiledPrompt = SceneImagePromptBuilder.Build(scene, characterPrompt);
        var version = await _versions.CreateQueuedImageVersionAsync(new SceneImageVersionCreateRequest(
            input.ProjectId, scene.Id, input.UserId, input.CustomerId, parentJobId, logicalRequestId,
            scene.ImagePrompt, compiledPrompt, scene.VideoPrompt, null,
            new { scene.Id, scene.ProjectId, scene.SceneIndex, scene.Title, scene.DurationSeconds,
                scene.ScenePrompt, scene.ImagePrompt, scene.VideoPrompt },
            new { input.CharacterId, referenceMediaId, referenceUrl, characterPrompt },
            new { capability = input.CapabilityCode, aspectRatio = SceneImageRenderService.NormalizeAspectRatio(input.AspectRatio),
                outputFormat = "png", source = "scene_image_batch", model = model.Model, model.Mode,
                model.Resolution, modelAttemptIndex = model.AttemptIndex }), ct);

        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Draft, errorMessage: null,
            title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt, ct: ct);
        var child = await _jobs.EnqueueAsync(new RenderJobCreateModel
        {
            JobType = SceneImageRenderWorkItemHandler.JobTypeName,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            Input = new SceneImageRenderWorkItemInput
            {
                ParentJobId = parentJobId, ImageVersionId = version.Id, ProjectId = input.ProjectId,
                SceneId = scene.Id, SceneIndex = scene.SceneIndex, UserId = input.UserId,
                CustomerId = input.CustomerId, CreatedBy = input.CreatedBy,
                TrustedPayerContext = input.TrustedPayerContext,
                Prompt = version.CompiledImagePromptSnapshot ?? compiledPrompt,
                AspectRatio = SceneImageRenderService.NormalizeAspectRatio(input.AspectRatio),
                CharacterId = input.CharacterId, ReferenceMediaId = referenceMediaId,
                ReferenceObjectKey = referenceObjectKey, ReferenceUrl = referenceUrl,
                CapabilityCode = input.CapabilityCode, LogicalRequestId = logicalRequestId,
                RequestedModel = model.Model, ModelAttemptIndex = model.AttemptIndex
            },
            Prompt = new { projectId = input.ProjectId, sceneId = scene.Id, parentJobId },
            References = Array.Empty<object>(), LogCode = parentJobId.ToString("N"),
            MaxAttempts = 100, PointCostEstimate = 0, PointStatus = RenderPointStatuses.NotRequired
        }, ct);
        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_CHILD_JOB_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} image child job queued.",
            new { parentJobId, childJobId = child.Id, sceneId = scene.Id, imageVersionId = version.Id }, ct);
    }

    private async Task<(Guid? MediaId, string? Url, string? ObjectKey, string? CharacterPrompt)>
        ResolveCharacterReferenceAsync(SceneImageBatchInput input, CancellationToken ct)
    {
        if (input.CharacterId is not long characterId)
        {
            if (string.IsNullOrWhiteSpace(input.CharacterReferenceUrl)
                && string.IsNullOrWhiteSpace(input.CharacterReferenceObjectKey))
                return (null, null, null, null);
            var mediaId = await _sceneImages.ResolveCharacterReferenceMediaIdAsync(
                input.ProjectId, input.CharacterReferenceUrl, input.CharacterReferenceObjectKey,
                input.UserId, input.CustomerId, requireReference: true, ct: ct);
            return (mediaId, input.CharacterReferenceUrl, input.CharacterReferenceObjectKey, null);
        }

        try
        {
            var character = await _characters.GetCharacterAsync(
                new CurrentUserSession { UserId = input.UserId, CustomerId = input.CustomerId }, characterId, ct);
            if (character is null
                || (string.IsNullOrWhiteSpace(character.MasterImageUrl)
                    && string.IsNullOrWhiteSpace(character.MasterImageObjectKey)))
            {
                throw new InvalidOperationException("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE");
            }
            var mediaId = await _sceneImages.ResolveCharacterReferenceMediaIdAsync(input.ProjectId,
                character?.MasterImageUrl, character?.MasterImageObjectKey, input.UserId, input.CustomerId,
                requireReference: true, ct: ct);
            if (mediaId is null)
            {
                throw new InvalidOperationException("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE");
            }
            return (mediaId, character?.MasterImageUrl, character?.MasterImageObjectKey, character?.NormalizedPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCENE_IMAGE_CHARACTER_REFERENCE_RESOLVE_FAILED projectId={ProjectId} characterId={CharacterId}",
                input.ProjectId, characterId);
            if (ex is InvalidOperationException { Message: "RVIDEO_REFERENCE_IMAGE_UNAVAILABLE" })
            {
                throw;
            }

            throw new InvalidOperationException("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE", ex);
        }
    }
}
