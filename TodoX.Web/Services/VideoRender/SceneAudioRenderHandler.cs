using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.Web.Services;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneAudioRenderHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IMediaFileService _media;
    private readonly IAiStudioCatalogService _catalog;
    private readonly IVbeeVoiceClient _vbee;
    private readonly IRenderJobService _jobs;
    private readonly IRVideoSceneMediaFinalizerService _finalizer;
    private readonly TenantContext _tenant;
    private readonly IVbeeRuntimeConfigProvider _runtimeConfig;
    private readonly ILogger<SceneAudioRenderHandler> _logger;

    public string JobType => RenderJobTypes.RenderSceneAudio;

    public SceneAudioRenderHandler(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IMediaFileService media,
        IAiStudioCatalogService catalog,
        IVbeeVoiceClient vbee,
        IRenderJobService jobs,
        IRVideoSceneMediaFinalizerService finalizer,
        TenantContext tenant,
        IVbeeRuntimeConfigProvider runtimeConfig,
        ILogger<SceneAudioRenderHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _media = media;
        _catalog = catalog;
        _vbee = vbee;
        _jobs = jobs;
        _finalizer = finalizer;
        _tenant = tenant;
        _runtimeConfig = runtimeConfig;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneAudioRenderWorkItemInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene audio worker input invalid.");
        if (input.ProjectId <= 0 || input.SceneId <= 0 || input.AudioVersionId == Guid.Empty || string.IsNullOrWhiteSpace(input.LogicalRequestId))
        {
            throw new InvalidOperationException("Missing scene audio worker snapshot.");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scene = project.Scenes.FirstOrDefault(x => x.Id == input.SceneId)
            ?? throw new InvalidOperationException("Video scene not found.");

        var version = (await _versions.ListSceneAudioVersionsAsync(scene.Id, 0, 100, ct))
            .FirstOrDefault(x => x.Id == input.AudioVersionId)
            ?? throw new InvalidOperationException("Scene audio version not found.");

        if (version.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var options = await _runtimeConfig.GetAsync(ct);
        var sampleRate = options.ResolveSampleRate(input.VoiceCode);

        options.GetTokenOrThrow();
        if (string.IsNullOrWhiteSpace(input.AppId ?? options.AppId))
        {
            throw new InvalidOperationException("VBEE_APP_ID is missing.");
        }
        if (string.IsNullOrWhiteSpace(input.VoiceCode))
        {
            throw new InvalidOperationException("VBEE voice_code is missing.");
        }
        if (string.IsNullOrWhiteSpace(input.NarrationText))
        {
            throw new InvalidOperationException("VBEE narration text is empty.");
        }

        var requestId = string.IsNullOrWhiteSpace(version.ProviderTaskId) ? null : version.ProviderTaskId.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            var submitRequest = new VbeeVoiceSubmitRequest(
                input.VoiceCode,
                input.NarrationText,
                input.TtsRate,
                input.VoiceInstruction,
                string.Empty,
                null,
                sampleRate,
                input.Bitrate,
                input.SpeedRate,
                input.AppId ?? options.AppId);

            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMITTING", "info",
                $"Scene {scene.SceneIndex} external voice submitting.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, null, "SUBMITTING", sampleRate), ct);
            var submitted = await _vbee.SubmitAsync(submitRequest, options, ct);
            requestId = NormalizeRequestId(submitted.RequestId);
            if (IsDirectAudio(submitted.AudioUrl))
            {
                await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_DIRECT_RESULT", "info",
                    $"Scene {scene.SceneIndex} external voice returned a direct MP3.",
                    BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "SUCCESS", sampleRate, submitted.AudioUrl), ct);

                await CompleteFromAudioUrlAsync(project, scene, version, input, requestId, submitted.AudioUrl!, submitted.Response, sampleRate, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMIT_FAILED", "error",
                    $"Scene {scene.SceneIndex} external voice submit did not return a provider request id.",
                    BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, null, "FAILED", sampleRate,
                        errorCode: "VBEE_SUBMIT_REQUEST_ID_MISSING",
                        errorMessage: "Vbee returned no request_id and no direct MP3 audio URL.",
                        reason: "provider_request_id_missing"), ct);
                throw new InvalidOperationException("VBEE_SUBMIT_REQUEST_ID_MISSING");
            }

            await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, "vbee", input.VoiceCode, null, requestId, ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMITTED", "info",
                $"Scene {scene.SceneIndex} external voice submitted.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, submitted.RawStatus ?? "SUBMITTED", sampleRate), ct);

            await _jobs.ScheduleProviderPollAsync(job.Id, options.PollInterval, "VBEE_SUBMITTED", "Waiting for Vbee poll result.", ct);
            throw new RenderJobDeferredException("Waiting for Vbee poll result.");
        }
        else
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_POLLING", "info",
                $"Scene {scene.SceneIndex} external voice resumed from existing request.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "POLLING", sampleRate), ct);
        }

        var status = await _vbee.GetStatusAsync(requestId, options, ct);
        var normalizedStatus = ReadString(status, "status", "state");
        var audioUrl = ReadString(status, "audio_link", "audio_url", "audioUrl", "download_url", "downloadUrl", "url");
        var errorCode = ReadString(status, "error_code", "errorCode", "code");
        var errorMessage = ReadString(status, "error_message", "errorMessage", "message", "error");

        if (IsDirectAudio(audioUrl))
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_RESULT_READY", "info",
                $"Scene {scene.SceneIndex} external voice completed from poll.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, normalizedStatus ?? "SUCCESS", sampleRate, audioUrl), ct);
            await CompleteFromAudioUrlAsync(project, scene, version, input, requestId, audioUrl!, status, sampleRate, ct);
            return;
        }

        if (IsTerminalFailure(normalizedStatus, errorCode))
        {
            await _versions.FailSceneAudioVersionAsync(version.Id, errorCode ?? "VBEE_FAILED", errorMessage ?? "Vbee submission failed.", ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_FAILED", "error",
                $"Scene {scene.SceneIndex} external voice failed.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, normalizedStatus ?? "FAILED", sampleRate, audioUrl, errorCode, errorMessage), ct);
            throw new RenderJobTerminalFailureException(errorMessage ?? "Vbee submission failed.");
        }

        await _jobs.ScheduleProviderPollAsync(job.Id, options.PollInterval, "VBEE_PENDING", "Waiting for Vbee poll result.", ct);
        throw new RenderJobDeferredException("Waiting for Vbee poll result.");
    }

    private async Task CompleteFromAudioUrlAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneAudioVersionDto version,
        SceneAudioRenderWorkItemInput input,
        string? requestId,
        string audioUrl,
        JsonObject? providerResponse,
        int sampleRate,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_RESULT_DOWNLOADING", "info",
            $"Scene {scene.SceneIndex} external voice downloading MP3.",
            BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "DOWNLOAD", sampleRate, audioUrl), ct);

        await _tenant.EnsureLoadedAsync(ct);
        var storageKey = version.StorageKey ?? SceneMediaStorageKeys.SceneAudioOutput(_tenant.TenantId, project.Id, scene.Id, version.Id);
        MediaFileDto saved;
        try
        {
            saved = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(
                audioUrl,
                storageKey,
                "scene_audio",
                "audio/mpeg",
                input.UserId,
                input.CustomerId,
                _tenant.TenantId,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_DOWNLOAD_FAILED", "error",
                $"Scene {scene.SceneIndex} external voice MP3 download failed.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId,
                    "DOWNLOAD_FAILED", sampleRate, audioUrl,
                    errorCode: "SCENE_AUDIO_DOWNLOAD_FAILED", errorMessage: ex.Message), ct);
            throw;
        }

        await _versions.CompleteSceneAudioVersionAsync(version.Id, new SceneAudioVersionCompleteRequest(
            saved.PublicUrl ?? saved.FileUrl,
            saved.ObjectKey,
            version.DurationSeconds,
            ProviderCode: "vbee",
            ModelName: input.VoiceCode,
            ProviderTaskId: requestId,
            BillingLogicalRequestId: input.LogicalRequestId,
            EstimatedUsd: null,
            ActualUsd: null,
            ChargedPoints: 0,
            RefundedPoints: 0,
            CostSource: "configured_tariff",
            ResultMediaId: saved.Id,
            MimeType: saved.MimeType), ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_READY", "info",
            $"Scene {scene.SceneIndex} external voice ready.",
            BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "SUCCESS", sampleRate, audioUrl), ct);

        await _finalizer.TryFinalizeSceneMediaAsync(project.Id, scene.Id, "SCENE_AUDIO_READY", ct);
    }

    private static bool IsDirectAudio(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool IsTerminalFailure(string? status, string? errorCode)
        => status?.Trim().ToUpperInvariant() is "FAILED" or "FAILURE" or "ERROR" or "REJECTED" or "CANCELLED" or "CANCELED"
           || !string.IsNullOrWhiteSpace(errorCode);

    private static string? NormalizeRequestId(string? requestId)
        => string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim();

    private static string? ReadString(JsonObject node, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (node[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }
        return null;
    }

    private static object BuildEventData(
        long projectId,
        long sceneId,
        int sceneIndex,
        SceneAudioRenderWorkItemInput input,
        Guid versionId,
        string? providerTaskId,
        string providerStatus,
        int sampleRate,
        string? audioUrl = null,
        string? errorCode = null,
        string? errorMessage = null,
        bool? sampleRateRetryApplied = null,
        int? originalSampleRate = null,
        int? fallbackSampleRate = null,
        string? reason = null)
        => new
        {
            projectId,
            sceneId,
            sceneIndex,
            audioVersionId = versionId,
            renderJobId = input.ParentJobId,
            input.LogicalRequestId,
            input.VoiceCatalogCode,
            input.VoiceCode,
            providerTaskId,
            providerStatus,
            sampleRate,
            input.TtsRate,
            audioUrl,
            errorCode,
            errorMessage,
            reason,
            sampleRateRetryApplied,
            originalSampleRate,
            fallbackSampleRate
        };

}
