namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// Device-facing API for the local TodoX.EditVideoTool exe. Only small JSON travels here — video files never do.
/// Auth: activation uses the license key; every other call uses the short-lived access token + device hash.
/// </summary>
public static class EditVideoEndpoints
{
    public const string DeviceHashHeader = "X-TodoX-Device";

    public static void MapEditVideoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/editvideo/v1");

        group.MapGet("/public-key", (EditVideoLicenseService licenses) =>
            licenses.PublicKeyPem is { } pem
                ? Results.Text(pem, "application/x-pem-file")
                : Error(EditVideoErrorCodes.SigningUnavailable, "License service is not configured."));

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
    }

    internal static int StatusFor(string code) => code switch
    {
        EditVideoErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        EditVideoErrorCodes.InvalidToken or EditVideoErrorCodes.DeviceMismatch => StatusCodes.Status401Unauthorized,
        EditVideoErrorCodes.InvalidLicense => StatusCodes.Status404NotFound,
        EditVideoErrorCodes.DeviceLimitReached => StatusCodes.Status409Conflict,
        EditVideoErrorCodes.SigningUnavailable => StatusCodes.Status503ServiceUnavailable,
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
