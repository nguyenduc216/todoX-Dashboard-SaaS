using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.Render;

public sealed class SceneImageBatchInput
{
    public string CapabilityCode { get; set; } = SceneImageRenderContext.RVideoCapabilityCode;
    public string ReferenceSource { get; set; } = "NONE";
    public bool UseSharedReferenceImage { get; set; }
    public VideoSceneImageInputMode ImageInputMode { get; set; } = VideoSceneImageInputMode.SceneSource;
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
    public bool ParentJobBilled { get; set; }
    public PointBillingIntent BillingIntent { get; set; } = PointBillingIntent.InitialRender;
    public Guid? BillingReferenceId { get; set; }
    public bool SkipCustomerCharge
    {
        get => ParentJobBilled;
        set => ParentJobBilled = value;
    }
}

public sealed class SceneImageRenderWorkItemInput
{
    public bool SkipCustomerCharge { get; set; }
    public PointBillingIntent BillingIntent { get; set; } = PointBillingIntent.InitialRender;
    public Guid? BillingReferenceId { get; set; }
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
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IPointPricingService _pointPricing;
    private readonly WalletService _wallets;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<SceneImageBatchRenderHandler> _logger;

    public SceneImageBatchRenderHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService sceneImages,
        ISceneMediaVersioningService versions,
        IAiCharacterService characters,
        IRenderJobService jobs,
        RVideoJobSettingsRepository settings,
        IPointPricingService pointPricing,
        WalletService wallets,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IConfiguration config,
        ILogger<SceneImageBatchRenderHandler> logger)
    {
        _repo = repo;
        _sceneImages = sceneImages;
        _versions = versions;
        _characters = characters;
        _jobs = jobs;
        _settings = settings;
        _pointPricing = pointPricing;
        _wallets = wallets;
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public string JobType => JobTypeName;

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneImageBatchInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image batch job input invalid.");
        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        if (input.UseSharedReferenceImage || input.ImageInputMode == VideoSceneImageInputMode.SharedBaseImage)
        {
            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_BATCH_SHARED_BASE_SKIPPED", "info",
                "SharedBaseImage uses the shared reference image directly; scene image generation was skipped.",
                new { jobId = job.Id, projectId = input.ProjectId, input.ImageInputMode, input.UseSharedReferenceImage }, ct);
            return;
        }

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

        if (input.BillingIntent == PointBillingIntent.InitialRender
            && !project.Events.Any(x => x.EventType == "RVIDEO_PARENT_BILLED"))
        {
            await ChargeInitialRenderAsync(job, input, project, scenes, ct);
        }

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

    private async Task ChargeInitialRenderAsync(
        RenderJobDto job,
        SceneImageBatchInput input,
        VideoProjectDto project,
        IReadOnlyList<VideoProjectSceneDto> scenes,
        CancellationToken ct)
    {
        var settings = await _settings.GetAsync(project.Id, ct);
        var imageCount = 0;
        foreach (var scene in scenes)
        {
            var selected = await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
            if (!RVideoEffectiveSceneImageSourceResolver.RequiresAiGeneration(scene, settings, selected, project))
            {
                continue;
            }
            imageCount++;
        }

        var videoScenes = scenes
            .Select(scene => new PreRenderVideoScene(scene.Id, scene.DurationSeconds))
            .ToArray();
        var plan = new PreRenderUsagePlan(
            await ResolvePointServiceIdAsync(project.CoreJobId, ct),
            imageCount,
            string.Equals(RVideoImageModelPolicy.GetInitial().Mode, "vip", StringComparison.OrdinalIgnoreCase)
                ? ServiceSellPriceQualityTiers.Premium
                : ServiceSellPriceQualityTiers.Standard,
            videoScenes,
            ServiceSellPriceQualityTiers.Standard,
            settings is not null
                ? scenes.Count(scene => RVideoRules.RequiresExternalVoice(scene, settings)
                    && !string.IsNullOrWhiteSpace(RVideoRules.ResolveSceneVoiceText(scene)))
                : 0,
            ServiceSellPriceQualityTiers.Standard,
            settings is not null && RVideoRules.ResolveVoiceMode(settings) == RVideoVoiceModes.Library)
            .Validate();
        var estimate = await _pointPricing.EstimateAsync(plan.ToPricingRequest(), ct);
        var customerId = input.CustomerId ?? job.CustomerId;
        var available = customerId is Guid cid ? await _wallets.GetBalanceAsync(cid) : 0m;
        var charge = await _wallets.ChargeAsync(
            customerId, input.UserId, estimate.TotalPoints, 1, "rvideo_initial_render",
            "todox", "point_pricing", "rvideo", "point", job.Id, "rvideo_parent_job");
        if (!charge.Ok)
        {
            await _jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed,
                errorCode: "insufficient_points", errorMessage: charge.Error, ct: ct);
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_PARENT_BILLING_FAILED", "error",
                "Initial rVideo render was blocked before provider submission.",
                new { jobId = job.Id, available_points_at_check = available, required_points = estimate.TotalPoints }, ct);
            throw new RenderJobTerminalFailureException(charge.Error ?? "Insufficient points.");
        }

        await _jobs.UpsertSnapshotAsync(job.Id,
            new
            {
                projectId = project.Id,
                serviceId = plan.ServiceId,
                imageCount = plan.ImageCount,
                imageQuality = plan.ImageQuality,
                videoSeconds = plan.VideoSeconds,
                videoQuality = plan.VideoQuality,
                voiceCount = plan.VoiceCount,
                voiceQuality = plan.VoiceQuality,
                voiceEnabled = plan.VoiceEnabled,
                imagePoints = estimate.Image.Points,
                videoPoints = estimate.Video.Points,
                voicePoints = estimate.Voice.Points,
                totalPoints = estimate.TotalPoints,
                available_points_at_check = available,
                balance_after_charge = charge.BalanceAfter
            },
            project.Scenes.Select(scene => new { scene.Id, scene.SceneIndex, scene.DurationSeconds }).ToArray(), ct);
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_PARENT_BILLED", "info",
            "Initial IMAGE + VIDEO + VOICE points were charged before provider submission.",
            new { jobId = job.Id, totalPoints = estimate.TotalPoints, balance_after_charge = charge.BalanceAfter }, ct);
        input.ParentJobBilled = true;
        input.SkipCustomerCharge = true;
        input.BillingReferenceId = job.Id;
    }

    private async Task<Guid?> ResolvePointServiceIdAsync(Guid? coreJobId, CancellationToken ct)
    {
        if (coreJobId is not Guid jobId) return null;
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            "SELECT service_id FROM render.render_jobs WHERE id=@jobId AND tenant_id=@tenant LIMIT 1;",
            new { jobId, tenant = _tenant.TenantId });
    }

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
            new
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
            new { input.CharacterId, referenceMediaId, referenceUrl, referenceObjectKey, referenceSource = input.ReferenceSource, characterPrompt },
            new
            {
                capability = input.CapabilityCode,
                aspectRatio = SceneImageRenderService.NormalizeAspectRatio(input.AspectRatio),
                outputFormat = "png",
                source = "scene_image_batch",
                model = model.Model,
                model.Mode,
                model.Resolution,
                modelAttemptIndex = model.AttemptIndex
            }), ct);

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
                ParentJobId = parentJobId,
                ImageVersionId = version.Id,
                ProjectId = input.ProjectId,
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                CreatedBy = input.CreatedBy,
                TrustedPayerContext = input.TrustedPayerContext,
                Prompt = version.CompiledImagePromptSnapshot ?? compiledPrompt,
                AspectRatio = SceneImageRenderService.NormalizeAspectRatio(input.AspectRatio),
                CharacterId = input.CharacterId,
                ReferenceMediaId = referenceMediaId,
                ReferenceObjectKey = referenceObjectKey,
                ReferenceUrl = referenceUrl,
                CapabilityCode = input.CapabilityCode,
                LogicalRequestId = logicalRequestId,
                RequestedModel = model.Model,
                ModelAttemptIndex = model.AttemptIndex,
                SkipCustomerCharge = input.SkipCustomerCharge,
                BillingIntent = input.BillingIntent,
                BillingReferenceId = input.BillingReferenceId
            },
            Prompt = new { projectId = input.ProjectId, sceneId = scene.Id, parentJobId },
            References = Array.Empty<object>(),
            LogCode = parentJobId.ToString("N"),
            MaxAttempts = 100,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        }, ct);
        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_CHILD_JOB_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} image child job queued.",
            new { parentJobId, childJobId = child.Id, sceneId = scene.Id, imageVersionId = version.Id }, ct);
    }

    private async Task<(Guid? MediaId, string? Url, string? ObjectKey, string? CharacterPrompt)>
        ResolveCharacterReferenceAsync(SceneImageBatchInput input, CancellationToken ct)
    {
        if (string.Equals(input.ReferenceSource, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, null, null);
        }

        if (input.CharacterId is not long characterId)
        {
            if (string.IsNullOrWhiteSpace(input.CharacterReferenceUrl)
                && string.IsNullOrWhiteSpace(input.CharacterReferenceObjectKey))
            {
                throw new InvalidOperationException("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE");
            }
            var mediaId = await _sceneImages.ResolveCharacterReferenceMediaIdAsync(
                input.ProjectId, input.CharacterReferenceUrl, input.CharacterReferenceObjectKey,
                input.UserId, input.CustomerId, requireReference: true, ct: ct);
            if (mediaId is null)
            {
                throw new InvalidOperationException("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE");
            }
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
