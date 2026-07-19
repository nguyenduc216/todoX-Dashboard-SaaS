using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneVideoRenderInput
{
    public long ProjectId { get; set; }
    public long[] SceneIds { get; set; } = Array.Empty<long>();
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720P";
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public string? ProviderConfigJson { get; set; }
    public string? CapabilityConfigJson { get; set; }
}

public sealed class SceneVideoRenderHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string JobTypeName = RenderJobTypes.RenderVideoBatch;
    public const string RoutingProviderCode = "configured_video_router";
    public const string RoutingModelCode = "scene_video_default";

    private readonly VideoRenderRepository _repo;
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _providerRepo;
    private readonly IRenderJobService _jobs;
    private readonly IYEScaleVideoPricingResolver _pricing;
    private readonly IVideoPromptValidator _promptValidator;
    private readonly ILogger<SceneVideoRenderHandler> _logger;

    public string JobType => JobTypeName;

    public SceneVideoRenderHandler(
        VideoRenderRepository repo,
        IAiProviderService providers,
        AiProviderRepository providerRepo,
        IRenderJobService jobs,
        IYEScaleVideoPricingResolver pricing,
        IVideoPromptValidator promptValidator,
        ILogger<SceneVideoRenderHandler> logger)
    {
        _repo = repo;
        _providers = providers;
        _providerRepo = providerRepo;
        _jobs = jobs;
        _pricing = pricing;
        _promptValidator = promptValidator;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneVideoRenderInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene video batch job input invalid.");
        if (input.ProjectId <= 0)
        {
            throw new InvalidOperationException("Missing projectId in render video batch job.");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");

        var option = await _providers.ResolveProviderForCapabilityAsync(AiProviderCatalog.ImageToVideo, providerCapabilityId: null, fromUser: false, ct);
        if (!string.Equals(option.ProviderCode, "yescale_task_video", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The current image-to-video provider is not supported by the TodoX runtime yet.");
        }
        if (!string.Equals(option.CapabilityCode, AiProviderCatalog.ImageToVideo, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(option.ModelName, "omni-flash", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The current image-to-video runtime must resolve YEScale omni-flash.");
        }

        var detail = await _providerRepo.GetProviderAsync(option.ProviderId, ct)
            ?? throw new InvalidOperationException("Configured image-to-video provider could not be loaded.");
        var capability = detail.Capabilities.FirstOrDefault(x => x.Id == option.ProviderCapabilityId)
            ?? throw new InvalidOperationException("Configured image-to-video capability could not be loaded.");
        input.ProviderConfigJson = detail.ConfigJson;
        input.CapabilityConfigJson = capability.ConfigJson;

        var targetSceneIds = input.SceneIds.Length == 0
            ? project.Scenes.Select(x => x.Id).ToHashSet()
            : input.SceneIds.ToHashSet();
        var scenes = project.Scenes
            .Where(scene => targetSceneIds.Contains(scene.Id))
            .OrderBy(scene => scene.SceneIndex)
            .ToList();
        if (scenes.Count == 0)
        {
            return;
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_BATCH_STARTED", "info",
            $"Batch render video started for {scenes.Count} scenes.",
            new
            {
                batchJobId = job.Id,
                sceneCount = scenes.Count,
                option.ProviderCode,
                option.ModelName,
                input.AspectRatio,
                input.Resolution
            }, ct);

        var enqueued = 0;
        var validationFailed = new List<int>();
        foreach (var scene in scenes)
        {
            if (await EnqueueSceneChildJobAsync(project, scene, input, option, capability, job, ct))
            {
                enqueued++;
            }
            else
            {
                validationFailed.Add(scene.SceneIndex);
            }
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_BATCH_COMPLETED", "info",
            $"Batch job enqueued {enqueued} child scene-video jobs and skipped {validationFailed.Count} invalid scenes.",
            new
            {
                batchJobId = job.Id,
                totalRequested = scenes.Count,
                enqueued,
                validationFailed = validationFailed.Count,
                validationFailedSceneIndexes = validationFailed,
                provider = option.ProviderCode,
                model = option.ModelName,
                capability = option.CapabilityCode
            }, ct);
    }

    private async Task<bool> EnqueueSceneChildJobAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneVideoRenderInput input,
        ProviderOptionDto option,
        AiProviderCapabilityDto capability,
        RenderJobDto parentJob,
        CancellationToken ct)
    {
        var selectedImage = await _repo.GetSelectedImageProjectionAsync(scene.Id, ct);
        var validation = _promptValidator.Validate(scene.VideoPrompt, option.ModelName, capability.ConfigJson, scene.SceneIndex);
        if (!validation.IsValid)
        {
            await MarkSceneValidationFailedAsync(project.Id, scene, validation, ct);
            return false;
        }

        var sourceImageUrl = selectedImage?.PublicUrl;
        if (string.IsNullOrWhiteSpace(sourceImageUrl))
        {
            var imageValidation = new VideoPromptValidationResult(
                false,
                validation.ModelName,
                validation.TrimmedPrompt,
                validation.ActualCharacterCount,
                validation.MaxCharacterCount,
                "scene_source_image_required",
                $"Scene {scene.SceneIndex:00}: cần ảnh nguồn đã chọn trước khi render video.");
            await MarkSceneValidationFailedAsync(project.Id, scene, imageValidation, ct);
            return false;
        }
        var resolvedPrice = _pricing.Resolve(
            option,
            capability,
            input.AspectRatio,
            input.Resolution,
            scene.DurationSeconds,
            true);

        await _repo.UpdateSceneAsync(
            scene.Id,
            VideoSceneStatuses.VideoQueued,
            errorMessage: null,
            title: scene.Title,
            scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt,
            ct: ct);

        var childInput = new SceneVideoRenderWorkItemInput
        {
            ParentJobId = parentJob.Id,
            ProjectId = project.Id,
            SceneId = scene.Id,
            SceneIndex = scene.SceneIndex,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            CreatedBy = input.CreatedBy,
            TrustedPayerContext = input.TrustedPayerContext,
            SelectedSourceImageVersionId = selectedImage?.Id,
            SourceImageUrl = sourceImageUrl,
            SourceImageObjectKey = selectedImage?.StorageKey,
            ImagePrompt = scene.ImagePrompt,
            VideoPrompt = validation.TrimmedPrompt,
            Voice = null,
            VoiceInstruction = null,
            ProviderId = option.ProviderId,
            ProviderCode = option.ProviderCode,
            ProviderConfigJson = input.ProviderConfigJson,
            ProviderCapabilityId = option.ProviderCapabilityId,
            CapabilityCode = option.CapabilityCode,
            CapabilityConfigJson = input.CapabilityConfigJson,
            ModelName = option.ModelName,
            MaxPromptCharacters = validation.MaxCharacterCount,
            AspectRatio = input.AspectRatio,
            Resolution = input.Resolution,
            DurationSeconds = scene.DurationSeconds,
            EstimatedUsd = resolvedPrice.ProviderEstimatedCostUsd,
            EstimatedPoints = resolvedPrice.ChargedPoints,
            PricingMode = resolvedPrice.Mode,
            PricingRuleKey = resolvedPrice.RuleKey,
            TariffSnapshotJson = resolvedPrice.TariffSnapshotJson,
            CostSource = resolvedPrice.CostSource,
            LogicalRequestId = BuildLogicalRequestId(parentJob.Id, scene.Id),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var childJob = await _jobs.EnqueueAsync(new RenderJobCreateModel
        {
            JobType = RenderJobTypes.RenderSceneVideo,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            Input = childInput,
            Prompt = new { projectId = project.Id, sceneId = scene.Id, parentJobId = parentJob.Id },
            References = Array.Empty<object>(),
            LogCode = parentJob.LogCode,
            ProviderCode = option.ProviderCode,
            ModelCode = option.ModelName,
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.Pending
        }, ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_CHILD_JOB_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} was enqueued as an independent scene-video child job.",
            new
            {
                batchJobId = parentJob.Id,
                childJobId = childJob.Id,
                sceneId = scene.Id,
                scene.SceneIndex,
                input.AspectRatio,
                input.Resolution,
                option.ProviderCode,
                option.ModelName,
                sourceImageVersionId = selectedImage?.Id
            }, ct);

        return true;
    }

    private async Task MarkSceneValidationFailedAsync(
        long projectId,
        VideoProjectSceneDto scene,
        VideoPromptValidationResult validation,
        CancellationToken ct)
    {
        await _repo.UpdateSceneAsync(
            scene.Id,
            VideoSceneStatuses.Failed,
            errorMessage: validation.Message,
            title: scene.Title,
            scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt,
            ct: ct);

        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_PROMPT_VALIDATION_FAILED", "warning",
            validation.Message ?? $"Scene {scene.SceneIndex:00}: prompt video không hợp lệ.",
            new
            {
                sceneId = scene.Id,
                scene.SceneIndex,
                model = validation.ModelName,
                actualCharacters = validation.ActualCharacterCount,
                maxCharacters = validation.MaxCharacterCount,
                errorCode = validation.ErrorCode
            }, ct);
    }

    public static string BuildLogicalRequestId(Guid parentJobId, long sceneId)
        => $"render_job_scene_video-job-{parentJobId:N}-scene-{sceneId}";
}
