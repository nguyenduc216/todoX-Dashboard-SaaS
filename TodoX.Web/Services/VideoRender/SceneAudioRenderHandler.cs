using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TodoX.Web.Services;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneAudioRenderHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultMaxReconciliationRetries = 3;

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IMediaFileService _media;
    private readonly IAiStudioCatalogService _catalog;
    private readonly IVbeeVoiceClient _vbee;
    private readonly IRenderJobService _jobs;
    private readonly IRVideoSceneMediaFinalizerService _finalizer;
    private readonly TenantContext _tenant;
    private readonly IOptionsMonitor<VbeeOptions> _options;
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
        IOptionsMonitor<VbeeOptions> options,
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
        _options = options;
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

        var voice = await ResolveVoiceAsync(input, ct);
        var sampleRate = _options.CurrentValue.ResolveSampleRate(input.VoiceCode);
        var validCallbackUrl = VbeeOptions.BuildAuthorizedCallbackUriOrNull(
            string.IsNullOrWhiteSpace(input.CallbackUrl) ? _options.CurrentValue.CallbackUrl : input.CallbackUrl,
            _options.CurrentValue.CallbackSecret)?.ToString();

        _options.CurrentValue.GetTokenOrThrow();
        if (string.IsNullOrWhiteSpace(input.AppId ?? _options.CurrentValue.AppId))
        {
            throw new InvalidOperationException("VBEE_APP_ID is missing.");
        }
        if (string.IsNullOrWhiteSpace(input.VoiceCode))
        {
            throw new InvalidOperationException("VBEE voice_code is missing.");
        }
        if (string.IsNullOrWhiteSpace(validCallbackUrl) || !Uri.TryCreate(validCallbackUrl, UriKind.Absolute, out var callbackUri))
        {
            throw new InvalidOperationException("VBEE callback URL is invalid.");
        }
        if (string.IsNullOrWhiteSpace(input.NarrationText))
        {
            throw new InvalidOperationException("VBEE narration text is empty.");
        }

        var submitRequest = new VbeeVoiceSubmitRequest(
            input.VoiceCode,
            input.NarrationText,
            input.TtsRate,
            input.VoiceInstruction,
            callbackUri.ToString(),
            input.LogicalRequestId,
            sampleRate,
            input.Bitrate,
            input.SpeedRate,
            input.AppId ?? _options.CurrentValue.AppId);

        var requestId = string.IsNullOrWhiteSpace(version.ProviderTaskId) ? null : version.ProviderTaskId.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMITTING", "info",
                $"Scene {scene.SceneIndex} external voice submitting.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, null, "SUBMITTING", sampleRate), ct);
            var submitted = await _vbee.SubmitAsync(submitRequest, ct);
            if (IsDirectAudio(submitted.AudioUrl))
            {
                await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_DIRECT_RESULT", "info",
                    $"Scene {scene.SceneIndex} external voice returned a direct MP3.",
                    BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "SUCCESS", sampleRate, submitted.AudioUrl), ct);

                await CompleteFromAudioUrlAsync(project, scene, version, input, requestId, submitted.AudioUrl!, submitted.Response, ct);
                return;
            }

            requestId = NormalizeRequestId(submitted.RequestId);
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

            await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, "vbee", voice?.ProviderCode ?? input.VoiceCode, null, requestId, ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMITTED", "info",
                $"Scene {scene.SceneIndex} external voice submitted.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, submitted.RawStatus ?? "SUBMITTED", sampleRate), ct);
        }
        else
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMITTING", "info",
                $"Scene {scene.SceneIndex} external voice resumed from existing request.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "RESUME", sampleRate), ct);
        }

        var status = await _vbee.GetStatusAsync(requestId, ct);
        var normalizedStatus = ReadString(status, "status", "state");
        var audioUrl = ReadString(status, "audio_link", "audio_url", "audioUrl", "download_url", "downloadUrl", "url");
        var errorCode = ReadString(status, "error_code", "errorCode", "code");
        var errorMessage = ReadString(status, "error_message", "errorMessage", "message", "error");
        var sampleRateRetryApplied = HasSampleRateRetryApplied(version.RenderConfigJson);

        if (TryResolveSampleRateRetry(status, errorCode, errorMessage, sampleRate, sampleRateRetryApplied, out var retryRate))
        {
            var fallbackRate = retryRate;
            var retryConfigJson = BuildSampleRateRetryConfigJson(version.RenderConfigJson, sampleRate, fallbackRate, requestId);
            await _versions.UpdateSceneAudioVersionRenderConfigAsync(version.Id, retryConfigJson, ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_SAMPLE_RATE_RETRY", "warning",
                $"Scene {scene.SceneIndex} external voice retry with fallback sample rate.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, normalizedStatus ?? "SAMPLE_RATE_RETRY", sampleRate, audioUrl, errorCode, errorMessage, true, sampleRate, fallbackRate), ct);

            var retryRequest = submitRequest with { SampleRate = fallbackRate };
            var retrySubmitted = await _vbee.SubmitAsync(retryRequest, ct);
            if (IsDirectAudio(retrySubmitted.AudioUrl))
            {
                var directRetryRequestId = NormalizeRequestId(retrySubmitted.RequestId) ?? requestId;
                await CompleteFromAudioUrlAsync(project, scene, version, input, directRetryRequestId, retrySubmitted.AudioUrl!, retrySubmitted.Response, ct);
                return;
            }

            var retryRequestId = NormalizeRequestId(retrySubmitted.RequestId);
            if (string.IsNullOrWhiteSpace(retryRequestId))
            {
                await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMIT_FAILED", "error",
                    $"Scene {scene.SceneIndex} external voice retry did not return a provider request id.",
                    BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, null, "FAILED", fallbackRate,
                        errorCode: "VBEE_SUBMIT_REQUEST_ID_MISSING",
                        errorMessage: "Vbee retry returned no request_id and no direct MP3 audio URL.",
                        reason: "provider_request_id_missing"), ct);
                throw new InvalidOperationException("VBEE_SUBMIT_REQUEST_ID_MISSING");
            }

            await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, "vbee", voice?.ProviderCode ?? input.VoiceCode, null, retryRequestId, ct);
            await _jobs.ScheduleRetryAsync(job.Id, _options.CurrentValue.PollInterval, "VBEE_SAMPLE_RATE_RETRY", "Waiting for the retried Vbee request.", ct);
            throw new RenderJobDeferredException("Waiting for retried Vbee request.");
        }

        if (string.Equals(normalizedStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase) && IsDirectAudio(audioUrl))
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_CALLBACK_RECEIVED", "info",
                $"Scene {scene.SceneIndex} external voice completed from poll.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, normalizedStatus ?? "SUCCESS", sampleRate, audioUrl), ct);
            await CompleteFromAudioUrlAsync(project, scene, version, input, requestId, audioUrl!, status, ct);
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

        await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, "vbee", voice?.ProviderCode ?? input.VoiceCode, null, requestId, ct);
        await _jobs.ScheduleRetryAsync(job.Id, _options.CurrentValue.PollInterval, "VBEE_PENDING", "Waiting for Vbee callback or poll result.", ct);
        throw new RenderJobDeferredException("Waiting for Vbee callback or poll result.");
    }

    private async Task CompleteFromAudioUrlAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneAudioVersionDto version,
        SceneAudioRenderWorkItemInput input,
        string? requestId,
        string audioUrl,
        JsonObject? providerResponse,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_RESULT_DOWNLOADING", "info",
            $"Scene {scene.SceneIndex} external voice downloading MP3.",
            BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "DOWNLOAD", _options.CurrentValue.ResolveSampleRate(input.VoiceCode), audioUrl), ct);

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
                    "DOWNLOAD_FAILED", _options.CurrentValue.ResolveSampleRate(input.VoiceCode), audioUrl,
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
            BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "SUCCESS", _options.CurrentValue.ResolveSampleRate(input.VoiceCode), audioUrl), ct);

        await _finalizer.TryFinalizeSceneMediaAsync(project.Id, scene.Id, "SCENE_AUDIO_READY", ct);
    }

    private async Task<AiStudioVoiceDto?> ResolveVoiceAsync(SceneAudioRenderWorkItemInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.VoiceCatalogCode))
        {
            return input.VoiceSnapshot;
        }

        return await _catalog.GetVoiceByCodeAsync(input.VoiceCatalogCode, activeOnly: true, ct) ?? input.VoiceSnapshot;
    }

    private static bool IsDirectAudio(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool IsTerminalFailure(string? status, string? errorCode)
        => status?.Trim().ToUpperInvariant() is "FAILED" or "FAILURE" or "ERROR" or "REJECTED" or "CANCELLED" or "CANCELED"
           || !string.IsNullOrWhiteSpace(errorCode);

    internal static bool TryResolveSampleRateRetry(JsonObject providerResponse, string? errorCode, string? errorMessage, int currentSampleRate, bool sampleRateRetryApplied, out int retryRate)
    {
        retryRate = 0;
        if (sampleRateRetryApplied || currentSampleRate <= 0)
        {
            return false;
        }

        if (!(string.Equals(errorCode, "1013", StringComparison.OrdinalIgnoreCase)
              || (errorMessage?.Contains("sample rate", StringComparison.OrdinalIgnoreCase) ?? false)
              || providerResponse.ToJsonString().Contains("sample_rate", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        retryRate = 0;
        return true;
    }

    internal static bool HasSampleRateRetryApplied(string? renderConfigJson)
    {
        if (string.IsNullOrWhiteSpace(renderConfigJson))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(renderConfigJson) as JsonObject;
            return root?["vbee_retry"]?["sample_rate_retry_applied"]?.GetValue<bool>() == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string BuildSampleRateRetryConfigJson(string? renderConfigJson, int originalSampleRate, int fallbackSampleRate, string requestId)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(renderConfigJson) ? "{}" : renderConfigJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["vbee_retry"] = new JsonObject
        {
            ["sample_rate_retry_applied"] = true,
            ["original_sample_rate"] = originalSampleRate,
            ["fallback_sample_rate"] = fallbackSampleRate,
            ["request_id"] = requestId,
            ["updated_at_utc"] = DateTimeOffset.UtcNow
        };

        return root.ToJsonString(JsonOptions);
    }

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
