using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.AiProviders;

public interface IYEScaleTaskClient
{
    Task<YEScaleTaskSubmitResponse> SubmitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default);
    Task<YEScaleTaskStatusResponse> GetStatusAsync(string taskId, CancellationToken ct = default);
    Task<YEScaleTaskResult> SubmitAndWaitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default);
}

public sealed class YEScaleTaskClient : IYEScaleTaskClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<YEScaleOptions> _options;
    private readonly ILogger<YEScaleTaskClient> _logger;

    public YEScaleTaskClient(HttpClient http, IOptionsMonitor<YEScaleOptions> options, ILogger<YEScaleTaskClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<YEScaleTaskSubmitResponse> SubmitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
    {
        ValidateSubmitRequest(request);
        var options = _options.CurrentValue;
        using var response = await SendWithRetriesAsync(
            () => BuildMessage(HttpMethod.Post, BuildUrl(options, options.TaskSubmitPath), options, JsonSerializer.Serialize(request, JsonOptions)),
            "submit",
            request.Model,
            taskId: null,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!IsSuccess(response.StatusCode))
        {
            throw BuildException(response.StatusCode, body, null);
        }

        var parsed = Deserialize<YEScaleTaskSubmitResponse>(body);
        if (string.IsNullOrWhiteSpace(parsed.TaskId))
        {
            throw new YEScaleTaskException("YEScale submit response missing task_id.", (int)response.StatusCode);
        }

        return parsed;
    }

    public async Task<YEScaleTaskStatusResponse> GetStatusAsync(string taskId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("taskId is required.", nameof(taskId));
        }

        var options = _options.CurrentValue;
        var path = options.TaskStatusPathTemplate.Replace("{task_id}", Uri.EscapeDataString(taskId), StringComparison.Ordinal);
        using var response = await SendWithRetriesAsync(
            () => BuildMessage(HttpMethod.Get, BuildUrl(options, path), options, body: null),
            "poll",
            model: null,
            taskId,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!IsSuccess(response.StatusCode))
        {
            throw BuildException(response.StatusCode, body, taskId);
        }

        var parsed = Deserialize<YEScaleTaskStatusResponse>(body);
        if (string.IsNullOrWhiteSpace(parsed.Status))
        {
            throw new YEScaleTaskException("YEScale poll response missing status.", (int)response.StatusCode, taskId: taskId);
        }

        if (string.IsNullOrWhiteSpace(parsed.TaskId))
        {
            parsed.TaskId = taskId;
        }

        return parsed;
    }

    public async Task<YEScaleTaskResult> SubmitAndWaitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var submit = await SubmitAsync(request, ct);
        var taskId = submit.TaskId!;
        var options = _options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PollTimeout);

        _logger.LogInformation("YESCALE_TASK_SUBMITTED model={Model} taskId={TaskId}", request.Model, taskId);

        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var status = await GetStatusAsync(taskId, timeout.Token);
                _logger.LogInformation("YESCALE_TASK_STATUS model={Model} taskId={TaskId} status={Status}", request.Model, taskId, status.Status);

                if (status.IsSuccess || status.IsFailure)
                {
                    started.Stop();
                    return new YEScaleTaskResult
                    {
                        TaskId = taskId,
                        Status = status.Status ?? string.Empty,
                        TerminalResponse = status,
                        SubmitResponseJson = JsonSerializer.Serialize(submit, JsonOptions),
                        TerminalResponseJson = JsonSerializer.Serialize(status, JsonOptions),
                        Duration = started.Elapsed
                    };
                }

                await Task.Delay(options.PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new YEScaleTaskException("YEScale poll timed out.", transient: true, taskId: taskId, innerException: ex);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        Func<HttpRequestMessage> createMessage,
        string operation,
        string? model,
        string? taskId,
        CancellationToken ct)
    {
        var options = _options.CurrentValue;
        Exception? lastException = null;
        for (var attempt = 0; attempt <= options.RetryCount; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var message = createMessage();
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                requestTimeout.CancelAfter(options.RequestTimeout);
                response = await _http.SendAsync(message, requestTimeout.Token);
                if (!IsRetryable(response.StatusCode))
                {
                    return response;
                }

                if (attempt >= options.RetryCount)
                {
                    return response;
                }

                var wait = RetryDelay(response, attempt);
                _logger.LogWarning("YESCALE_TRANSIENT_HTTP operation={Operation} model={Model} taskId={TaskId} attempt={Attempt} status={StatusCode} waitMs={WaitMs}",
                    operation, model, taskId, attempt + 1, (int)response.StatusCode, wait.TotalMilliseconds);
                response.Dispose();
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (OperationCanceledException ex)
            {
                response?.Dispose();
                if (attempt < options.RetryCount)
                {
                    lastException = ex;
                    var wait = RetryDelay(null, attempt);
                    _logger.LogWarning("YESCALE_TRANSIENT_TIMEOUT operation={Operation} model={Model} taskId={TaskId} attempt={Attempt} waitMs={WaitMs}",
                        operation, model, taskId, attempt + 1, wait.TotalMilliseconds);
                    await Task.Delay(wait, ct);
                    continue;
                }

                throw new YEScaleTaskException("YEScale request timed out.", transient: true, taskId: taskId, innerException: ex);
            }
            catch (Exception ex) when (IsTransientNetworkException(ex) && attempt < options.RetryCount)
            {
                response?.Dispose();
                lastException = ex;
                var wait = RetryDelay(null, attempt);
                _logger.LogWarning("YESCALE_TRANSIENT_NETWORK operation={Operation} model={Model} taskId={TaskId} attempt={Attempt} waitMs={WaitMs} error={Error}",
                    operation, model, taskId, attempt + 1, wait.TotalMilliseconds, ex.GetType().Name);
                await Task.Delay(wait, ct);
            }
        }

        throw new YEScaleTaskException("YEScale transient request failed after retries.", transient: true, taskId: taskId, innerException: lastException);
    }

    private static HttpRequestMessage BuildMessage(HttpMethod method, Uri uri, YEScaleOptions options, string? body)
    {
        var message = new HttpRequestMessage(method, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GetAccessKeyOrThrow());
        if (body is not null)
        {
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return message;
    }

    private static Uri BuildUrl(YEScaleOptions options, string path)
    {
        var baseUri = options.GetBaseUri().ToString().TrimEnd('/');
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        return new Uri(baseUri + normalized, UriKind.Absolute);
    }

    private static void ValidateSubmitRequest(YEScaleTaskSubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model)) throw new InvalidOperationException("YEScale model is required.");
        if (string.IsNullOrWhiteSpace(request.Prompt)) throw new InvalidOperationException("YEScale prompt is required.");
    }

    private static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;

    private static bool IsRetryable(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == HttpStatusCode.TooManyRequests
           || (int)statusCode >= 500;

    private static bool IsTransientNetworkException(Exception ex)
        => ex is HttpRequestException or IOException;

    private static TimeSpan RetryDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response?.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) return delay;
        }

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
    }

    private static T Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new YEScaleTaskException("YEScale response body is empty.");
        }
        catch (JsonException ex)
        {
            throw new YEScaleTaskException("YEScale response JSON invalid.", innerException: ex);
        }
    }

    private static YEScaleTaskException BuildException(HttpStatusCode statusCode, string body, string? taskId)
    {
        var errorCode = TryReadErrorCode(body);
        var transient = IsRetryable(statusCode);
        return new YEScaleTaskException($"YEScale HTTP {(int)statusCode}.", (int)statusCode, errorCode, transient, taskId);
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.TryGetProperty("code", out var code)) return code.GetString();
                if (error.TryGetProperty("type", out var type)) return type.GetString();
            }

            return root.TryGetProperty("code", out var topCode) ? topCode.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
