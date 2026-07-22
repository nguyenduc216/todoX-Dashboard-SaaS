using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.DanceSell;

public static class DanceSellPhase1Endpoints
{
    public static void MapDanceSellPhase1Endpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/kie/test-motion-control", CreateTestMotionControlAsync)
            .DisableAntiforgery();

        app.MapGet("/api/admin/kie/test-motion-control/{jobId:guid}", GetTestMotionControlAsync);

        app.MapPost("/api/providers/kie/callback", HandleCallbackAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> CreateTestMotionControlAsync(
        DanceSellAdminTestRequest body,
        AuthStateService auth,
        TenantContext tenant,
        IDanceSellRepository danceSellJobs,
        IRenderJobService renderJobs,
        IKiePayloadBuilder payloadBuilder,
        IOptionsMonitor<KieOptions> options,
        CancellationToken ct)
    {
        if (!IsAdmin(auth.CurrentUser))
        {
            return Results.Json(new { success = false, message = "Admin permission required." }, statusCode: StatusCodes.Status403Forbidden);
        }

        await tenant.EnsureLoadedAsync(ct);
        KieMotionControlRequest preview;
        try
        {
            preview = payloadBuilder.BuildMotionControlRequest(new KieMotionControlBuildRequest
            {
                Prompt = body.Prompt,
                CharacterImageUrl = body.ImageUrl,
                MotionVideoUrl = body.VideoUrl,
                Mode = body.Mode,
                CharacterOrientation = body.CharacterOrientation
            });
        }
        catch (KieProviderException ex)
        {
            return Results.Json(new { success = false, errorCode = ex.ErrorCode, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
        }

        var user = auth.CurrentUser!;
        var logicalRequestId = $"dance-sell-phase1-{Guid.NewGuid():N}";
        var danceJob = await danceSellJobs.CreateAsync(new DanceSellJobCreateRequest
        {
            TenantId = tenant.TenantId,
            CustomerId = user.CustomerId,
            UserId = user.UserId,
            LogicalRequestId = logicalRequestId,
            Prompt = body.Prompt,
            CharacterImageUrl = body.ImageUrl,
            MotionVideoUrl = body.VideoUrl,
            Mode = body.Mode,
            CharacterOrientation = body.CharacterOrientation,
            ProviderCode = DanceSellConstants.ProviderCode,
            ProviderModel = options.CurrentValue.MotionControlModel
        }, ct);

        var renderJob = await renderJobs.EnqueueAsync(new RenderJobCreateModel
        {
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            JobType = RenderJobTypes.DanceSell,
            Priority = 50,
            Input = new DanceSellRenderInput
            {
                DanceSellJobId = danceJob.Id,
                LogicalRequestId = logicalRequestId
            },
            Prompt = new { prompt = body.Prompt },
            References = new[] { body.ImageUrl, body.VideoUrl },
            LogCode = null,
            ProviderCode = DanceSellConstants.ProviderCode,
            ModelCode = options.CurrentValue.MotionControlModel,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired,
            MaxAttempts = Math.Max(3, options.CurrentValue.MaxPollCount + options.CurrentValue.SubmitMaxRetry + 5)
        }, ct);

        await danceSellJobs.SetRenderJobIdAsync(danceJob.Id, renderJob.Id, ct);

        return Results.Json(new
        {
            danceSellJobId = danceJob.Id,
            renderJobId = renderJob.Id,
            logicalRequestId,
            status = "queued",
            requestPreview = new
            {
                preview.Model,
                input = preview.Input
            }
        });
    }

    private static async Task<IResult> GetTestMotionControlAsync(
        Guid jobId,
        AuthStateService auth,
        IDanceSellRepository danceSellJobs,
        IRenderJobService renderJobs,
        CancellationToken ct)
    {
        if (!IsAdmin(auth.CurrentUser))
        {
            return Results.Json(new { success = false, message = "Admin permission required." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var job = await danceSellJobs.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            return Results.NotFound(new { success = false, message = "Dance Sell test job not found." });
        }

        RenderJobDto? render = job.RenderJobId is Guid renderJobId ? await renderJobs.GetAsync(renderJobId, ct) : null;
        return Results.Json(new
        {
            job.Id,
            job.RenderJobId,
            job.LogicalRequestId,
            job.Status,
            job.ProviderTaskId,
            job.ProviderStatus,
            job.ResultVideoUrl,
            job.PollCount,
            job.NextPollAt,
            job.ErrorCode,
            job.ErrorMessage,
            renderStatus = render?.Status,
            renderErrorCode = render?.ErrorCode,
            renderErrorMessage = render?.ErrorMessage
        });
    }

    private static async Task<IResult> HandleCallbackAsync(
        HttpRequest request,
        IKieClient client,
        IDanceSellRepository danceSellJobs,
        IDanceSellCompletionService completion,
        IOptionsMonitor<KieOptions> options,
        CancellationToken ct)
    {
        var authorization = GetCallbackAuthorizationStatus(request, options.CurrentValue.CallbackSecret);
        if (authorization == KieCallbackAuthorizationStatus.NotConfigured)
        {
            return Results.Json(new { success = false, message = "KIE callback secret is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        if (authorization is KieCallbackAuthorizationStatus.MissingSecret or KieCallbackAuthorizationStatus.InvalidSecret)
        {
            return Results.Json(new { success = false, message = "Invalid callback secret." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var raw = await new StreamReader(request.Body).ReadToEndAsync(ct);
        KieCallbackResult callback;
        try
        {
            callback = client.ParseCallback(raw);
        }
        catch (KieProviderException ex)
        {
            return Results.Json(new { success = false, errorCode = ex.ErrorCode, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(callback.TaskId))
        {
            return Results.Json(new { success = false, errorCode = KieErrorCodes.CallbackInvalid, message = "Missing taskId." }, statusCode: StatusCodes.Status400BadRequest);
        }

        var job = await danceSellJobs.GetByProviderTaskIdAsync(callback.TaskId, ct);
        if (job is null)
        {
            return Results.NotFound(new { success = false, message = "Provider task was not found." });
        }

        var resultUrl = callback.Status == KieTaskStatuses.Completed ? callback.ResultUrls.FirstOrDefault() : null;
        var errorCode = callback.Status == KieTaskStatuses.Failed
            ? (callback.FailCode ?? KieErrorCodes.TaskFailed)
            : callback.ResultParseError is not null ? KieErrorCodes.ResultJsonInvalid : null;
        var errorMessage = callback.Status == KieTaskStatuses.Failed
            ? (callback.FailMsg ?? "KIE task failed.")
            : callback.ResultParseError;

        if (resultUrl is not null)
        {
            await completion.CompleteAsync(new DanceSellCompletionRequest
            {
                DanceJob = job,
                ProviderTaskId = callback.TaskId,
                ProviderStatus = callback.ProviderState ?? callback.Status,
                ResponseJson = raw,
                ResultVideoUrl = resultUrl,
                ResultUrlCount = callback.ResultUrls.Count,
                CreditsConsumed = callback.CreditsConsumed,
                Source = "callback"
            }, ct);
        }
        else if (errorCode is not null)
        {
            await completion.FailAsync(new DanceSellFailureRequest
            {
                DanceJob = job,
                ProviderTaskId = callback.TaskId,
                ProviderStatus = callback.ProviderState ?? callback.Status,
                ResponseJson = raw,
                Status = DanceSellJobStatuses.Failed,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Permanent = true,
                Source = "callback"
            }, ct);
        }

        await danceSellJobs.UpdateCallbackAsync(callback.TaskId, KieJsonRedactor.Redact(raw) ?? "{}", callback.ProviderState ?? callback.Status, resultUrl, errorCode, errorMessage, ct);

        return Results.Json(new { success = true, taskId = callback.TaskId, status = callback.Status });
    }

    private static bool IsAdmin(CurrentUserSession? user)
        => user?.IsAuthenticated == true
           && (user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator);

    public static KieCallbackAuthorizationStatus GetCallbackAuthorizationStatus(HttpRequest request, string? configuredSecret)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return KieCallbackAuthorizationStatus.NotConfigured;
        }

        var provided = request.Headers["X-KIE-CALLBACK-SECRET"].FirstOrDefault()
                       ?? request.Query["secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return KieCallbackAuthorizationStatus.MissingSecret;
        }

        return string.Equals(provided, configuredSecret, StringComparison.Ordinal)
            ? KieCallbackAuthorizationStatus.Authorized
            : KieCallbackAuthorizationStatus.InvalidSecret;
    }

    public static bool IsCallbackConfigured(string? configuredSecret)
        => !string.IsNullOrWhiteSpace(configuredSecret);
}

public enum KieCallbackAuthorizationStatus
{
    NotConfigured,
    MissingSecret,
    InvalidSecret,
    Authorized
}
