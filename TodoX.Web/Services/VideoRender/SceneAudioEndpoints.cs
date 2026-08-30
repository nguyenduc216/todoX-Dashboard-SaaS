using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.VideoRender;

public static class SceneAudioEndpoints
{
    public static void MapSceneAudioEndpoints(this WebApplication app)
    {
        app.MapPost("/api/providers/vbee/callback", HandleVbeeCallbackAsync).DisableAntiforgery();
    }

    public static VbeeCallbackAuthorizationStatus GetCallbackAuthorizationStatus(HttpRequest request, string? configuredSecret)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return VbeeCallbackAuthorizationStatus.NotConfigured;
        }

        var provided = request.Headers["X-VBEE-CALLBACK-SECRET"].FirstOrDefault()
                       ?? request.Query["secret"].FirstOrDefault()
                       ?? request.Query["callback_secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return VbeeCallbackAuthorizationStatus.MissingSecret;
        }

        return string.Equals(provided, configuredSecret, StringComparison.Ordinal)
            ? VbeeCallbackAuthorizationStatus.Authorized
            : VbeeCallbackAuthorizationStatus.InvalidSecret;
    }

    private static async Task<IResult> HandleVbeeCallbackAsync(
        HttpRequest request,
        TenantContext tenant,
        TodoXConnectionFactory factory,
        IVbeeVoiceClient vbee,
        IVbeeRuntimeConfigProvider runtimeConfig,
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IMediaFileService media,
        IRVideoSceneMediaFinalizerService finalizer,
        CancellationToken ct)
    {
        await tenant.EnsureLoadedAsync(ct);
        var options = await runtimeConfig.GetAsync(ct);
        var authorization = GetCallbackAuthorizationStatus(request, options.CallbackSecret);
        if (authorization == VbeeCallbackAuthorizationStatus.NotConfigured)
        {
            return Results.Json(new { success = false, message = "Vbee callback secret is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (authorization is VbeeCallbackAuthorizationStatus.MissingSecret or VbeeCallbackAuthorizationStatus.InvalidSecret)
        {
            return Results.Json(new { success = false, message = "Invalid callback secret." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var payload = await vbee.ParseCallbackAsync(request, ct);
        if (string.IsNullOrWhiteSpace(payload.RequestId))
        {
            return Results.BadRequest(new { success = false, message = "Missing request_id." });
        }

        using (var conn = await factory.OpenAsync(ct))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.vbee_callback_inbox
                    (tenant_id, provider_code, provider_task_id, scene_id, scene_audio_version_id,
                     raw_payload_json, received_at, updated_at)
                VALUES
                    (@tenantId, 'vbee', @requestId, @sceneId, @sceneAudioVersionId,
                     CAST(@rawPayload AS jsonb), now(), now())
                ON CONFLICT (tenant_id, provider_task_id)
                DO UPDATE SET
                    scene_id=COALESCE(EXCLUDED.scene_id, video_render.vbee_callback_inbox.scene_id),
                    scene_audio_version_id=COALESCE(EXCLUDED.scene_audio_version_id, video_render.vbee_callback_inbox.scene_audio_version_id),
                    raw_payload_json=EXCLUDED.raw_payload_json,
                    updated_at=now();
                """,
                new
                {
                    tenantId = tenant.TenantId,
                    requestId = payload.RequestId,
                    sceneId = payload.SceneId,
                    sceneAudioVersionId = (Guid?)null,
                    rawPayload = JsonSerializer.Serialize(payload.Raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                });
        }

        var version = await versions.GetSceneAudioVersionByProviderTaskIdAsync(payload.RequestId, ct);
        if (version is null)
        {
            return Results.Ok(new { success = true, request_id = payload.RequestId, matched = false });
        }

        using (var conn = await factory.OpenAsync(ct))
        {
            await conn.ExecuteAsync(
                """
                UPDATE video_render.vbee_callback_inbox
                   SET scene_audio_version_id=@sceneAudioVersionId,
                       updated_at=now()
                 WHERE tenant_id=@tenantId AND provider_task_id=@requestId;
                """,
                new { tenantId = tenant.TenantId, requestId = payload.RequestId, sceneAudioVersionId = version.Id });
        }

        if (string.Equals(version.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { success = true, request_id = payload.RequestId, matched = true, already_completed = true });
        }

        if (string.Equals(payload.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(payload.ErrorCode)
            || !string.IsNullOrWhiteSpace(payload.ErrorMessage))
        {
            await versions.FailSceneAudioVersionAsync(version.Id, payload.ErrorCode ?? "VBEE_FAILED", payload.ErrorMessage ?? "Vbee callback failed.", ct);
            return Results.Ok(new { success = true, request_id = payload.RequestId, matched = true, status = "failed" });
        }

        if (string.IsNullOrWhiteSpace(payload.AudioUrl))
        {
            return Results.Ok(new { success = true, request_id = payload.RequestId, matched = true, status = payload.Status ?? "SUBMITTED" });
        }

        if (!IsHttpAudioUrl(payload.AudioUrl))
        {
            await versions.FailSceneAudioVersionAsync(version.Id, "VBEE_AUDIO_URL_INVALID", "Vbee returned an invalid audio URL.", ct);
            return Results.Ok(new { success = true, request_id = payload.RequestId, matched = true, status = "failed" });
        }

        try
        {
            await CompleteSceneAudioAsync(tenant.TenantId, version, payload.RequestId, payload.AudioUrl!, media, versions, finalizer, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await repo.AddProjectEventAsync(version.ProjectId, "SCENE_AUDIO_DOWNLOAD_FAILED", "error",
                "Vbee callback audio download failed.",
                new
                {
                    projectId = version.ProjectId,
                    sceneId = version.SceneId,
                    requestId = payload.RequestId,
                    audioUrl = payload.AudioUrl,
                    error = ex.Message
                }, ct);
            throw;
        }
        return Results.Ok(new { success = true, request_id = payload.RequestId, matched = true, status = "completed" });
    }

    private static async Task CompleteSceneAudioAsync(
        Guid tenantId,
        SceneAudioVersionDto version,
        string requestId,
        string audioUrl,
        IMediaFileService media,
        ISceneMediaVersioningService versions,
        IRVideoSceneMediaFinalizerService finalizer,
        CancellationToken ct)
    {
        if (string.Equals(version.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var saved = await media.DownloadAndSaveBinaryAtObjectKeyAsync(
            audioUrl,
            version.StorageKey ?? SceneMediaStorageKeys.SceneAudioOutput(tenantId, version.ProjectId, version.SceneId, version.Id),
            "scene_audio",
            "audio/mpeg",
            userId: null,
            customerId: null,
            tenantId,
            ct);

        await versions.CompleteSceneAudioVersionAsync(version.Id, new SceneAudioVersionCompleteRequest(
            saved.PublicUrl ?? saved.FileUrl,
            saved.ObjectKey,
            DurationSeconds: null,
            ProviderCode: "vbee",
            ModelName: version.VoiceCatalogCode,
            ProviderCapabilityId: null,
            ProviderTaskId: requestId,
            BillingLogicalRequestId: version.LogicalRequestId,
            EstimatedUsd: null,
            ActualUsd: null,
            ChargedPoints: 0,
            RefundedPoints: 0,
            CostSource: "configured_tariff",
            ResultMediaId: saved.Id,
            MimeType: saved.MimeType), ct);

        await finalizer.TryFinalizeSceneMediaAsync(version.ProjectId, version.SceneId, "VBEE_CALLBACK", ct);
    }

    private static bool IsHttpAudioUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public enum VbeeCallbackAuthorizationStatus
{
    NotConfigured,
    MissingSecret,
    InvalidSecret,
    Authorized
}
