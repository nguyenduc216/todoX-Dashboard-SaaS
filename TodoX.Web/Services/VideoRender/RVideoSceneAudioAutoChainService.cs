using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneAudioAutoChainService
{
    Task<bool> TryEnqueueSceneAudioAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default);
}

public sealed class RVideoSceneAudioAutoChainService : IRVideoSceneAudioAutoChainService
{
    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRenderJobService _jobs;
    private readonly IAiStudioCatalogService _catalog;
    private readonly IOptionsMonitor<VbeeOptions> _options;
    private readonly ILogger<RVideoSceneAudioAutoChainService> _logger;

    public RVideoSceneAudioAutoChainService(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        IRenderJobService jobs,
        IAiStudioCatalogService catalog,
        IOptionsMonitor<VbeeOptions> options,
        ILogger<RVideoSceneAudioAutoChainService> logger)
    {
        _repo = repo;
        _versions = versions;
        _settings = settings;
        _jobs = jobs;
        _catalog = catalog;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> TryEnqueueSceneAudioAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default)
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
        if (selected is not null && string.Equals(selected.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var logicalRequestId = BuildLogicalRequestKey(projectId, sceneId);
        var existing = await _versions.GetSceneAudioVersionByLogicalRequestIdAsync(logicalRequestId, ct);
        if (existing is null && await _versions.HasActiveAudioVersionAsync(sceneId, ct))
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED", "info",
                $"Scene {scene.SceneIndex} voice auto enqueue skipped because another audio attempt is already active.",
                new { projectId, sceneId, scene.SceneIndex, triggerSource, logicalRequestId }, ct);
            return false;
        }

        var voice = await ResolveVoiceAsync(settings, ct);
        if (voice is null)
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED", "warning",
                $"Scene {scene.SceneIndex} voice auto enqueue skipped because the selected voice is unavailable.",
                new { projectId, sceneId, scene.SceneIndex, triggerSource, voiceCatalogCode = settings?.VoiceCatalogCode }, ct);
            return false;
        }

        var ttsRate = ResolveTtsRate(metadata, settings);
        ValidateTtsRate(voice, ttsRate);

        var operationId = existing?.RenderJobId ?? Guid.NewGuid();
        var version = existing ?? await _versions.CreateQueuedSceneAudioVersionAsync(new SceneAudioVersionCreateRequest(
            project.Id,
            scene.Id,
            project.UserId,
            project.CustomerId,
            operationId,
            logicalRequestId,
            settings?.VoiceCatalogCode,
            JsonSerializer.Serialize(voice, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            voiceText,
            voiceInstruction,
            ttsRate,
            scene.DurationSeconds,
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
                voiceCode = voice.ProviderVoiceId ?? voice.Code,
                defaultTtsRate = settings?.DefaultTtsRate,
                vbeeDefaults = new
                {
                    _options.CurrentValue.DefaultSampleRate,
                    _options.CurrentValue.DefaultBitrate,
                    _options.CurrentValue.DefaultSpeedRate
                }
            }), ct);

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
                voiceCode = voice.ProviderVoiceId ?? voice.Code,
                ttsRate
            }, ct);

        var model = new RenderJobCreateModel
        {
            JobType = RenderJobTypes.RenderSceneAudio,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            Input = new SceneAudioRenderWorkItemInput
            {
                ParentJobId = operationId,
                ProjectId = project.Id,
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                AudioVersionId = version.Id,
                UserId = project.UserId,
                CustomerId = project.CustomerId,
                LogicalRequestId = logicalRequestId,
                VoiceCatalogCode = settings?.VoiceCatalogCode ?? string.Empty,
                VoiceCode = voice.ProviderVoiceId ?? voice.Code,
                VoiceName = voice.Name,
                NarrationText = voiceText,
                VoiceInstruction = voiceInstruction,
                TtsRate = ttsRate,
                DefaultTtsRate = settings?.DefaultTtsRate,
                SampleRate = _options.CurrentValue.ResolveSampleRate(voice.ProviderVoiceId ?? voice.Code),
                Bitrate = _options.CurrentValue.DefaultBitrate,
                SpeedRate = _options.CurrentValue.DefaultSpeedRate,
                CallbackUrl = _options.CurrentValue.GetCallbackUriOrNull()?.ToString(),
                AppId = _options.CurrentValue.AppId,
                VoiceSnapshot = voice,
                SceneSnapshot = new { scene.Id, scene.ProjectId, scene.SceneIndex, scene.Title, scene.DurationSeconds, scene.ScenePrompt },
                RenderConfigSnapshot = new
                {
                    projectId,
                    sceneId,
                    operationId,
                    triggerSource,
                    voiceCatalogCode = settings?.VoiceCatalogCode,
                    voiceCode = voice.ProviderVoiceId ?? voice.Code,
                    logicalRequestId
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
            ModelCode = voice.ProviderVoiceId ?? voice.Code,
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        };

        var (job, alreadyActive) = await _jobs.EnqueueForLogCodeIfNoneActiveAsync(model, logicalRequestId, ct);
        if (alreadyActive)
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED", "info",
                $"Scene {scene.SceneIndex} audio auto enqueue skipped because the same request is already active.",
                new { projectId, sceneId, scene.SceneIndex, triggerSource, logicalRequestId, activeJobId = job.Id }, ct);
            return false;
        }

        await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_AUTO_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} audio auto enqueue submitted.",
            new { projectId, sceneId, scene.SceneIndex, triggerSource, logicalRequestId, jobId = job.Id, versionId = version.Id }, ct);

        return true;
    }

    private async Task<AiStudioVoiceDto?> ResolveVoiceAsync(RVideoJobSettingsDto? settings, CancellationToken ct)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.VoiceCatalogCode))
        {
            return null;
        }

        return await _catalog.GetVoiceByCodeAsync(settings.VoiceCatalogCode, activeOnly: true, ct);
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
    public Guid ParentJobId { get; set; }
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
    public int SampleRate { get; set; }
    public int Bitrate { get; set; }
    public decimal SpeedRate { get; set; }
    public string? CallbackUrl { get; set; }
    public string? AppId { get; set; }
    public AiStudioVoiceDto? VoiceSnapshot { get; set; }
    public object SceneSnapshot { get; set; } = new { };
    public object RenderConfigSnapshot { get; set; } = new { };
}
