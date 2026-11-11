using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneAudioAutoChainService
{
    Task<bool> TryEnqueueSceneAudioAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default);

    /// <summary>
    /// User action "Tạo lại giọng": a new Vbee audio attempt for the scene's current voice text (a new audio version),
    /// even when a completed one exists. The finalizer then muxes it into the scene video and the final video is re-merged.
    /// </summary>
    Task<RVideoSceneLinkResult> RegenerateSceneAudioAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class RVideoSceneAudioAutoChainService : IRVideoSceneAudioAutoChainService
{
    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRenderJobService _jobs;
    private readonly IAiStudioCatalogService _catalog;
    private readonly IVbeeRuntimeConfigProvider _runtimeConfig;
    private readonly IPointPricingService _pointPricing;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<RVideoSceneAudioAutoChainService> _logger;

    public RVideoSceneAudioAutoChainService(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        IRenderJobService jobs,
        IAiStudioCatalogService catalog,
        IVbeeRuntimeConfigProvider runtimeConfig,
        IPointPricingService pointPricing,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ILogger<RVideoSceneAudioAutoChainService> logger)
    {
        _repo = repo;
        _versions = versions;
        _settings = settings;
        _jobs = jobs;
        _catalog = catalog;
        _runtimeConfig = runtimeConfig;
        _pointPricing = pointPricing;
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public Task<bool> TryEnqueueSceneAudioAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default)
        => EnqueueSceneAudioAsync(projectId, sceneId, triggerSource, regenerate: false, ct);

    public const string TriggerUserRegenerate = "RVIDEO_V2_USER_REGENERATE_VOICE";

    public async Task<RVideoSceneLinkResult> RegenerateSceneAudioAsync(long projectId, long sceneId, CurrentUserSession user, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(projectId, user, ct);
        var scene = project?.Scenes.FirstOrDefault(x => x.Id == sceneId);
        if (project is null || scene is null) return new(false, "Không tìm thấy scene hoặc bạn không có quyền.");
        var settings = await _settings.GetAsync(projectId, ct);
        if (!string.Equals(RVideoRules.ResolveVoiceMode(settings), RVideoVoiceModes.Library, StringComparison.OrdinalIgnoreCase))
            return new(false, "Job không dùng giọng từ thư viện. Với AI Native Voice, giọng nằm trong video: hãy tạo lại video của scene.");
        if (string.IsNullOrWhiteSpace(RVideoRules.ResolveSceneVoiceText(scene)))
            return new(false, "Scene chưa có lời thoại để tạo giọng.");
        var selectedVideo = await _versions.GetSelectedVideoVersionAsync(sceneId, ct);
        if (selectedVideo is null || !string.Equals(selectedVideo.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return new(false, "Scene chưa có video hoàn tất. Giọng được tạo sau khi video xong.");
        if (await _versions.HasActiveAudioVersionAsync(sceneId, ct))
            return new(false, "Scene đang tạo giọng. Đợi lượt hiện tại xong rồi thử lại.");
        var started = await EnqueueSceneAudioAsync(projectId, sceneId, TriggerUserRegenerate, regenerate: true, ct);
        return started
            ? new(true, $"Đang tạo lại giọng cho scene {scene.SceneIndex:00}. Xong sẽ tự ghép vào video và ghép lại video cuối.")
            : new(false, "Chưa tạo được giọng (xem nhật ký job). Kiểm tra giọng đọc đã chọn trong Cài đặt nâng cao.");
    }

    private async Task<bool> EnqueueSceneAudioAsync(long projectId, long sceneId, string triggerSource, bool regenerate, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            _logger.LogWarning("RVIDEO_AUDIO_AUTO_CHAIN_PROJECT_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var scene = project.Scenes.FirstOrDefault(x => x.Id == sceneId);
        if (scene is null)
        {
            _logger.LogWarning("RVIDEO_AUDIO_AUTO_CHAIN_SCENE_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var settings = await _settings.GetAsync(projectId, ct);
        if (!RVideoRules.RequiresExternalVoice(scene, settings))
        {
            return false;
        }

        var metadata = ScenePromptMetadata.FromScene(scene);
        var voiceText = RVideoRules.ResolveSceneVoiceText(scene);
        var voiceInstruction = RVideoRules.ResolveSceneVoiceInstruction(scene);
        if (string.IsNullOrWhiteSpace(voiceText))
        {
            return false;
        }

        var selectedVideo = await _versions.GetSelectedVideoVersionAsync(sceneId, ct);
        if (selectedVideo is null || !string.Equals(selectedVideo.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED", "info",
                $"Scene {scene.SceneIndex} voice auto enqueue skipped because the selected scene video is not completed.",
                new { projectId, sceneId, scene.SceneIndex, triggerSource, reason = "selected_video_not_completed" }, ct);
            return false;
        }

        var selected = await _versions.GetSelectedAudioVersionAsync(sceneId, ct);
        if (!regenerate && selected is not null && string.Equals(selected.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A regeneration is a new attempt with its own audio version (the per-scene key would reuse the old one).
        // The key follows the next version number, so a double click or a retry stays one attempt (one charge).
        var logicalRequestId = BuildLogicalRequestKey(projectId, sceneId);
        if (regenerate)
        {
            var latest = (await _versions.ListSceneAudioVersionsAsync(sceneId, 0, 1, ct)).FirstOrDefault();
            logicalRequestId = $"{logicalRequestId}:v{(latest?.VersionNumber ?? 0) + 1}";
        }
        var existing = await _versions.GetSceneAudioVersionByLogicalRequestIdAsync(logicalRequestId, ct);
        if (existing is null && await _versions.HasActiveAudioVersionAsync(sceneId, ct))
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED", "info",
                $"Scene {scene.SceneIndex} voice auto enqueue skipped because another audio attempt is already active.",
                new { projectId, sceneId, scene.SceneIndex, triggerSource, logicalRequestId }, ct);
            return false;
        }

        var voice = await ResolveVoiceAsync(settings, RVideoSceneVoice.SceneVoiceCode(metadata), ct);
        if (voice is null)
        {
            await RecordEnqueueFailureAsync(projectId, scene, settings, triggerSource,
                "voice_catalog_unavailable", "The configured active voice could not be resolved.", ct);
            return false;
        }

        string voiceCode;
        try
        {
            voiceCode = ResolveProviderVoiceCode(voice);
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "RVIDEO_VBEE_PROVIDER_VOICE_ID_MISSING", StringComparison.Ordinal))
        {
            await RecordEnqueueFailureAsync(projectId, scene, settings, triggerSource,
                "provider_voice_id_missing", ex.Message, ct);
            return false;
        }
        var ttsRate = ResolveTtsRate(metadata, settings);
        ValidateTtsRate(voice, ttsRate);
        var options = await _runtimeConfig.GetAsync(ct);
        var pointServiceId = await ResolvePointServiceIdAsync(project.CoreJobId, ct);
        var pointEstimate = await _pointPricing.EstimateAsync(new PointPricingEstimateRequest(
            pointServiceId,
            0,
            ServiceSellPriceQualityTiers.Standard,
            0,
            ServiceSellPriceQualityTiers.Standard,
            1,
            ServiceSellPriceQualityTiers.Standard,
            true), ct);
        var customerPointRate = pointEstimate.Voice.Rate;
        var billingOperationId = RVideoParentBillingState.ResolveBillingOperationId(project, selectedVideo.RenderJobId ?? Guid.Empty);
        var parentJobBilled = RVideoParentBillingState.HasCurrentOperationParentVoiceCharge(
            project.Events,
            billingOperationId);

        var version = existing ?? await _versions.CreateQueuedSceneAudioVersionAsync(new SceneAudioVersionCreateRequest(
            ProjectId: project.Id,
            SceneId: scene.Id,
            UserId: project.UserId,
            CustomerId: project.CustomerId,
            RenderJobId: null,
            LogicalRequestId: logicalRequestId,
            VoiceCatalogCode: voice.Code,
            VoiceCodeSnapshot: voiceCode,
            VoiceTextSnapshot: voiceText,
            VoiceSnapshotJson: JsonSerializer.Serialize(voice, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            NarrationTextSnapshot: voiceText,
            VoiceInstructionSnapshot: voiceInstruction,
            TtsRate: ttsRate,
            DurationSeconds: scene.DurationSeconds,
            SceneSnapshot: new
            {
                scene.Id,
                scene.ProjectId,
                scene.SceneIndex,
                scene.Title,
                scene.DurationSeconds,
                scene.ScenePrompt,
                voiceText,
                voiceInstruction,
                metadata.TtsRate
            },
            RenderConfigSnapshot: new
            {
                projectId,
                sceneId,
                source = triggerSource,
                stage = "audio",
                autoChain = true,
                voiceCatalogCode = settings?.VoiceCatalogCode,
                voiceCode,
                defaultTtsRate = settings?.DefaultTtsRate,
                customerPointRate,
                customerPointQuality = ServiceSellPriceQualityTiers.Standard,
                vbeeDefaults = new
                {
                    options.DefaultSampleRate,
                    options.DefaultBitrate,
                    options.DefaultSpeedRate
                }
            }), ct);

        var model = new RenderJobCreateModel
        {
            JobType = RenderJobTypes.RenderSceneAudio,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            Input = new SceneAudioRenderWorkItemInput
            {
                ParentJobId = existing?.RenderJobId,
                ProjectId = project.Id,
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                AudioVersionId = version.Id,
                UserId = project.UserId,
                CustomerId = project.CustomerId,
                LogicalRequestId = logicalRequestId,
                VoiceCatalogCode = voice.Code,
                VoiceCode = voiceCode,
                VoiceName = voice.Name,
                NarrationText = voiceText,
                VoiceInstruction = voiceInstruction,
                TtsRate = ttsRate,
                DefaultTtsRate = settings?.DefaultTtsRate,
                CustomerPointRate = customerPointRate,
                CustomerPointQuality = ServiceSellPriceQualityTiers.Standard,
                SampleRate = options.ResolveSampleRate(voiceCode),
                Bitrate = options.DefaultBitrate,
                SpeedRate = options.DefaultSpeedRate,
                CallbackUrl = null,
                AppId = options.AppId,
                VoiceSnapshot = voice,
                SceneSnapshot = new { scene.Id, scene.ProjectId, scene.SceneIndex, scene.Title, scene.DurationSeconds, scene.ScenePrompt },
                RenderConfigSnapshot = new
                {
                    projectId,
                    sceneId,
                    triggerSource,
                    voiceCatalogCode = settings?.VoiceCatalogCode,
                    voiceCode,
                    logicalRequestId,
                    customerPointRate,
                    customerPointQuality = ServiceSellPriceQualityTiers.Standard,
                    billingIntent = PointBillingIntent.InitialRender,
                    billingOperationId = billingOperationId
                }
            },
            Prompt = new
            {
                projectId,
                sceneId,
                source = triggerSource,
                stage = "audio",
                autoChain = true
            },
            References = Array.Empty<object>(),
            LogCode = logicalRequestId,
            ProviderCode = "vbee",
            ModelCode = voiceCode,
            MaxAttempts = 100,
            PointCostEstimate = parentJobBilled ? 0 : pointEstimate.TotalPoints,
            PointStatus = parentJobBilled
                ? RenderPointStatuses.NotRequired
                : pointEstimate.TotalPoints > 0 ? RenderPointStatuses.Pending : RenderPointStatuses.NotRequired
        };

        var existingJob = await ResolveReusableRenderJobAsync(version.RenderJobId, logicalRequestId, ct);

        RenderJobDto job;
        try
        {
            job = existingJob ?? (await _jobs.EnqueueForLogCodeIfNoneActiveAsync(model, logicalRequestId, ct)).Job;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await RecordEnqueueFailureAsync(projectId, scene, settings, triggerSource,
                "render_job_enqueue_failed", ex.Message, ct);
            return false;
        }

        var previousJobId = version.RenderJobId;
        var bound = previousJobId is Guid oldJobId && oldJobId != job.Id
            ? await _versions.TryRebindSceneAudioVersionRenderJobAsync(version.Id, oldJobId, job.Id, ct)
            : await _versions.TryBindSceneAudioVersionRenderJobAsync(version.Id, job.Id, ct);
        if (!bound)
        {
            await RecordEnqueueFailureAsync(projectId, scene, settings, triggerSource,
                "render_job_binding_conflict", "The audio version is already bound to another render job.", ct);
            return false;
        }

        await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_QUEUED", "info",
            $"Scene {scene.SceneIndex} external voice queued.",
            new
            {
                projectId,
                sceneId,
                scene.SceneIndex,
                triggerSource,
                versionId = version.Id,
                logicalRequestId,
                voiceCatalogCode = settings?.VoiceCatalogCode,
                voiceCode,
                ttsRate,
                jobId = job.Id
            }, ct);

        await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} audio auto enqueue submitted.",
            new { projectId, sceneId, scene.SceneIndex, triggerSource, logicalRequestId, jobId = job.Id, versionId = version.Id }, ct);

        return true;
    }

    private async Task<RenderJobDto?> ResolveReusableRenderJobAsync(Guid? renderJobId, string logicalRequestId, CancellationToken ct)
    {
        if (renderJobId is Guid jobId)
        {
            var current = await _jobs.GetAsync(jobId, ct);
            if (IsReusableRenderJob(current))
            {
                return current;
            }
        }

        var byLogCode = await _jobs.GetByLogCodeAsync(logicalRequestId, ct);
        return IsReusableRenderJob(byLogCode) ? byLogCode : null;
    }

    private static bool IsReusableRenderJob(RenderJobDto? job)
        => job is not null && IsReusableRenderJobStatus(job.Status);

    private static bool IsReusableRenderJobStatus(string? status)
        => status is not null && (
            status.Equals(RenderJobStatuses.Queued, StringComparison.OrdinalIgnoreCase)
            || status.Equals(RenderJobStatuses.Preparing, StringComparison.OrdinalIgnoreCase)
            || status.Equals(RenderJobStatuses.Rendering, StringComparison.OrdinalIgnoreCase)
            || status.Equals(RenderJobStatuses.PostProcessing, StringComparison.OrdinalIgnoreCase)
            || status.Equals(RenderJobStatuses.PendingReconciliation, StringComparison.OrdinalIgnoreCase)
            || status.Equals(RenderJobStatuses.Processing, StringComparison.OrdinalIgnoreCase));

    private async Task<AiStudioVoiceDto?> ResolveVoiceAsync(RVideoJobSettingsDto? settings, string? sceneVoiceCode, CancellationToken ct)
    {
        if (settings is null) return null;
        // A voice picked for this scene wins over the job's voice.
        if (sceneVoiceCode is not null && await _catalog.GetVoiceByCodeAsync(sceneVoiceCode, activeOnly: true, ct) is { } sceneVoice)
            return sceneVoice;
        if (!string.IsNullOrWhiteSpace(settings.VoiceCatalogCode))
            return await _catalog.GetVoiceByCodeAsync(settings.VoiceCatalogCode, activeOnly: true, ct);
        // "Giọng từ thư viện" chosen without picking a voice: use the catalog's default voice instead of producing none.
        var voices = await _catalog.ListVoicesAsync(activeOnly: true, ct: ct);
        return voices.FirstOrDefault(x => x.IsDefault) ?? voices.FirstOrDefault();
    }

    private async Task RecordEnqueueFailureAsync(
        long projectId,
        VideoProjectSceneDto scene,
        RVideoJobSettingsDto? settings,
        string triggerSource,
        string reason,
        string error,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_FAILED", "error",
            $"Scene {scene.SceneIndex} voice auto enqueue failed.",
            new
            {
                projectId,
                sceneId = scene.Id,
                sceneIndex = scene.SceneIndex,
                voiceCatalogCode = settings?.VoiceCatalogCode,
                triggerSource,
                reason,
                error
            }, ct);
    }

    private static string ResolveProviderVoiceCode(AiStudioVoiceDto voice)
    {
        if (string.IsNullOrWhiteSpace(voice.ProviderVoiceId))
        {
            throw new InvalidOperationException("RVIDEO_VBEE_PROVIDER_VOICE_ID_MISSING");
        }

        return voice.ProviderVoiceId.Trim();
    }

    private async Task<Guid?> ResolvePointServiceIdAsync(Guid? coreJobId, CancellationToken ct)
    {
        if (coreJobId is not Guid jobId)
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
            new { jobId, tenant = _tenant.TenantId });
    }

    private static decimal ResolveTtsRate(ScenePromptMetadata metadata, RVideoJobSettingsDto? settings)
    {
        if (metadata.TtsRate is decimal sceneRate && sceneRate > 0)
        {
            return sceneRate;
        }

        if (settings?.DefaultTtsRate is decimal projectRate && projectRate > 0)
        {
            return projectRate;
        }

        return 1.0m;
    }

    private static void ValidateTtsRate(AiStudioVoiceDto voice, decimal rate)
    {
        if (rate <= 0)
        {
            throw new InvalidOperationException("RVIDEO_TTS_RATE_INVALID");
        }

        if (voice.MinRate is decimal min && rate < min)
        {
            throw new InvalidOperationException("RVIDEO_TTS_RATE_OUT_OF_RANGE");
        }

        if (voice.MaxRate is decimal max && rate > max)
        {
            throw new InvalidOperationException("RVIDEO_TTS_RATE_OUT_OF_RANGE");
        }
    }

    public static string BuildLogicalRequestKey(long projectId, long sceneId)
        => $"rvideo-scene-audio:{projectId}:{sceneId}";
}

public sealed class SceneAudioRenderWorkItemInput
{
    public Guid? ParentJobId { get; set; }
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int SceneIndex { get; set; }
    public Guid AudioVersionId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string VoiceCatalogCode { get; set; } = string.Empty;
    public string VoiceCode { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public string NarrationText { get; set; } = string.Empty;
    public string? VoiceInstruction { get; set; }
    public decimal TtsRate { get; set; }
    public decimal? DefaultTtsRate { get; set; }
    public decimal CustomerPointRate { get; set; }
    public string CustomerPointQuality { get; set; } = ServiceSellPriceQualityTiers.Standard;
    public int SampleRate { get; set; }
    public int Bitrate { get; set; }
    public decimal SpeedRate { get; set; }
    public string? CallbackUrl { get; set; }
    public string? AppId { get; set; }
    public PointBillingIntent BillingIntent { get; set; } = PointBillingIntent.InitialRender;
    public Guid? BillingOperationId { get; set; }
    public AiStudioVoiceDto? VoiceSnapshot { get; set; }
    public object SceneSnapshot { get; set; } = new { };
    public object RenderConfigSnapshot { get; set; } = new { };
}
