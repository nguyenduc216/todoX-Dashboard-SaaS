using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TodoX.Web.Models;

namespace TodoX.Web.Services.DanceSell;

public static class DanceSellPhase2Endpoints
{
    public static void MapDanceSellPhase2Endpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dance-sell");

        group.MapGet("/capabilities", (IDanceSellPhase2Service service) => Results.Json(service.GetCapability()));
        group.MapGet("/providers", GetProvidersAsync);
        group.MapGet("/providers/{routeId:guid}/capability", GetProviderCapabilityAsync);
        group.MapGet("/jobs", ListJobsAsync);
        group.MapPost("/jobs", CreateJobAsync).DisableAntiforgery();
        group.MapGet("/jobs/{id:guid}", GetJobAsync);
        group.MapGet("/jobs/{id:guid}/download", DownloadAsync);
        group.MapGet("/jobs/{id:guid}/reference/download", DownloadReferenceAsync);
        group.MapPut("/jobs/{id:guid}", UpdateBusinessAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/character", UploadCharacterAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/product", UploadProductAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/direct-reference", UploadDirectReferenceAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/motion/upload", UploadMotionAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/motion/tiktok", StageTikTokAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/reference/generate", GenerateReferenceAsync).DisableAntiforgery();
        group.MapGet("/jobs/{id:guid}/reference/versions", ListReferenceVersionsAsync);
        group.MapPost("/jobs/{id:guid}/reference/{versionId:guid}/approve", ApproveReferenceAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/reference/unapprove", UnapproveReferenceAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/render", QueueRenderAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/retry", RetryAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/retry-reference", GenerateReferenceAsync).DisableAntiforgery();
        group.MapPost("/jobs/{id:guid}/retry-motion", RetryAsync).DisableAntiforgery();

        var admin = app.MapGroup("/api/admin").RequireTodoXAdmin();
        admin.MapGet("/ai-operation-logs", SearchOperationLogsAsync);
        admin.MapGet("/ai-operation-logs/{id:guid}", GetOperationLogAsync);
        admin.MapPost("/ai-operation-logs/{id:guid}/refund", RefundOperationAsync).DisableAntiforgery();
        admin.MapPost("/ai-operation-logs/{id:guid}/retry-refund", RetryRefundAsync).DisableAntiforgery();
        admin.MapPost("/ai-operation-logs/{id:guid}/retry-charge", RetryChargeAsync).DisableAntiforgery();
        admin.MapGet("/ai-provider-accounts", ListProviderAccountsAsync);
        admin.MapPost("/dance-sell/jobs/{id:guid}/reference-comparison/run", RunReferenceComparisonAsync).DisableAntiforgery();
        admin.MapPost("/dance-sell/jobs/{id:guid}/reference-comparison/{versionId:guid}/poll", PollReferenceComparisonAsync).DisableAntiforgery();
        admin.MapPost("/dance-sell/jobs/{id:guid}/reference-comparison/{versionId:guid}/score", ScoreReferenceComparisonAsync).DisableAntiforgery();
    }

    private static async Task<IResult> GetProvidersAsync(string operationType, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.GetProvidersAsync(operationType, user, ct));

    private static async Task<IResult> GetProviderCapabilityAsync(Guid routeId, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.GetProviderCapabilityAsync(routeId, user, ct));

    private static async Task<IResult> ListJobsAsync(AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.ListAsync(user, 50, 0, ct));

    private static async Task<IResult> CreateJobAsync(DanceSellCreateJobRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.CreateJobAsync(request, user, ct));

    private static async Task<IResult> GetJobAsync(Guid id, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.GetAsync(id, user, ct));

    private static async Task<IResult> DownloadAsync(
        Guid id,
        AuthStateService auth,
        IDanceSellPhase2Service service,
        IHttpClientFactory httpClients,
        CancellationToken ct)
        => await ExecuteAsync(auth, async user =>
        {
            var job = await service.GetAsync(id, user, ct);
            if (!string.Equals(job.Status, DanceSellJobStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(job.ResultVideoUrl))
            {
                throw new InvalidOperationException("DANCE_SELL_RESULT_NOT_READY");
            }

            if (!Uri.TryCreate(job.ResultVideoUrl, UriKind.Absolute, out var resultUri))
            {
                throw new InvalidOperationException("DANCE_SELL_RESULT_URL_INVALID");
            }

            await EnsurePublicHttpsUrlAsync(resultUri, ct);
            var client = httpClients.CreateClient("DanceSellDownload");
            using var request = new HttpRequestMessage(HttpMethod.Get, resultUri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new InvalidOperationException("DANCE_SELL_RESULT_DOWNLOAD_FAILED");
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return Results.Stream(
                stream,
                "video/mp4",
                $"todox-video-thoi-trang-{id:N}.mp4",
                enableRangeProcessing: true);
        });

    private static async Task<IResult> DownloadReferenceAsync(
        Guid id,
        AuthStateService auth,
        IDanceSellPhase2Service service,
        IHttpClientFactory httpClients,
        CancellationToken ct)
        => await ExecuteAsync(auth, async user =>
        {
            var job = await service.GetAsync(id, user, ct);
            if (!string.Equals(job.PreparedReferenceStatus, DanceSellReferenceStatuses.Approved, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(job.PreparedReferenceUrl))
            {
                throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
            }

            if (!Uri.TryCreate(job.PreparedReferenceUrl, UriKind.Absolute, out var referenceUri))
            {
                throw new InvalidOperationException("DANCE_SELL_REFERENCE_URL_INVALID");
            }

            await EnsurePublicHttpsUrlAsync(referenceUri, ct);
            var client = httpClients.CreateClient("DanceSellDownload");
            using var request = new HttpRequestMessage(HttpMethod.Get, referenceUri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new InvalidOperationException("DANCE_SELL_REFERENCE_DOWNLOAD_FAILED");
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return Results.Stream(stream, "image/jpeg", $"todox-anh-tham-chieu-{id:N}.jpg");
        });

    private static async Task<IResult> UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.UpdateBusinessAsync(id, request, user, ct));

    private static async Task<IResult> UploadCharacterAsync(Guid id, HttpRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteFileAsync(request, auth, (user, file, bytes) => service.UploadCharacterAsync(id, bytes, file.FileName, file.ContentType, user, ct), ct);

    private static async Task<IResult> UploadProductAsync(Guid id, HttpRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteFileAsync(request, auth, (user, file, bytes) => service.UploadProductAsync(id, bytes, file.FileName, file.ContentType, user, ct), ct);

    private static async Task<IResult> UploadDirectReferenceAsync(Guid id, HttpRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteFileAsync(request, auth, (user, file, bytes) => service.UploadDirectReferenceAsync(id, bytes, file.FileName, file.ContentType, user, ct), ct);

    private static async Task<IResult> UploadMotionAsync(Guid id, HttpRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteFileAsync(request, auth, (user, file, bytes) => service.UploadMotionAsync(id, bytes, file.FileName, file.ContentType, user, ct), ct);

    private static async Task<IResult> StageTikTokAsync(Guid id, DanceSellTikTokStageRequest request, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.StageTikTokAsync(id, request.Url, user, ct));

    private static async Task<IResult> GenerateReferenceAsync(Guid id, AuthStateService auth, IDanceSellReferenceImageService service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.GenerateAsync(id, user, ct));

    private static async Task<IResult> ListReferenceVersionsAsync(Guid id, AuthStateService auth, IDanceSellRepository repo, IDanceSellPhase2Service jobs, CancellationToken ct)
        => await ExecuteAsync(auth, async user =>
        {
            await jobs.GetAsync(id, user, ct);
            return await repo.ListReferenceVersionsAsync(id, ct);
        });

    private static async Task<IResult> ApproveReferenceAsync(Guid id, Guid versionId, AuthStateService auth, IDanceSellReferenceImageService service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.ApproveAsync(id, versionId, user, ct));

    private static async Task<IResult> UnapproveReferenceAsync(Guid id, AuthStateService auth, IDanceSellReferenceImageService service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.UnapproveAsync(id, user, ct));

    private static async Task<IResult> QueueRenderAsync(Guid id, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.QueueRenderAsync(id, user, ct));

    private static async Task<IResult> RetryAsync(Guid id, AuthStateService auth, IDanceSellPhase2Service service, CancellationToken ct)
        => await ExecuteAsync(auth, user => service.RetryAsync(id, user, ct));

    private static async Task<IResult> SearchOperationLogsAsync(
        [AsParameters] DanceSellOperationLogFilter filter,
        AuthStateService auth,
        IDanceSellOperationRepository logs,
        CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => logs.SearchLogsAsync(filter, ct));

    private static async Task<IResult> GetOperationLogAsync(Guid id, AuthStateService auth, IDanceSellOperationRepository logs, CancellationToken ct)
        => await ExecuteAdminAsync(auth, async () =>
        {
            var detail = await logs.GetLogDetailAsync(id, ct);
            if (detail is null) throw new InvalidOperationException("DANCE_SELL_OPERATION_NOT_FOUND");
            return detail;
        });

    private static async Task<IResult> RefundOperationAsync(Guid id, DanceSellRefundRequest request, AuthStateService auth, IAiOperationBillingService billing, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => billing.RefundAsync(id, request.Points, request.Reason, auth.CurrentUser?.UserId, ct));

    private static async Task<IResult> RetryRefundAsync(Guid id, DanceSellReasonRequest request, AuthStateService auth, IAiOperationBillingService billing, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => billing.RetryRefundAsync(id, request.Reason, auth.CurrentUser?.UserId, ct));

    private static async Task<IResult> RetryChargeAsync(Guid id, DanceSellReasonRequest request, AuthStateService auth, IAiOperationBillingService billing, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => billing.RetryChargeAsync(id, request.Reason, auth.CurrentUser?.UserId, ct));

    private static Task<IResult> ListProviderAccountsAsync(AuthStateService auth)
        => ExecuteAdminAsync(auth, () => Task.FromResult<IReadOnlyList<ProviderAccountDto>>(Array.Empty<ProviderAccountDto>()));

    private static async Task<IResult> RunReferenceComparisonAsync(Guid id, AuthStateService auth, IDanceSellReferenceComparisonService service, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => service.RunAsync(id, auth.CurrentUser!, ct));

    private static async Task<IResult> PollReferenceComparisonAsync(Guid id, Guid versionId, AuthStateService auth, IDanceSellReferenceComparisonService service, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => service.PollAsync(id, versionId, auth.CurrentUser!, ct));

    private static async Task<IResult> ScoreReferenceComparisonAsync(Guid id, Guid versionId, DanceSellReferenceComparisonScoreRequest request, AuthStateService auth, IDanceSellReferenceComparisonService service, CancellationToken ct)
        => await ExecuteAdminAsync(auth, () => service.ScoreAsync(id, versionId, request, auth.CurrentUser!, ct));

    private static async Task<IResult> ExecuteFileAsync<T>(HttpRequest request, AuthStateService auth, Func<CurrentUserSession, IFormFile, byte[], Task<T>> action, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.Json(new { success = false, errorCode = "DANCE_SELL_INVALID_UPLOAD", message = "Expected multipart/form-data." }, statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.Json(new { success = false, errorCode = "DANCE_SELL_INVALID_UPLOAD", message = "Missing file." }, statusCode: StatusCodes.Status400BadRequest);
        }

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return await ExecuteAsync(auth, user => action(user, file, ms.ToArray()));
    }

    private static async Task<IResult> ExecuteAsync<T>(AuthStateService auth, Func<CurrentUserSession, Task<T>> action)
    {
        var user = auth.CurrentUser;
        if (user?.IsAuthenticated != true)
        {
            return Results.Json(new { success = false, errorCode = "DANCE_SELL_UNAUTHORIZED", message = "Authentication required." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            var data = await action(user);
            return Results.Json(new { success = true, data });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message switch
            {
                "DANCE_SELL_UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                "DANCE_SELL_NOT_FOUND" => StatusCodes.Status404NotFound,
                "DANCE_SELL_RESULT_NOT_READY" => StatusCodes.Status409Conflict,
                "DANCE_SELL_RESULT_URL_INVALID" => StatusCodes.Status400BadRequest,
                "DANCE_SELL_RESULT_DOWNLOAD_FAILED" => StatusCodes.Status502BadGateway,
                "DANCE_SELL_REFERENCE_NOT_READY" => StatusCodes.Status409Conflict,
                "DANCE_SELL_REFERENCE_URL_INVALID" => StatusCodes.Status400BadRequest,
                "DANCE_SELL_REFERENCE_DOWNLOAD_FAILED" => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { success = false, errorCode = ex.Message, message = ex.Message }, statusCode: status);
        }
    }

    private static async Task<IResult> ExecuteAdminAsync<T>(AuthStateService auth, Func<Task<T>> action)
    {
        var user = auth.CurrentUser;
        if (user?.IsAuthenticated != true || !DanceSellSecurity.IsAdmin(user))
        {
            return Results.Json(new { success = false, errorCode = "DANCE_SELL_ADMIN_REQUIRED", message = "Admin permission required." }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var data = await action();
            return Results.Json(new { success = true, data });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message switch
            {
                "DANCE_SELL_OPERATION_NOT_FOUND" => StatusCodes.Status404NotFound,
                "DANCE_SELL_BILLING_DISABLED" => StatusCodes.Status409Conflict,
                "DANCE_SELL_DATABASE_SCHEMA_NOT_READY" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { success = false, errorCode = ex.Message, message = ex.Message }, statusCode: status);
        }
    }

    private static async Task EnsurePublicHttpsUrlAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DANCE_SELL_RESULT_URL_INVALID");
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.DnsSafeHost, out var address)
                ? new[] { address }
                : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("DANCE_SELL_RESULT_URL_INVALID");
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocalAddress))
        {
            throw new InvalidOperationException("DANCE_SELL_RESULT_URL_INVALID");
        }
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 0
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        return false;
    }
}

public sealed class DanceSellRefundRequest
{
    public decimal Points { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class DanceSellReasonRequest
{
    public string Reason { get; set; } = string.Empty;
}
