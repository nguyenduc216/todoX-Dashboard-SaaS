using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.DanceSell;

public sealed class DanceSellRenderHandler : IRenderJobHandler
{
    private readonly IDanceSellRepository _repo;
    private readonly IKiePayloadBuilder _payloadBuilder;
    private readonly IKieClient _client;
    private readonly IKieRateLimiter _rateLimiter;
    private readonly IRenderJobService _renderJobs;
    private readonly IDanceSellCompletionService _completion;
    private readonly IAiProviderService _providers;
    private readonly IOptionsMonitor<KieOptions> _options;
    private readonly ILogger<DanceSellRenderHandler> _logger;

    public string JobType => RenderJobTypes.DanceSell;

    public DanceSellRenderHandler(
        IDanceSellRepository repo,
        IKiePayloadBuilder payloadBuilder,
        IKieClient client,
        IKieRateLimiter rateLimiter,
        IRenderJobService renderJobs,
        IDanceSellCompletionService completion,
        IAiProviderService providers,
        IOptionsMonitor<KieOptions> options,
        ILogger<DanceSellRenderHandler> logger)
    {
        _repo = repo;
        _payloadBuilder = payloadBuilder;
        _client = client;
        _rateLimiter = rateLimiter;
        _renderJobs = renderJobs;
        _completion = completion;
        _providers = providers;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<DanceSellRenderInput>(job.InputJson, KieJson.Options)
            ?? throw new RenderJobTerminalFailureException("Dance Sell render input invalid.");
        var danceJob = await _repo.GetByIdAsync(input.DanceSellJobId, ct)
            ?? throw new RenderJobTerminalFailureException("Dance Sell job not found.");

        if (danceJob.Status is DanceSellJobStatuses.Completed)
        {
            return;
        }

        if (danceJob.Status is DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)
        {
            throw new RenderJobTerminalFailureException(danceJob.ErrorMessage ?? "Dance Sell job already failed.");
        }

        if (string.IsNullOrWhiteSpace(danceJob.ProviderTaskId))
        {
            await SubmitAsync(job, danceJob, ct);
            return;
        }

        await PollAsync(job, danceJob, ct);
    }

    private async Task SubmitAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, CancellationToken ct)
    {
        var permit = await _rateLimiter.AcquireSubmitPermitAsync(DanceSellConstants.ProviderCode, ct);
        if (!permit.Allowed)
        {
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, permit.RetryAfter, KieErrorCodes.RateLimited, "KIE submit rate limit reached.", ct);
            throw new RenderJobDeferredException("KIE submit deferred by local rate limiter.");
        }

        KieMotionControlRequest payload;
        try
        {
            payload = _payloadBuilder.BuildMotionControlRequest(new KieMotionControlBuildRequest
            {
                Prompt = danceJob.Prompt,
                CharacterImageUrl = danceJob.CharacterImageUrl,
                MotionVideoUrl = danceJob.MotionVideoUrl,
                Mode = danceJob.Mode,
                CharacterOrientation = danceJob.CharacterOrientation
            });
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.Unknown, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }

        var requestJson = KieJsonRedactor.Redact(JsonSerializer.Serialize(payload, KieJson.Options)) ?? "{}";
        var sw = Stopwatch.StartNew();
        try
        {
            var submitted = await _client.CreateTaskAsync(payload, ct);
            sw.Stop();
            var responseJson = KieJsonRedactor.Redact(submitted.RawResponse) ?? "{}";
            await _repo.UpdateSubmittedAsync(danceJob.Id, requestJson, submitted.TaskId!, responseJson, ct);
            await _renderJobs.AddEventAsync(renderJob.Id, "KIE_TASK_SUBMITTED", "KIE Motion Control task submitted.",
                new { danceSellJobId = danceJob.Id, taskId = submitted.TaskId, durationMs = sw.ElapsedMilliseconds },
                ct: ct);
            await LogUsageAsync(danceJob, renderJob, "submitted", submitted.TaskId, "submitted", null, null, ct);
            await ScheduleNextPollAsync(renderJob, "KIE task submitted; polling scheduled.", ct);
        }
        catch (KieProviderException ex) when (ex.ErrorCode == KieErrorCodes.RateLimited)
        {
            await _repo.UpdateFailedAsync(danceJob.Id, DanceSellJobStatuses.Queued, null, BuildErrorJson(ex), KieErrorCodes.RateLimited, ex.Message, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? _options.CurrentValue.PollInterval, KieErrorCodes.RateLimited, ex.Message, ct);
            throw new RenderJobDeferredException("KIE submit deferred after HTTP 429.");
        }
        catch (KieProviderException ex) when (ex.IsTransient)
        {
            await _repo.UpdateFailedAsync(danceJob.Id, DanceSellJobStatuses.Queued, null, BuildErrorJson(ex), ex.ErrorCode ?? KieErrorCodes.ProviderUnavailable, ex.Message, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? TimeSpan.FromSeconds(30), ex.ErrorCode ?? KieErrorCodes.ProviderUnavailable, ex.Message, ct);
            throw new RenderJobDeferredException("KIE transient submit failure scheduled for retry.");
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.Unknown, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
    }

    private async Task PollAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, CancellationToken ct)
    {
        if (danceJob.PollCount >= Math.Max(1, _options.CurrentValue.MaxPollCount))
        {
            await FailAsync(renderJob, danceJob, KieErrorCodes.PollTimeout, "KIE poll max count reached.", danceJob.PollResponseJson, permanent: true, ct, DanceSellJobStatuses.Timeout);
            throw new RenderJobTerminalFailureException("KIE poll max count reached.");
        }

        try
        {
            var detail = await _client.GetTaskDetailAsync(danceJob.ProviderTaskId!, ct);
            var responseJson = KieJsonRedactor.Redact(detail.RawResponse) ?? "{}";
            if (detail.Status == KieTaskStatuses.Completed)
            {
                if (!string.IsNullOrWhiteSpace(detail.ResultParseError))
                {
                    await FailAsync(renderJob, danceJob, KieErrorCodes.ResultJsonInvalid, detail.ResultParseError, responseJson, permanent: true, ct);
                    throw new RenderJobTerminalFailureException(detail.ResultParseError);
                }

                var resultUrl = detail.ResultUrls.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(resultUrl))
                {
                    await FailAsync(renderJob, danceJob, KieErrorCodes.ResultUrlMissing, "KIE resultUrls is empty.", responseJson, permanent: true, ct);
                    throw new RenderJobTerminalFailureException("KIE resultUrls is empty.");
                }

                await _completion.CompleteAsync(new DanceSellCompletionRequest
                {
                    DanceJob = danceJob,
                    ProviderTaskId = danceJob.ProviderTaskId,
                    ProviderStatus = detail.ProviderState ?? detail.Status,
                    ResponseJson = responseJson,
                    ResultVideoUrl = resultUrl,
                    ResultUrlCount = detail.ResultUrls.Count,
                    Source = "poll"
                }, ct);
                return;
            }

            if (detail.Status == KieTaskStatuses.Failed)
            {
                var error = string.IsNullOrWhiteSpace(detail.FailMsg) ? "KIE task failed." : detail.FailMsg;
                await FailAsync(renderJob, danceJob, KieErrorCodes.TaskFailed, error, responseJson, permanent: true, ct);
                throw new RenderJobTerminalFailureException(error);
            }

            var nextPoll = DateTime.UtcNow.Add(_options.CurrentValue.PollInterval);
            await _repo.UpdatePollingAsync(danceJob.Id, detail.ProviderState ?? detail.Status, responseJson, danceJob.PollCount + 1, nextPoll, ct);
            await _renderJobs.AddEventAsync(renderJob.Id, "KIE_TASK_POLLING", "KIE task is still running.",
                new { danceSellJobId = danceJob.Id, danceJob.ProviderTaskId, detail.ProviderState, detail.Status, pollCount = danceJob.PollCount + 1 }, ct: ct);
            await LogUsageAsync(danceJob, renderJob, "processing", danceJob.ProviderTaskId, detail.ProviderState, null, null, ct);
            await ScheduleNextPollAsync(renderJob, "KIE task not terminal; next poll scheduled.", ct);
        }
        catch (KieProviderException ex) when (ex.IsTransient)
        {
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? _options.CurrentValue.PollInterval, ex.ErrorCode ?? KieErrorCodes.PollFailed, ex.Message, ct);
            throw new RenderJobDeferredException("KIE transient poll failure scheduled for retry.");
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.PollFailed, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
    }

    private async Task ScheduleNextPollAsync(RenderJobDto renderJob, string message, CancellationToken ct)
    {
        await _renderJobs.ScheduleRetryAsync(renderJob.Id, _options.CurrentValue.PollInterval, "KIE_POLL_SCHEDULED", message, ct);
        throw new RenderJobDeferredException(message);
    }

    private async Task FailAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, string errorCode, string errorMessage, string? rawResponse, bool permanent, CancellationToken ct, string status = DanceSellJobStatuses.Failed)
    {
        await _repo.UpdateFailedAsync(danceJob.Id, status, null, BuildErrorJson(errorCode, errorMessage, rawResponse), errorCode, errorMessage, ct);
        await _renderJobs.AddEventAsync(renderJob.Id, "KIE_TASK_FAILED", errorMessage,
            new { danceSellJobId = danceJob.Id, danceJob.ProviderTaskId, errorCode, permanent }, "error", ct);
        await LogUsageAsync(danceJob, renderJob, "failed", danceJob.ProviderTaskId, danceJob.ProviderStatus, null, errorMessage, ct);
    }

    private async Task LogUsageAsync(DanceSellJobDto danceJob, RenderJobDto renderJob, string status, string? taskId, string? providerStatus, int? resultUrlCount, string? errorMessage, CancellationToken ct)
    {
        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = DanceSellCompletionService.ToBigIntCustomerId(danceJob.CustomerId),
            ProviderCode = DanceSellConstants.ProviderCode,
            CapabilityCode = DanceSellConstants.CapabilityCode,
            FeatureCode = DanceSellConstants.FeatureCode,
            ModelName = DanceSellConstants.Model,
            RequestId = danceJob.LogicalRequestId,
            JobId = renderJob.Id.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = 0,
            TotalPoints = 0,
            ProviderRawCost = null,
            Status = status,
            ErrorMessage = errorMessage,
            MetadataJson = JsonSerializer.Serialize(new
            {
                danceSellJobId = danceJob.Id,
                providerTaskId = taskId,
                providerStatus,
                resultUrlCount,
                phase = "phase1_no_billing"
            }, KieJson.Options)
        }, ct);
    }

    private static string BuildErrorJson(KieProviderException ex)
        => BuildErrorJson(ex.ErrorCode, ex.Message, ex.RawResponse, ex.StatusCode);

    private static string BuildErrorJson(string? errorCode, string errorMessage, string? rawResponse, int? statusCode = null)
        => JsonSerializer.Serialize(new KieProviderError
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            HttpStatus = statusCode,
            RawResponse = KieJsonRedactor.Redact(rawResponse)
        }, KieJson.Options);
}

public static class KieJsonRedactor
{
    private static readonly string[] SecretKeys = new[]
    {
        "authorization", "apiKey", "api_key", "token", "accessToken", "secret", "password", "KIE_API_KEY"
    };

    public static string? Redact(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var redacted = RedactElement(doc.RootElement);
            return JsonSerializer.Serialize(redacted, KieJson.Options);
        }
        catch (JsonException)
        {
            var text = raw;
            foreach (var key in SecretKeys)
            {
                text = text.Replace(key, "[redacted-key]", StringComparison.OrdinalIgnoreCase);
            }

            return text;
        }
    }

    private static object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => IsSecretKey(p.Name) ? (object?)"[redacted]" : RedactElement(p.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool IsSecretKey(string key)
        => SecretKeys.Any(x => key.Equals(x, StringComparison.OrdinalIgnoreCase));
}
