using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoX.SkillEndpoint;

public sealed record ProxyResponse(int StatusCode, JsonElement Body);

public sealed class TodoXOperationsClient
{
    private readonly HttpClient _http;
    private readonly SkillEndpointOptions _options;

    public TodoXOperationsClient(HttpClient http, IOptions<SkillEndpointOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<ProxyResponse> GetJobAsync(long jobId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/ops/v1/render-jobs/{jobId}", null, null, ct);

    public Task<ProxyResponse> DiagnoseJobAsync(long jobId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/ops/v1/render-jobs/{jobId}/diagnostic", null, null, ct);

    public Task<ProxyResponse> CreateRepairPlanAsync(long jobId, RepairPlanRequest body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/ops/v1/render-jobs/{jobId}/repair-plan", body, null, ct);

    public Task<ProxyResponse> RetryJobAsync(long jobId, RetryJobRequest body, string idempotencyKey, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/ops/v1/render-jobs/{jobId}/retry", body, idempotencyKey, ct);

    public Task<ProxyResponse> ResumeJobAsync(long jobId, ResumeJobRequest body, string idempotencyKey, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/ops/v1/render-jobs/{jobId}/resume", body, idempotencyKey, ct);

    public Task<ProxyResponse> ExecuteRepairAsync(long jobId, ExecuteRepairRequest body, string idempotencyKey, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/ops/v1/render-jobs/{jobId}/repair", body, idempotencyKey, ct);

    public Task<ProxyResponse> ReconcileJobAsync(long jobId, ReconcileJobRequest body, string idempotencyKey, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/ops/v1/render-jobs/{jobId}/reconcile", body, idempotencyKey, ct);

    public Task<ProxyResponse> GetActionAsync(string actionId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/ops/v1/actions/{Uri.EscapeDataString(actionId)}", null, null, ct);

    private async Task<ProxyResponse> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        if (_http.BaseAddress is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "TODOX_OPERATIONS_NOT_CONFIGURED",
                "Chưa cấu hình SkillEndpoint:TodoXOperationsBaseUrl.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json");

        if (!string.IsNullOrWhiteSpace(_options.TodoXOperationsApiKey))
            request.Headers.TryAddWithoutValidation("X-TodoX-Ops-Key", _options.TodoXOperationsApiKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            var element = ParseBody(text, response.IsSuccessStatusCode);
            return new ProxyResponse((int)response.StatusCode, element);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error(StatusCodes.Status504GatewayTimeout, "TODOX_OPERATIONS_TIMEOUT",
                "TodoX Operations API phản hồi quá thời gian.");
        }
        catch (HttpRequestException ex)
        {
            return Error(StatusCodes.Status502BadGateway, "TODOX_OPERATIONS_UNREACHABLE", ex.Message);
        }
    }

    private static JsonElement ParseBody(string text, bool success)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // fall through to normalized envelope
            }
        }

        return JsonSerializer.SerializeToElement(new
        {
            success,
            message = string.IsNullOrWhiteSpace(text) ? null : text
        });
    }

    private static ProxyResponse Error(int statusCode, string code, string message) =>
        new(statusCode, JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = code,
            message
        }));
}
