using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
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
}

public sealed class SceneVideoRenderHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_video_job";
    public const string RoutingProviderCode = "configured_video_router";
    public const string RoutingModelCode = "scene_video_default";
    public string JobType => JobTypeName;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PendingStatuses = { "QUEUED", "PENDING", "SUBMITTED", "PROCESSING", "RUNNING" };

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _providerRepo;
    private readonly IAiImageBillingService _billing;
    private readonly IYEScaleTaskClient _tasks;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneVideoRenderHandler> _logger;

    public SceneVideoRenderHandler(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IAiProviderService providers,
        AiProviderRepository providerRepo,
        IAiImageBillingService billing,
        IYEScaleTaskClient tasks,
        IMediaFileService media,
        TenantContext tenant,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<SceneVideoRenderHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _providers = providers;
        _providerRepo = providerRepo;
        _billing = billing;
        _tasks = tasks;
        _media = media;
        _tenant = tenant;
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneVideoRenderInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene video job input invalid.");
        if (input.ProjectId <= 0)
        {
            throw new InvalidOperationException("Thiếu projectId trong job render video.");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy project video.");
        var option = await _providers.ResolveProviderForCapabilityAsync(AiProviderCatalog.ImageToVideo, providerCapabilityId: null, fromUser: false, ct);
        if (!string.Equals(option.ProviderCode, "yescale_task_video", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Provider image-to-video hiện tại chưa được runtime TodoX hỗ trợ.");
        }

        var detail = await _providerRepo.GetProviderAsync(option.ProviderId, ct)
            ?? throw new InvalidOperationException("Không đọc được provider image-to-video.");
        var capability = detail.Capabilities.FirstOrDefault(x => x.Id == option.ProviderCapabilityId)
            ?? throw new InvalidOperationException("Không đọc được capability image-to-video.");

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
            $"Bắt đầu render {scenes.Count} scene video qua provider đã cấu hình.",
            new
            {
                jobId = job.Id,
                sceneCount = scenes.Count,
                option.ProviderCode,
                option.ModelName,
                input.AspectRatio,
                input.Resolution
            }, ct);

        var failures = 0;
        foreach (var scene in scenes)
        {
            var ok = await RenderOneAsync(project, scene, input, option, detail, capability, job.Id, ct);
            if (!ok)
            {
                failures++;
            }
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_BATCH_COMPLETED", failures == 0 ? "info" : "warning",
            $"Hoàn tất render scene video. Thành công {scenes.Count - failures}/{scenes.Count}.",
            new { jobId = job.Id, total = scenes.Count, failed = failures }, ct);
    }

    private async Task<bool> RenderOneAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneVideoRenderInput input,
        ProviderOptionDto option,
        AiProviderDetailDto providerDetail,
        AiProviderCapabilityDto capability,
        Guid jobId,
        CancellationToken ct)
    {
        var draftImageUrl = scene.StaticImageUrl;
        var selectedImageVersion = await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
        if (!string.IsNullOrWhiteSpace(selectedImageVersion?.PublicUrl))
        {
            draftImageUrl = selectedImageVersion.PublicUrl;
        }

        if (string.IsNullOrWhiteSpace(draftImageUrl))
        {
            await FailSceneAsync(project.Id, scene, null, "missing_image", "Scene chưa có ảnh được chọn để tạo video.", ct);
            return false;
        }

        if (string.IsNullOrWhiteSpace(scene.VideoPrompt))
        {
            await FailSceneAsync(project.Id, scene, null, "missing_prompt", "Scene chưa có prompt video.", ct);
            return false;
        }

        var logicalRequestId = $"render_job_scene_video-job-{jobId:N}-scene-{scene.Id}";
        var version = await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
            project.Id,
            scene.Id,
            selectedImageVersion?.Id,
            input.UserId,
            input.CustomerId,
            jobId,
            logicalRequestId,
            scene.ImagePrompt,
            scene.VideoPrompt,
            SceneSnapshot: new
            {
                scene.Id,
                scene.ProjectId,
                scene.SceneIndex,
                scene.Title,
                scene.DurationSeconds,
                scene.ScenePrompt,
                scene.ImagePrompt,
                scene.VideoPrompt,
                selectedImageVersionId = selectedImageVersion?.Id,
                selectedImageUrl = draftImageUrl
            },
            RenderConfigSnapshot: new
            {
                capability = AiProviderCatalog.ImageToVideo,
                providerId = option.ProviderId,
                providerCapabilityId = option.ProviderCapabilityId,
                option.ProviderCode,
                option.ModelName,
                aspectRatio = input.AspectRatio,
                resolution = input.Resolution,
                duration = scene.DurationSeconds
            }), ct);

        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoQueued,
            errorMessage: null, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_RENDER_START", "info",
            $"Scene {scene.SceneIndex} bắt đầu render video.",
            new
            {
                jobId,
                sceneId = scene.Id,
                scene.SceneIndex,
                option.ProviderCode,
                option.ModelName,
                input.AspectRatio,
                input.Resolution,
                duration = scene.DurationSeconds,
                sourceImageVersionId = selectedImageVersion?.Id
            }, ct);

        var billingCost = _billing.BuildConfiguredCost(option.UnitCostPoints, 1);
        var tariffSnapshot = JsonSerializer.Serialize(new
        {
            model = option.ModelName,
            providerCapabilityId = option.ProviderCapabilityId,
            unitCostPoints = option.UnitCostPoints,
            providerEstimatedCostUsd = billingCost.ProviderEstimatedCostUsd,
            costSource = "configured_tariff",
            configJson = capability.ConfigJson,
            capturedAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

        var reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = logicalRequestId,
            RenderJobId = jobId.ToString("N"),
            CustomerId = input.CustomerId,
            UserId = input.UserId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            FeatureCode = "render_job_scene_video",
            RequestedModel = option.ModelName,
            Cost = billingCost,
            TrustedPayerContext = input.TrustedPayerContext,
            TariffSnapshotJson = tariffSnapshot,
            Metadata = new
            {
                projectId = project.Id,
                sceneId = scene.Id,
                sceneIndex = scene.SceneIndex,
                duration = scene.DurationSeconds,
                input.Resolution,
                input.AspectRatio
            },
            CreatedBy = input.CreatedBy
        }, ct);

        if (!reservation.Ok || !reservation.ShouldSubmitProvider)
        {
            await FailSceneAsync(project.Id, scene, version, reservation.Status, reservation.ErrorMessage ?? "Không thể đặt chỗ billing cho render video.", ct);
            return false;
        }

        try
        {
            var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
                option.ModelName ?? string.Empty,
                scene.VideoPrompt ?? string.Empty,
                draftImageUrl,
                input.AspectRatio,
                input.Resolution,
                scene.DurationSeconds,
                providerDetail.ConfigJson,
                capability.ConfigJson);

            var submit = await _tasks.SubmitAsync(payload, ct);
            var taskId = submit.TaskId ?? string.Empty;
            await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoRendering,
                errorMessage: null, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                $"Scene {scene.SceneIndex} đã submit sang YEScale.",
                new { jobId, sceneId = scene.Id, scene.SceneIndex, option.ProviderCode, option.ModelName, taskId }, ct);

            YEScaleTaskStatusResponse terminal = null!;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var status = await _tasks.GetStatusAsync(taskId, ct);
                if (status.IsSuccess || status.IsFailure)
                {
                    terminal = status;
                    break;
                }

                await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_POLL", "info",
                    $"Scene {scene.SceneIndex} đang chờ YEScale xử lý.",
                    new { jobId, sceneId = scene.Id, scene.SceneIndex, taskId, status = status.Status }, ct);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            var responseJson = JsonSerializer.Serialize(terminal, JsonOptions);
            if (!terminal.IsSuccess)
            {
                await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = logicalRequestId,
                    Success = false,
                    ActualModel = option.ModelName,
                    ProviderTaskId = taskId,
                    ProviderUsageJson = responseJson,
                    TariffSnapshotJson = tariffSnapshot,
                    ErrorMessage = ExtractFailureMessage(terminal)
                }, ct);
                await FailSceneAsync(project.Id, scene, version, "provider_failure", ExtractFailureMessage(terminal), ct);
                return false;
            }

            var outputUrl = ExtractVideoUrl(terminal)
                ?? throw new InvalidOperationException("YEScale SUCCESS nhưng không tìm thấy URL video đầu ra.");
            await _tenant.EnsureLoadedAsync(ct);
            var saved = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(
                outputUrl,
                version.StorageKey ?? SceneMediaStorageKeys.SceneVideoOutput(_tenant.TenantId, project.Id, scene.Id, version.Id),
                "video_scene_video",
                "video/mp4",
                input.UserId,
                input.CustomerId,
                _tenant.TenantId,
                ct);
            var physicalPath = ResolvePhysicalPath(saved.ObjectKey);

            await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = logicalRequestId,
                Success = true,
                ActualModel = option.ModelName,
                ProviderTaskId = taskId,
                ProviderUsageJson = responseJson,
                TariffSnapshotJson = tariffSnapshot
            }, ct);

            await _versions.CompleteSceneVideoVersionAsync(version.Id, new SceneVideoVersionCompleteRequest(
                saved.PublicUrl ?? saved.FileUrl,
                physicalPath,
                PosterUrl: selectedImageVersion?.PublicUrl ?? scene.StaticImageUrl,
                DurationSeconds: scene.DurationSeconds,
                MimeType: "video/mp4",
                ProviderCode: option.ProviderCode,
                ModelName: option.ModelName,
                ProviderCapabilityId: option.ProviderCapabilityId,
                ProviderTaskId: taskId,
                BillingLogicalRequestId: logicalRequestId,
                EstimatedUsd: billingCost.ProviderEstimatedCostUsd,
                ActualUsd: null,
                ChargedPoints: reservation.ChargedPoints,
                RefundedPoints: 0,
                CostSource: billingCost.ProviderCostSource,
                AspectRatio: input.AspectRatio), ct);

            await _providers.LogUsageAsync(new AiProviderUsageLog
            {
                CustomerId = input.CustomerId.HasValue ? ToBigIntCustomerId(input.CustomerId) : null,
                ProviderId = option.ProviderId,
                ProviderCapabilityId = option.ProviderCapabilityId,
                ProviderCode = option.ProviderCode,
                CapabilityCode = option.CapabilityCode,
                FeatureCode = "scene_video_render",
                ModelName = option.ModelName,
                RequestId = logicalRequestId,
                JobId = jobId.ToString("N"),
                Quantity = 1,
                UnitType = option.UnitType,
                UnitCostPoints = option.UnitCostPoints,
                TotalPoints = reservation.ChargedPoints,
                ProviderRawCost = null,
                Status = "success",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    projectId = project.Id,
                    sceneId = scene.Id,
                    sceneIndex = scene.SceneIndex,
                    taskId,
                    duration = scene.DurationSeconds,
                    input.Resolution,
                    input.AspectRatio,
                    sourceImageVersionId = selectedImageVersion?.Id
                }, JsonOptions),
                CreatedBy = input.CreatedBy
            }, ct);

            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_READY", "info",
                $"Scene {scene.SceneIndex} đã render video thành công.",
                new
                {
                    jobId,
                    sceneId = scene.Id,
                    scene.SceneIndex,
                    option.ProviderCode,
                    option.ModelName,
                    taskId,
                    videoUrl = saved.PublicUrl ?? saved.FileUrl
                }, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
            {
                LogicalRequestId = logicalRequestId,
                ActualModel = option.ModelName,
                ErrorMessage = "Scene video render cancelled."
            }, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = logicalRequestId,
                Success = false,
                ActualModel = option.ModelName,
                ErrorMessage = ex.Message
            }, CancellationToken.None);
            await FailSceneAsync(project.Id, scene, version, ex.GetType().Name, ex.Message, ct);
            return false;
        }
    }

    private async Task FailSceneAsync(long projectId, VideoProjectSceneDto scene, SceneVideoVersionDto? version, string? errorCode, string errorMessage, CancellationToken ct)
    {
        if (version is not null)
        {
            await _versions.FailSceneVideoVersionAsync(version.Id, errorCode, errorMessage, ct);
        }

        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
            errorMessage: errorMessage, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_RENDER_FAILED", "error",
            $"Render video scene {scene.SceneIndex} thất bại.",
            new { sceneId = scene.Id, scene.SceneIndex, errorCode, error = errorMessage }, ct);
    }

    private static string? ExtractVideoUrl(YEScaleTaskStatusResponse response)
    {
        if (response.Extra is null)
        {
            return null;
        }

        foreach (var element in response.Extra.Values)
        {
            var url = ExtractVideoUrl(element);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? ExtractVideoUrl(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var key in new[] { "video_url", "videoUrl", "url", "output_url", "outputUrl" })
                {
                    if (element.TryGetProperty(key, out var url) && url.ValueKind == JsonValueKind.String && Uri.TryCreate(url.GetString(), UriKind.Absolute, out _))
                    {
                        return url.GetString();
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = ExtractVideoUrl(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = ExtractVideoUrl(item);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;
        }

        return null;
    }

    private static string ExtractFailureMessage(YEScaleTaskStatusResponse response)
    {
        if (response.Error is JsonElement error)
        {
            if (error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }

            if (error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    return message.GetString()!;
                }

                if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(code.GetString()))
                {
                    return code.GetString()!;
                }
            }
        }

        return "Task video YEScale thất bại.";
    }

    private static long? ToBigIntCustomerId(Guid? id)
    {
        if (id is null) return null;
        var bytes = id.Value.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }

    private string ResolvePhysicalPath(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return string.Empty;
        }

        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        return Path.Combine(_env.ContentRootPath, uploadRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }
}
