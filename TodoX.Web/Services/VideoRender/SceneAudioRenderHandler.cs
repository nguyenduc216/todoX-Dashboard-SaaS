using System.Net;
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
    private readonly WalletService _wallets;
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
        WalletService wallets,
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
        _wallets = wallets;
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

        var vbeeToken = options.GetTokenOrThrow();
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
            VbeeVoiceSubmitResult submitted;
            try
            {
                submitted = await _vbee.SubmitAsync(submitRequest, options, ct);
            }
            catch (VbeeVoiceSubmitException ex)
            {
                var safeErrorMessage = RedactSensitiveText(
                    ex.ErrorMessage ?? ex.Message,
                    vbeeToken,
                    input.NarrationText,
                    input.VoiceInstruction,
                    input.AppId,
                    options.AppId,
                    options.CallbackSecret);
                await HandleSubmitFailureAsync(project, scene, version, input, sampleRate, ex.HttpStatusCode, ex.ProviderStatus, ex.ResponseTopLevelKeys, ex.ResponseShape, ex.ErrorCode, safeErrorMessage, ct);
                throw new RenderJobTerminalFailureException(safeErrorMessage);
            }

            requestId = NormalizeRequestId(submitted.RequestId);
            if (IsDirectAudio(submitted.AudioUrl))
            {
                await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_DIRECT_RESULT", "info",
                    $"Scene {scene.SceneIndex} external voice returned a direct MP3.",
                    BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId, "SUCCESS", sampleRate, submitted.AudioUrl), ct);

                await CompleteFromAudioUrlAsync(job, project, scene, version, input, requestId, submitted.AudioUrl!, submitted.Response, sampleRate, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                await HandleSubmitFailureAsync(
                    project,
                    scene,
                    version,
                    input,
                    sampleRate,
                    GetProviderHttpStatus(submitted.Response),
                    GetProviderStatus(submitted.Response),
                    GetResponseTopLevelKeys(submitted.Response),
                    submitted.Response is null ? null : VbeeVoiceClient.BuildResponseShape(submitted.Response),
                    "VBEE_SUBMIT_REQUEST_ID_MISSING",
                    "Vbee returned no request_id and no direct MP3 audio URL.",
                    ct);
                throw new RenderJobTerminalFailureException("VBEE_SUBMIT_REQUEST_ID_MISSING");
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
            await CompleteFromAudioUrlAsync(job, project, scene, version, input, requestId, audioUrl!, status, sampleRate, ct);
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
        RenderJobDto job,
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
        catch (InvalidOperationException ex) when (IsHttp400(ex))
        {
            if (await TryRecoverStaleVbeeRequestAsync(job, project, scene, version, input, sampleRate, options: null, ct))
            {
                throw new RenderJobDeferredException("Vbee stale audio URL was recreated; waiting for the replacement request.");
            }

            await _versions.FailSceneAudioVersionAsync(version.Id, "VBEE_AUDIO_DOWNLOAD_HTTP_400", ex.Message, ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_DOWNLOAD_TERMINAL_FAILED", "error",
                $"Scene {scene.SceneIndex} external voice MP3 download returned HTTP 400 after recovery.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId,
                    "DOWNLOAD_FAILED", sampleRate, audioUrl, "VBEE_AUDIO_DOWNLOAD_HTTP_400", ex.Message), ct);
            throw new RenderJobTerminalFailureException("VBEE_AUDIO_DOWNLOAD_HTTP_400");
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

        await ChargeAudioIfNeededAsync(project, scene, version, input, saved, ct);

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

    private async Task<bool> TryRecoverStaleVbeeRequestAsync(
        RenderJobDto job,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneAudioVersionDto version,
        SceneAudioRenderWorkItemInput input,
        int sampleRate,
        VbeeOptions? options,
        CancellationToken ct)
    {
        if (!IsEligibleForVbeeRecovery(version, DateTimeOffset.UtcNow))
        {
            return false;
        }

        options ??= await _runtimeConfig.GetAsync(ct);
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
        // Persist the guard before submission so even a provider-side submit error cannot cause a third attempt.
        await _versions.UpdateSceneAudioVersionRenderConfigAsync(
            version.Id,
            MarkVbeeRecovery(version.RenderConfigJson),
            ct);

        VbeeVoiceSubmitResult submitted;
        try
        {
            submitted = await _vbee.SubmitAsync(submitRequest, options, ct);
        }
        catch (VbeeVoiceSubmitException ex)
        {
            var safeErrorMessage = RedactSensitiveText(
                ex.ErrorMessage ?? ex.Message,
                options.GetTokenOrThrow(),
                input.NarrationText,
                input.VoiceInstruction,
                input.AppId,
                options.AppId,
                options.CallbackSecret);
            await _versions.FailSceneAudioVersionAsync(version.Id, ex.ErrorCode ?? "VBEE_RECOVERY_SUBMIT_FAILED", safeErrorMessage, ct);
            await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_VBEE_RECOVERY_FAILED", "error",
                $"Scene {scene.SceneIndex} external voice recovery submit failed after the one allowed attempt.",
                BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, null,
                    "RECOVERY_FAILED", sampleRate, errorCode: ex.ErrorCode, errorMessage: safeErrorMessage,
                    reason: "stale_url_http_400"), ct);
            return false;
        }
        var requestId = NormalizeRequestId(submitted.RequestId);
        if (string.IsNullOrWhiteSpace(requestId) && !IsDirectAudio(submitted.AudioUrl))
        {
            await _versions.FailSceneAudioVersionAsync(version.Id, "VBEE_RECOVERY_REQUEST_ID_MISSING", "Vbee recovery returned no request_id or audio URL.", ct);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, "vbee", input.VoiceCode, null, requestId, ct);
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_VBEE_RECOVERY_SUBMITTED", "warning",
            $"Scene {scene.SceneIndex} external voice was recreated once after a stale provider URL returned HTTP 400.",
            BuildEventData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, requestId,
                "RECOVERY_SUBMITTED", sampleRate, submitted.AudioUrl, reason: "stale_url_http_400"), ct);

        if (IsDirectAudio(submitted.AudioUrl))
        {
            await CompleteFromAudioUrlAsync(job, project, scene, version, input, requestId, submitted.AudioUrl!, submitted.Response, sampleRate, ct);
            return true;
        }

        var pollOptions = await _runtimeConfig.GetAsync(ct);
        await _jobs.ScheduleProviderPollAsync(job.Id, pollOptions.PollInterval, "VBEE_RECOVERY_SUBMITTED", "Waiting for the recovered Vbee request.", ct);
        return true;
    }

    private static bool IsHttp400(InvalidOperationException ex)
        => ex.Message.Contains("HTTP 400", StringComparison.OrdinalIgnoreCase);

    private async Task ChargeAudioIfNeededAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneAudioVersionDto version,
        SceneAudioRenderWorkItemInput input,
        MediaFileDto saved,
        CancellationToken ct)
    {
        var (intent, billingOperationId) = ResolveBillingIntent(version.RenderConfigJson);
        var customerPointRate = input.TtsRate > 0 ? input.TtsRate : input.DefaultTtsRate ?? 1m;

        if (intent == PointBillingIntent.SystemRetry)
        {
            await _wallets.LogUsageOnlyAsync(input.CustomerId, input.UserId,
                "vbee", input.VoiceCode,
                "rvideo_system_retry_voice", 1, customerPointRate,
                "rvideo", "voice", version.Id, "rvideo_system_retry", "success");
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_AUDIO_SYSTEM_RETRY_RECORDED", "info",
                "Voice result was accepted after a system retry and recorded without a customer debit.",
                new
                {
                    projectId = project.Id,
                    sceneId = scene.Id,
                    scene.SceneIndex,
                    versionId = version.Id,
                    customerPointRate,
                    mediaId = saved.Id
                }, ct);
            return;
        }

        var referenceId = PointBillingReference.ForOperation(
            version.RenderJobId ?? version.Id,
            "rvideo_scene_audio",
            version.Id.ToString("N"),
            intent,
            billingOperationId);
        var charge = await _wallets.ChargeAsync(
            input.CustomerId, input.UserId, customerPointRate, 1,
            intent == PointBillingIntent.UserRerender ? "rvideo_user_rerender_voice" : "rvideo_initial_render_voice",
            "vbee",
            input.VoiceCode,
            "rvideo",
            "voice",
            referenceId,
            "rvideo_scene_audio_success");
        if (!charge.Ok)
        {
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_AUDIO_BILLING_ANOMALY", "error",
                "Voice result was accepted but the success charge could not be completed.",
                new
                {
                    projectId = project.Id,
                    sceneId = scene.Id,
                    scene.SceneIndex,
                    versionId = version.Id,
                    requiredPoints = customerPointRate,
                    error = charge.Error
                }, ct);
            throw new RenderJobTerminalFailureException(charge.Error ?? "Insufficient points after provider success.");
        }
    }

    private static (PointBillingIntent Intent, Guid? BillingOperationId) ResolveBillingIntent(string? renderConfigJson)
    {
        if (!string.IsNullOrWhiteSpace(renderConfigJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(renderConfigJson);
                var root = doc.RootElement;
                var intent = PointBillingIntent.InitialRender;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("billingIntent", out var billingIntent)
                    && billingIntent.ValueKind == JsonValueKind.String
                    && Enum.TryParse<PointBillingIntent>(billingIntent.GetString(), true, out var parsedIntent))
                {
                    intent = parsedIntent;
                }

                Guid? billingOperationId = null;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("billingOperationId", out var billingOperationIdValue)
                    && billingOperationIdValue.ValueKind == JsonValueKind.String
                    && Guid.TryParse(billingOperationIdValue.GetString(), out var parsedOperationId))
                {
                    billingOperationId = parsedOperationId;
                }

                return (intent, billingOperationId);
            }
            catch (JsonException)
            {
            }
        }

        return (PointBillingIntent.InitialRender, null);
    }

    internal static bool IsEligibleForVbeeRecovery(SceneAudioVersionDto version, DateTimeOffset utcNow)
        => version.SubmittedAt is DateTimeOffset submittedAt
           && submittedAt <= utcNow.AddMinutes(-30)
           && !string.IsNullOrWhiteSpace(version.ProviderTaskId)
           && !HasVbeeRecoveryMarker(version.RenderConfigJson);

    private static bool HasVbeeRecoveryMarker(string? renderConfigJson)
        => !string.IsNullOrWhiteSpace(renderConfigJson)
           && JsonNode.Parse(renderConfigJson) is JsonObject obj
           && obj["vbee_recovery_attempted"]?.GetValue<bool>() == true;

    private static string MarkVbeeRecovery(string? renderConfigJson)
    {
        JsonObject obj;
        try
        {
            obj = string.IsNullOrWhiteSpace(renderConfigJson)
                ? new JsonObject()
                : JsonNode.Parse(renderConfigJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            obj = new JsonObject();
        }

        obj["vbee_recovery_attempted"] = true;
        obj["vbee_recovery_at_utc"] = DateTimeOffset.UtcNow;
        return obj.ToJsonString();
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
        return VbeeVoiceClient.FindStringRecursive(node, keys);
    }

    private async Task HandleSubmitFailureAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneAudioVersionDto version,
        SceneAudioRenderWorkItemInput input,
        int sampleRate,
        HttpStatusCode? providerHttpStatus,
        string? providerStatus,
        IReadOnlyList<string>? responseTopLevelKeys,
        JsonObject? responseShape,
        string? providerErrorCode,
        string providerErrorMessage,
        CancellationToken ct)
    {
        await _versions.FailSceneAudioVersionAsync(version.Id, providerErrorCode ?? "VBEE_SUBMIT_FAILED", providerErrorMessage, ct);
        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_PROVIDER_SUBMIT_FAILED", "error",
            $"Scene {scene.SceneIndex} external voice submit failed.",
            BuildSubmitFailureData(project.Id, scene.Id, scene.SceneIndex, input, version.Id, sampleRate,
                providerHttpStatus,
                providerStatus,
                responseTopLevelKeys,
                responseShape,
                providerErrorCode,
                providerErrorMessage), ct);
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

    private static object BuildSubmitFailureData(
        long projectId,
        long sceneId,
        int sceneIndex,
        SceneAudioRenderWorkItemInput input,
        Guid versionId,
        int sampleRate,
        HttpStatusCode? providerHttpStatus,
        string? providerStatus,
        IReadOnlyList<string>? responseTopLevelKeys,
        JsonObject? responseShape,
        string? providerErrorCode,
        string? providerErrorMessage)
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
            providerHttpStatus = providerHttpStatus is null ? (int?)null : (int)providerHttpStatus.Value,
            providerStatus,
            providerErrorCode = providerErrorCode,
            providerErrorMessage = providerErrorMessage,
            responseTopLevelKeys = responseTopLevelKeys ?? Array.Empty<string>(),
            responseShape,
            sampleRate,
            input.TtsRate
        };

    private static HttpStatusCode? GetProviderHttpStatus(JsonObject? response)
    {
        if (response is null || !response.TryGetPropertyValue("http_status", out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var httpStatus))
        {
            return (HttpStatusCode)httpStatus;
        }

        if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
        {
            return (HttpStatusCode)parsed;
        }

        return null;
    }

    private static string? GetProviderStatus(JsonObject? response)
        => response is null ? null : VbeeVoiceClient.FindStringRecursive(response, "status", "state");

    private static IReadOnlyList<string> GetResponseTopLevelKeys(JsonObject? response)
        => response is null ? Array.Empty<string>() : VbeeVoiceClient.GetResponseTopLevelKeys(response);

    private static string RedactSensitiveText(string? value, params string?[] sensitiveValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = value;
        foreach (var sensitive in sensitiveValues)
        {
            if (!string.IsNullOrWhiteSpace(sensitive))
            {
                redacted = redacted.Replace(sensitive, "***", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

}
