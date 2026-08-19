using System.Text.Json;
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
    private readonly ILogger<SceneImageRenderWorkItemHandler> _logger;

    public SceneImageRenderWorkItemHandler(
        VideoRenderRepository repo,
        ISceneImageRenderService images,
        ISceneMediaVersioningService versions,
        IRenderJobService jobs,
        ILogger<SceneImageRenderWorkItemHandler> logger)
    {
        _repo = repo;
        _images = images;
        _versions = versions;
        _jobs = jobs;
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
        var taskId = await _versions.GetSceneImageProviderTaskIdAsync(version.Id, ct);

        try
        {
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
                CharacterReferenceMediaId = input.ReferenceMediaId,
                CharacterReferenceObjectKey = input.ReferenceObjectKey,
                CharacterReferenceUrl = input.ReferenceUrl,
                ProviderTaskId = taskId,
                CapabilityCode = input.CapabilityCode,
                ProgressCallback = (eventType, data) => _repo.AddProjectEventAsync(
                    input.ProjectId, eventType, "info", eventType,
                    new { data, jobId = job.Id, sceneId = input.SceneId, imageVersionId = input.ImageVersionId }, ct)
            }, ct);

            var pending = !outcome.Success && outcome.ProviderTaskId is not null
                && (string.Equals(outcome.Error, "79AI image task is still pending.", StringComparison.OrdinalIgnoreCase)
                    || outcome.BillingLogicalRequestId is not null);
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
                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed, errorMessage: outcome.Error,
                    title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt,
                    videoPrompt: scene.VideoPrompt, ct: ct);
                throw new RenderJobTerminalFailureException(outcome.Error ?? "Scene image render failed.");
            }

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
}
