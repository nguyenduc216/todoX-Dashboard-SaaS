using System.Text.Json;
using TodoX.EditVideo.Planning;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// Device-facing API for the local TodoX EditVideo exe. Only small JSON travels here — video files never do.
/// Auth: activation uses the license key; every other device call uses the short-lived access token + device hash.
/// </summary>
public static class EditVideoEndpoints
{
    public const string DeviceHashHeader = "X-TodoX-Device";

    public sealed record SessionStartRequest(string? Tool, string? AppVersion);
    public sealed record SessionLogsRequest(List<EditVideoLogEntry>? Entries);
    public sealed record SessionEndRequest(string? Status, JsonElement? Summary);
    public sealed record VoiceRequest(string? Text, string? VoiceCode, Guid? SessionId);

    public static void MapEditVideoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/editvideo/v1");

        group.MapGet("/public-key", (EditVideoLicenseService licenses) =>
            licenses.PublicKeyPem is { } pem
                ? Results.Text(pem, "application/x-pem-file")
                : Error(EditVideoErrorCodes.SigningUnavailable, "License service is not configured."));

        // Connectivity check for the tool's pre-flight; no auth so it also works before activation.
        group.MapGet("/ping", (EditVideoReleaseService releases, EditVideoLicenseService licenses) => Results.Json(new
        {
            success = true,
            serverTime = DateTimeOffset.UtcNow,
            signing = licenses.PublicKeyPem is not null,
            latestVersion = releases.GetLatest()?.Version
        }));

        group.MapPost("/activate", async (HttpContext http, EditVideoActivateRequest body, EditVideoLicenseService licenses, CancellationToken ct) =>
            ToResult(await licenses.ActivateAsync(body, ClientIp(http), ct)))
            .DisableAntiforgery();

        group.MapPost("/heartbeat", async (HttpContext http, EditVideoHeartbeatRequest body, EditVideoLicenseService licenses, CancellationToken ct) =>
            ToResult(await licenses.HeartbeatAsync(BearerToken(http.Request), body, ClientIp(http), ct)))
            .DisableAntiforgery();

        group.MapPost("/deactivate", async (HttpContext http, EditVideoLicenseService licenses, CancellationToken ct) =>
            ToResult(await licenses.DeactivateAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct)))
            .DisableAntiforgery();

        group.MapGet("/me", async (HttpContext http, EditVideoLicenseService licenses, CancellationToken ct) =>
            ToResult(await licenses.GetMeAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct)));

        // ---- usage sessions & logs
        group.MapPost("/sessions", async (HttpContext http, SessionStartRequest body, EditVideoLicenseService licenses, EditVideoUsageService usage, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            var id = await usage.StartSessionAsync(auth.Value.License, auth.Value.Device, body.Tool, body.AppVersion, ct);
            return Results.Json(new { success = true, data = new { sessionId = id } });
        }).DisableAntiforgery();

        group.MapPost("/sessions/{sessionId:guid}/logs", async (Guid sessionId, HttpContext http, SessionLogsRequest body, EditVideoLicenseService licenses, EditVideoUsageService usage, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            var count = await usage.AppendLogsAsync(sessionId, auth.Value.License.Id, body.Entries ?? [], ct);
            return Results.Json(new { success = true, data = new { stored = count } });
        }).DisableAntiforgery();

        group.MapPost("/sessions/{sessionId:guid}/end", async (Guid sessionId, HttpContext http, SessionEndRequest body, EditVideoLicenseService licenses, EditVideoUsageService usage, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            await usage.EndSessionAsync(sessionId, auth.Value.License.Id, body.Status, body.Summary, ct);
            return Results.Json(new { success = true });
        }).DisableAntiforgery();

        // ---- edit planning (the know-how lives here, not in the exe)
        group.MapGet("/styles", () => Results.Json(new
        {
            success = true,
            data = StyleCatalog.All.Select(s => new { s.Id, s.Name, s.Accent })
        }));

        group.MapPost("/plan", async (HttpContext http, PlanRequest body, EditVideoLicenseService licenses, EditVideoPlanService plans, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            var result = plans.Create(body);
            return result.Ok
                ? Results.Json(new { success = true, data = new { document = result.Value!.Document, totalSeconds = result.Value.Plan.TotalSeconds } })
                : ToResult(result);
        }).DisableAntiforgery();

        // ---- voices (Vbee through TodoX's account)
        group.MapGet("/voices", async (HttpContext http, EditVideoLicenseService licenses, EditVideoVoiceService voices, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            var (list, points, balance) = await voices.ListAsync(auth.Value.License.CustomerId, ct);
            return Results.Json(new { success = true, data = new { voices = list, pointsPerJob = points, balance } });
        });

        group.MapPost("/tts", async (HttpContext http, VoiceRequest body, EditVideoLicenseService licenses, EditVideoVoiceService voices, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            return ToResult(await voices.SubmitAsync(auth.Value.License, auth.Value.Device, body.Text, body.VoiceCode, body.SessionId, ct));
        }).DisableAntiforgery();

        group.MapGet("/tts/{jobId:guid}", async (Guid jobId, HttpContext http, EditVideoLicenseService licenses, EditVideoVoiceService voices, CancellationToken ct) =>
        {
            var auth = await licenses.AuthenticateDeviceAsync(BearerToken(http.Request), DeviceHash(http.Request), ClientIp(http), ct);
            if (!auth.Ok)
            {
                return ToResult(auth);
            }

            return ToResult(await voices.GetAsync(jobId, auth.Value.License.Id, ct));
        });

        group.MapPost("/vbee-callback", async (HttpRequest request, Guid job, IVbeeRuntimeConfigProvider config, IVbeeVoiceClient vbee, EditVideoVoiceService voices, TenantContext tenant, CancellationToken ct) =>
        {
            await tenant.EnsureLoadedAsync(ct);
            var options = await config.GetAsync(ct);
            if (SceneAudioEndpoints.GetCallbackAuthorizationStatus(request, options.CallbackSecret) != VbeeCallbackAuthorizationStatus.Authorized)
            {
                return Results.Unauthorized();
            }

            await voices.HandleCallbackAsync(job, await vbee.ParseCallbackAsync(request, ct), ct);
            return Results.Json(new { success = true });
        }).DisableAntiforgery();

        // ---- releases & downloads (public: the installer runs before activation)
        group.MapGet("/releases/latest", (EditVideoReleaseService releases) =>
            releases.GetLatest() is { } manifest
                ? Results.Json(new { success = true, data = manifest })
                : Results.Json(new { success = false, code = "no_release", message = "No release published yet." }, statusCode: StatusCodes.Status404NotFound));

        app.MapGet("/downloads/editvideo/{name}", (string name, EditVideoReleaseService releases) =>
            releases.ResolveFile(name) is { } path
                ? Results.File(path, name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "application/vnd.microsoft.portable-executable" : "application/octet-stream", name, enableRangeProcessing: true)
                : Results.NotFound());
    }

    internal static int StatusFor(string code) => code switch
    {
        EditVideoErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        EditVideoErrorCodes.InvalidToken or EditVideoErrorCodes.DeviceMismatch => StatusCodes.Status401Unauthorized,
        EditVideoErrorCodes.InvalidLicense or "not_found" or "voice_not_found" => StatusCodes.Status404NotFound,
        EditVideoErrorCodes.DeviceLimitReached => StatusCodes.Status409Conflict,
        "insufficient_points" => StatusCodes.Status402PaymentRequired,
        EditVideoErrorCodes.SigningUnavailable or "voice_service_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "voice_failed" => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status403Forbidden
    };

    private static IResult ToResult<T>(EditVideoResult<T> result) =>
        result.Ok
            ? Results.Json(new { success = true, data = result.Value })
            : Results.Json(new { success = false, code = result.ErrorCode, message = result.Message, detail = result.Detail },
                statusCode: StatusFor(result.ErrorCode!));

    private static IResult Error(string code, string message) =>
        Results.Json(new { success = false, code, message }, statusCode: StatusFor(code));

    private static string? BearerToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : null;
    }

    private static string? DeviceHash(HttpRequest request) => request.Headers[DeviceHashHeader].FirstOrDefault();

    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
