namespace TodoX.Web.Services.Platform;

/// <summary>
/// Versioned HTTP facade over the TodoX Core application layer. This layer is intentionally thin:
/// it authenticates/resolves the caller, maps transport details such as Idempotency-Key, then calls
/// transport-neutral application services.
/// </summary>
public static class CoreApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapTodoXCoreApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");

        group.MapGet("/services", async (
            HttpRequest httpRequest,
            ICoreApiCallerResolver callers,
            ICoreServiceCatalogService catalog,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var services = await catalog.ListAsync(ct);
            return Results.Ok(new { success = true, data = services });
        });

        group.MapGet("/services/{serviceCode}", async (
            string serviceCode,
            HttpRequest httpRequest,
            ICoreApiCallerResolver callers,
            ICoreServiceCatalogService catalog,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var service = await catalog.GetByCodeAsync(serviceCode, ct);
            return service is null
                ? Results.NotFound(new { success = false, message = "Service not found." })
                : Results.Ok(new { success = true, data = service });
        });

        group.MapPost("/jobs", async (
            HttpRequest httpRequest,
            CoreCreateJobRequest body,
            ICoreApiCallerResolver callers,
            ICoreJobApplicationService jobs,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var headerKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
            var request = new CoreCreateJobRequest
            {
                ServiceCode = body.ServiceCode,
                Input = body.Input,
                Prompt = body.Prompt,
                References = body.References,
                Priority = body.Priority,
                IdempotencyKey = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? headerKey : body.IdempotencyKey
            };

            try
            {
                var job = await jobs.CreateAsync(caller, request, ct);
                return Results.Ok(new { success = true, data = job });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (CoreInsufficientBalanceException ex)
            {
                return Results.Json(
                    new { success = false, message = ex.Message, jobId = ex.JobId },
                    statusCode: StatusCodes.Status402PaymentRequired);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapGet("/jobs", async (
            HttpRequest httpRequest,
            ICoreApiCallerResolver callers,
            ICoreJobApplicationService jobs,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var query = httpRequest.Query;
            var page = int.TryParse(query["page"].FirstOrDefault(), out var parsedPage) ? parsedPage : 1;
            var pageSize = int.TryParse(query["pageSize"].FirstOrDefault(), out var parsedPageSize) ? parsedPageSize : 20;
            try
            {
                var result = await jobs.ListAsync(caller, new CoreJobListRequest(
                    page,
                    pageSize,
                    query["status"].FirstOrDefault(),
                    query["serviceCode"].FirstOrDefault()), ct);
                return Results.Ok(new { success = true, data = result });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        group.MapGet("/jobs/{jobId:guid}", async (
            Guid jobId,
            HttpRequest httpRequest,
            ICoreApiCallerResolver callers,
            ICoreJobApplicationService jobs,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var job = await jobs.GetAsync(caller, jobId, ct);
            return job is null
                ? Results.NotFound(new { success = false, message = "Job not found." })
                : Results.Ok(new { success = true, data = job });
        });

        group.MapPost("/jobs/{jobId:guid}/cancel", async (
            Guid jobId,
            HttpRequest httpRequest,
            CoreCancelJobRequest? body,
            ICoreApiCallerResolver callers,
            ICoreJobApplicationService jobs,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var job = await jobs.CancelAsync(caller, jobId, body?.Reason, ct);
                return Results.Ok(new { success = true, data = job });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapPost("/jobs/{jobId:guid}/retry", async (
            Guid jobId,
            HttpRequest httpRequest,
            CoreRetryJobRequest? body,
            ICoreApiCallerResolver callers,
            ICoreJobApplicationService jobs,
            CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(httpRequest, ct);
            if (caller is null)
            {
                return Results.Unauthorized();
            }

            var headerKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
            var request = new CoreRetryJobRequest(
                string.IsNullOrWhiteSpace(body?.IdempotencyKey) ? headerKey : body.IdempotencyKey);

            try
            {
                var job = await jobs.RetryAsync(caller, jobId, request, ct);
                return Results.Ok(new { success = true, data = job });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
        }).DisableAntiforgery();

        return endpoints;
    }
}
