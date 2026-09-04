using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.Render;

public sealed class SceneImageRenderWorkItemHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_scene_image";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VideoRenderRepository _repo;
    private readonly ISceneImageRenderService _images;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IRenderJobService _jobs;
    private readonly IRVideoSceneVideoAutoChainService _autoChain;
    private readonly IRVideoJobService _rvideoJobs;
    private readonly IPointPricingService _pointPricing;
    private readonly WalletService _wallets;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<SceneImageRenderWorkItemHandler> _logger;

    public SceneImageRenderWorkItemHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService images,
        ISceneMediaVersioningService versions,
        IRenderJobService jobs,
        IRVideoSceneVideoAutoChainService autoChain,
        IRVideoJobService rvideoJobs,
        IPointPricingService pointPricing,
        WalletService wallets,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ILogger<SceneImageRenderWorkItemHandler> logger)
    {
        _repo = repo;
        _images = images;
        _versions = versions;
        _jobs = jobs;
        _autoChain = autoChain;
        _rvideoJobs = rvideoJobs;
        _pointPricing = pointPricing;
        _wallets = wallets;
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public string JobType => JobTypeName;

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneImageRenderWorkItemInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image worker input invalid.");
        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scene = project.Scenes.FirstOrDefault(x => x.Id == input.SceneId)
            ?? throw new InvalidOperationException("Video scene not found.");
        var version = (await _versions.ListImageVersionsAsync(input.SceneId, 0, 100, ct))
            .FirstOrDefault(x => x.Id == input.ImageVersionId)
            ?? throw new InvalidOperationException("Scene image version not found.");
        await _rvideoJobs.SyncLifecycleAsync(input.ProjectId, RVideoStages.Image, VideoProjectStatuses.Rendering, ct);
        var taskId = await _versions.GetSceneImageProviderTaskIdAsync(version.Id, ct);
        if (!string.IsNullOrWhiteSpace(input.RequestedModel))
        {
            await _versions.MarkSceneImageVersionRequestedAsync(version.Id, input.RequestedModel, ct);
        }

        try
        {
            // Provider submission must never consume customer points. RVIDEO charges only after
            // a valid image result has been received and persisted by the provider path.
            input.SkipCustomerCharge = true;

            var outcome = await _images.RenderSceneImageAsync(new SceneImageRenderContext
            {
                ProjectId = input.ProjectId,
                SceneId = input.SceneId,
                SceneIndex = input.SceneIndex,
                Prompt = input.Prompt,
                AspectRatio = input.AspectRatio,
                CharacterId = input.CharacterId,
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                TrustedPayerContext = input.TrustedPayerContext,
                CreatedBy = input.CreatedBy,
                RenderJobId = job.Id,
                LogicalRequestId = input.LogicalRequestId,
                OutputObjectKey = version.StorageKey,
                SkipCustomerCharge = input.SkipCustomerCharge,
                CharacterReferenceMediaId = input.ReferenceMediaId,
                CharacterReferenceObjectKey = input.ReferenceObjectKey,
                CharacterReferenceUrl = input.ReferenceUrl,
                ProviderTaskId = taskId,
                RequestedModel = input.RequestedModel,
                CapabilityCode = input.CapabilityCode,
                ProgressCallback = (eventType, data) => _repo.AddProjectEventAsync(
                    input.ProjectId, eventType, "info", eventType,
                    new { data, jobId = job.Id, sceneId = input.SceneId, imageVersionId = input.ImageVersionId }, ct)
            }, ct);

            var pending = outcome.ExecutionState == AiProviderExecutionState.Pending
                && outcome.ProviderTaskId is not null;
            if (pending)
            {
                await _versions.MarkSceneImageVersionSubmittedAsync(version.Id, outcome.ProviderCode,
                    outcome.ModelName, outcome.ProviderCapabilityId, outcome.ProviderTaskId!, ct);
                await _versions.MarkSceneImagePendingReconciliationAsync(version.Id, "provider_pending", outcome.Error, ct);
                await _jobs.ScheduleRetryAsync(job.Id, TimeSpan.FromSeconds(10), "SCENE_IMAGE_POLL_SCHEDULED",
                    "79AI image task remains pending; the same provider task will be polled later.", ct);
                throw new RenderJobDeferredException("79AI image task remains pending; retry scheduled.");
            }

            if (!outcome.Success)
            {
                await _versions.FailImageVersionAsync(version.Id, "provider_error", outcome.Error, ct);
                var nextModel = RVideoImageModelPolicy.GetNext(input.ModelAttemptIndex);
                if (nextModel is not null)
                {
                    await EnqueueFallbackAsync(job, input, scene, nextModel, ct);
                    return;
                }

                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed, errorMessage: outcome.Error,
                    title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt,
                    videoPrompt: scene.VideoPrompt, ct: ct);
                throw new RenderJobTerminalFailureException(outcome.Error ?? "Scene image render failed.");
            }

            var chargedPoints = 0m;
            var model = RVideoImageModelPolicy.GetByAttemptIndex(input.ModelAttemptIndex)
                ?? RVideoImageModelPolicy.GetInitial();
            var quality = string.Equals(model.Mode, "vip", StringComparison.OrdinalIgnoreCase)
                ? ServiceSellPriceQualityTiers.Premium
                : ServiceSellPriceQualityTiers.Standard;
            var serviceId = await ResolvePointServiceIdAsync(input.ProjectId, ct);
            var rate = await _pointPricing.ResolveRateAsync(
                serviceId, PointPricingResourceTypes.Image, quality, ct);
            if (input.BillingIntent != PointBillingIntent.SystemRetry)
            {
                var referenceId = PointBillingReference.ForOperation(
                    input.ParentJobId,
                    "rvideo_scene_image",
                    version.Id.ToString("N"),
                    input.BillingIntent,
                    input.BillingReferenceId);
                var charge = await _wallets.ChargeAsync(
                    input.CustomerId, input.UserId, rate.Rate, 1,
                    input.BillingIntent == PointBillingIntent.UserRerender
                        ? "rvideo_user_rerender_image"
                        : "rvideo_initial_render_image",
                    outcome.ProviderCode ?? "todox",
                    outcome.ModelName ?? model.Model,
                    "rvideo",
                    "image",
                    referenceId,
                    "rvideo_scene_image_success");
                if (!charge.Ok)
                {
                    await _repo.AddProjectEventAsync(input.ProjectId, "RVIDEO_IMAGE_BILLING_ANOMALY", "error",
                        "Image result was accepted but the success charge could not be completed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, versionId = version.Id, requiredPoints = rate.Rate, charge.Error }, ct);
                    throw new RenderJobTerminalFailureException(charge.Error ?? "Insufficient points after provider success.");
                }

                chargedPoints = charge.Charged == 0 ? rate.Rate : charge.Charged;
            }
            else
            {
                await _wallets.LogUsageOnlyAsync(input.CustomerId, input.UserId,
                    outcome.ProviderCode ?? "todox", outcome.ModelName ?? "image",
                    "rvideo_system_retry_image", 1, rate.Rate, "rvideo", "image",
                    version.Id, "rvideo_system_retry", "success");
            }

            outcome = outcome with { ChargedPoints = chargedPoints };
            var completed = await _versions.TryCompleteImageVersionAsync(version.Id, new SceneImageVersionCompleteRequest(
                outcome.ImageUrl, outcome.ObjectKey, outcome.ProviderCode, outcome.ModelName,
                outcome.ProviderCapabilityId, outcome.ProviderTaskId, outcome.ResultMediaId,
                outcome.BillingLogicalRequestId, outcome.EstimatedUsd, outcome.ActualUsd,
                outcome.ChargedPoints, outcome.RefundedPoints, outcome.ProviderUsageJson,
                "image/png", outcome.CostSource), ct);
            if (!completed)
            {
                _logger.LogWarning("SCENE_IMAGE_COMPLETE_STALE jobId={JobId} sceneId={SceneId} imageVersionId={ImageVersionId} providerTaskId={ProviderTaskId}",
                    job.Id, input.SceneId, input.ImageVersionId, outcome.ProviderTaskId);
                return;
            }

            await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_READY", "info",
                $"Scene {input.SceneIndex} image ready.",
                new { jobId = job.Id, sceneId = input.SceneId, imageVersionId = input.ImageVersionId,
                    provider = outcome.ProviderCode, model = outcome.ModelName }, ct);

            await _autoChain.TryEnqueueSceneVideoAsync(input.ProjectId, input.SceneId, "SCENE_IMAGE_READY", ct);
        }
        catch (Ai79TaskPollException ex)
        {
            await _versions.MarkSceneImagePendingReconciliationAsync(version.Id, "SCENE_IMAGE_POLL_TRANSIENT",
                ex.Message, CancellationToken.None);
            await _jobs.ScheduleRetryAsync(job.Id, TimeSpan.FromSeconds(30), "SCENE_IMAGE_POLL_TRANSIENT",
                "Temporary 79AI poll failure; the same task ID will be retried.", CancellationToken.None);
            throw new RenderJobDeferredException("Temporary 79AI poll failure; retry scheduled.");
        }
    }

    private async Task<Guid?> ResolvePointServiceIdAsync(long projectId, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project?.CoreJobId is not Guid coreJobId)
        {
            return null;
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT service_id
              FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenant
             LIMIT 1;
            """,
            new { jobId = coreJobId, tenant = _tenant.TenantId });
    }

    private async Task EnqueueFallbackAsync(
        RenderJobDto job,
        SceneImageRenderWorkItemInput input,
        VideoProjectSceneDto scene,
        RVideoImageModelPolicyEntry nextModel,
        CancellationToken ct)
    {
        var logicalRequestId = $"{input.LogicalRequestId}-fallback-{nextModel.AttemptIndex}";
        var version = await _versions.CreateQueuedImageVersionAsync(new SceneImageVersionCreateRequest(
            input.ProjectId, input.SceneId, input.UserId, input.CustomerId, input.ParentJobId, logicalRequestId,
            scene.ImagePrompt, input.Prompt, scene.VideoPrompt, null,
            new { scene.Id, scene.ProjectId, scene.SceneIndex, scene.Title, scene.DurationSeconds,
                scene.ScenePrompt, scene.ImagePrompt, scene.VideoPrompt },
            new { input.CharacterId, referenceMediaId = input.ReferenceMediaId,
                referenceUrl = input.ReferenceUrl, referenceObjectKey = input.ReferenceObjectKey },
            new { capability = input.CapabilityCode, aspectRatio = input.AspectRatio,
                outputFormat = "png", source = "scene_image_model_fallback",
                model = nextModel.Model, nextModel.Mode, nextModel.Resolution,
                modelAttemptIndex = nextModel.AttemptIndex }), ct);

        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Draft, errorMessage: null,
            title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt, ct: ct);

        var child = await _jobs.EnqueueAsync(new RenderJobCreateModel
        {
            JobType = JobTypeName,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            Input = new SceneImageRenderWorkItemInput
            {
                ParentJobId = input.ParentJobId,
                ImageVersionId = version.Id,
                ProjectId = input.ProjectId,
                SceneId = input.SceneId,
                SceneIndex = input.SceneIndex,
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                CreatedBy = input.CreatedBy,
                TrustedPayerContext = input.TrustedPayerContext,
                Prompt = input.Prompt,
                AspectRatio = input.AspectRatio,
                CharacterId = input.CharacterId,
                ReferenceMediaId = input.ReferenceMediaId,
                ReferenceObjectKey = input.ReferenceObjectKey,
                ReferenceUrl = input.ReferenceUrl,
                CapabilityCode = input.CapabilityCode,
                LogicalRequestId = logicalRequestId,
                RequestedModel = nextModel.Model,
                ModelAttemptIndex = nextModel.AttemptIndex,
                SkipCustomerCharge = input.SkipCustomerCharge,
                BillingIntent = input.BillingIntent,
                BillingReferenceId = input.BillingReferenceId
            },
            Prompt = new { projectId = input.ProjectId, sceneId = input.SceneId, fallbackFromJobId = job.Id },
            References = Array.Empty<object>(),
            LogCode = input.ParentJobId.ToString("N"),
            ProviderCode = SceneImageBatchRenderHandler.RoutingProviderCode,
            ModelCode = SceneImageBatchRenderHandler.RoutingModelCode,
            MaxAttempts = 100,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        }, ct);

        await _repo.AddProjectEventAsync(input.ProjectId, "SCENE_IMAGE_MODEL_FALLBACK_QUEUED", "warning",
            $"Scene {input.SceneIndex} image fallback queued.",
            new { jobId = job.Id, childJobId = child.Id, sceneId = input.SceneId,
                failedImageVersionId = input.ImageVersionId, imageVersionId = version.Id,
                model = nextModel.Model, modelAttemptIndex = nextModel.AttemptIndex }, ct);
    }
}
