using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.AiProviders.Kie;

public interface IKieClient
{
    Task<KieCreateTaskResult> CreateTaskAsync(KieMotionControlRequest request, CancellationToken cancellationToken);
    Task<KieTaskDetailResult> GetTaskDetailAsync(string taskId, CancellationToken cancellationToken);
    KieCallbackResult ParseCallback(string rawJson);
}

public sealed class KieClient : IKieClient
{
    private const string CreateTaskPath = "/api/v1/jobs/createTask";
    private const string RecordInfoPath = "/api/v1/jobs/recordInfo";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<KieOptions> _options;
    private readonly ILogger<KieClient> _logger;

    public KieClient(HttpClient http, IOptionsMonitor<KieOptions> options, ILogger<KieClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<KieCreateTaskResult> CreateTaskAsync(KieMotionControlRequest request, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HttpTimeout);

        var body = JsonSerializer.Serialize(request, KieJson.Options);
        using var message = BuildMessage(HttpMethod.Post, BuildUrl(options, CreateTaskPath), options, body);
        using var response = await SendAsync(message, timeout.Token, cancellationToken);
        var raw = await ReadContentAsync(response, timeout.Token, cancellationToken);
        if (!IsSuccess(response.StatusCode))
        {
            throw BuildException(response, raw, operation: "submit");
        }

        var parsed = KieResponseParser.ParseCreateTask(raw, (int)response.StatusCode);
        if (string.IsNullOrWhiteSpace(parsed.TaskId))
        {
            throw new KieProviderException("KIE submit response missing data.taskId.", KieErrorCodes.TaskIdMissing,
                transient: false, statusCode: (int)response.StatusCode, rawResponse: raw);
        }

        return parsed;
    }

    public async Task<KieTaskDetailResult> GetTaskDetailAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("taskId is required.", nameof(taskId));
        }

        var options = _options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HttpTimeout);

        var path = $"{RecordInfoPath}?taskId={Uri.EscapeDataString(taskId.Trim())}";
        using var message = BuildMessage(HttpMethod.Get, BuildUrl(options, path), options, body: null);
        using var response = await SendAsync(message, timeout.Token, cancellationToken);
        var raw = await ReadContentAsync(response, timeout.Token, cancellationToken);
        if (!IsSuccess(response.StatusCode))
        {
            throw BuildException(response, raw, operation: "poll");
        }

        var parsed = KieResponseParser.ParseTaskDetail(raw, (int)response.StatusCode, taskId);
        if (parsed.Status == KieTaskStatuses.Unknown)
        {
            _logger.LogWarning("KIE_UNKNOWN_STATUS taskId={TaskId} providerState={ProviderState}", taskId, parsed.ProviderState);
        }

        return parsed;
    }

    public KieCallbackResult ParseCallback(string rawJson)
    {
        try
        {
            return KieResponseParser.ParseCallback(rawJson);
        }
        catch (JsonException ex)
        {
            throw new KieProviderException("KIE callback JSON invalid.", KieErrorCodes.CallbackInvalid, transient: false, innerException: ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken timeoutToken, CancellationToken callerToken)
    {
        try
        {
            return await _http.SendAsync(message, timeoutToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (timeoutToken.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw new KieProviderException("KIE request timed out.", KieErrorCodes.ProviderUnavailable, transient: true, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new KieProviderException("KIE network request failed.", KieErrorCodes.ProviderUnavailable, transient: true, innerException: ex);
        }
    }

    private static async Task<string> ReadContentAsync(HttpResponseMessage response, CancellationToken timeoutToken, CancellationToken callerToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(timeoutToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (timeoutToken.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw new KieProviderException("KIE request timed out.", KieErrorCodes.ProviderUnavailable, transient: true, innerException: ex);
        }
    }

    private static HttpRequestMessage BuildMessage(HttpMethod method, Uri uri, KieOptions options, string? body)
    {
        var message = new HttpRequestMessage(method, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GetApiKeyOrThrow());
        if (body is not null)
        {
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return message;
    }

    private static Uri BuildUrl(KieOptions options, string path)
    {
        var baseUri = options.GetBaseUri().ToString().TrimEnd('/');
        var normalized = path.StartsWith('/') ? path : "/" + path;
        return new Uri(baseUri + normalized, UriKind.Absolute);
    }

    private static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;

    private static KieProviderException BuildException(HttpResponseMessage response, string raw, string operation)
    {
        var status = (int)response.StatusCode;
        var retryAfter = ReadRetryAfter(response);
        var errorCode = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => KieErrorCodes.SubmitBadRequest,
            HttpStatusCode.Unauthorized => KieErrorCodes.Unauthorized,
            HttpStatusCode.Forbidden => KieErrorCodes.Forbidden,
            HttpStatusCode.NotFound => KieErrorCodes.NotFound,
            HttpStatusCode.TooManyRequests => KieErrorCodes.RateLimited,
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => KieErrorCodes.ProviderUnavailable,
            _ => operation == "poll" ? KieErrorCodes.PollFailed : KieErrorCodes.Unknown
        };
        var transient = response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
        return new KieProviderException($"KIE HTTP {status} during {operation}.", errorCode, transient, status, raw, retryAfter);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return null;
    }
}
