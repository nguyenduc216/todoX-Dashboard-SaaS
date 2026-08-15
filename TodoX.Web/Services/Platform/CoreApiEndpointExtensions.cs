using System.Text.Json;

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
            ICoreServiceCatalogService catalog,
            CancellationToken ct) =>
        {
            var services = await catalog.ListAsync(ct);
            return Results.Ok(new { success = true, data = services });
        });

        group.MapGet("/services/{serviceCode}", async (
            string serviceCode,
            ICoreServiceCatalogService catalog,
            CancellationToken ct) =>
        {
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
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { success = false, message = ex.Message });
            }
        }).DisableAntiforgery();

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

        return endpoints;
    }
}
