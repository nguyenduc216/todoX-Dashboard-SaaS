using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public enum ProviderVideoTaskState
{
    Submitted,
    Processing,
    Completed,
    Failed,
    PendingReconciliation
}

public sealed class ProviderVideoRenderRequest
{
    public string ProviderCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string SourceImageUrl { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720P";
    public int DurationSeconds { get; set; }
    public string? ProviderConfigJson { get; set; }
    public string? CapabilityConfigJson { get; set; }
    public string? CredentialSecret { get; set; }
}

public sealed class ProviderVideoStatusRequest
{
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderTaskId { get; set; } = string.Empty;
    public string? ProviderSecondaryTaskId { get; set; }
    public string? ModelName { get; set; }
    public string? SourceImageUrl { get; set; }
    public string? ProviderConfigJson { get; set; }
    public string? CapabilityConfigJson { get; set; }
    public string? CredentialSecret { get; set; }
}

public sealed class ProviderVideoSubmitResult
{
    public string ProviderTaskId { get; set; } = string.Empty;
    public string? ProviderSecondaryTaskId { get; set; }
    public decimal? ProviderCreditFee { get; set; }
    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }
}

public sealed class ProviderVideoStatusResult
{
    public string ProviderTaskId { get; set; } = string.Empty;
    public string? ProviderSecondaryTaskId { get; set; }
    public ProviderVideoTaskState State { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ResultUrl { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? ProviderCreditFee { get; set; }
    public string? RawResponseJson { get; set; }

    public bool IsSuccess => State == ProviderVideoTaskState.Completed;
    public bool IsFailure => State == ProviderVideoTaskState.Failed;
    public bool IsTerminal => State is ProviderVideoTaskState.Completed or ProviderVideoTaskState.Failed or ProviderVideoTaskState.PendingReconciliation;
}

public sealed class ProviderVideoRenderException : Exception
{
    public ProviderVideoRenderException(
        string message,
        string providerCode,
        bool transient = false,
        int? statusCode = null,
        string? errorCode = null,
        string? taskId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderCode = providerCode;
        IsTransient = transient;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        TaskId = taskId;
    }

    public string ProviderCode { get; }
    public bool IsTransient { get; }
    public int? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? TaskId { get; }
}

public interface IAiVideoRenderProviderClient
{
    string ProviderCode { get; }
    Task<ProviderVideoSubmitResult> SubmitAsync(ProviderVideoRenderRequest request, CancellationToken ct = default);
    Task<ProviderVideoStatusResult> GetStatusAsync(ProviderVideoStatusRequest request, CancellationToken ct = default);
}

public interface IAiVideoRenderProviderResolver
{
    IAiVideoRenderProviderClient Resolve(string providerCode);
}

public sealed class AiVideoRenderProviderResolver : IAiVideoRenderProviderResolver
{
    private readonly IReadOnlyDictionary<string, IAiVideoRenderProviderClient> _clients;

    public AiVideoRenderProviderResolver(IEnumerable<IAiVideoRenderProviderClient> clients)
    {
        _clients = clients.ToDictionary(x => x.ProviderCode, StringComparer.OrdinalIgnoreCase);
    }

    public IAiVideoRenderProviderClient Resolve(string providerCode)
    {
        if (_clients.TryGetValue(providerCode, out var client))
        {
            return client;
        }

        var factoryKey = ProviderCodeMap.ToFactoryKey(providerCode);
        if (_clients.TryGetValue(factoryKey, out client))
        {
            return client;
        }

        throw new NotSupportedException($"Video provider '{providerCode}' is not supported.");
    }
}

public sealed class YEScaleVideoRenderProviderClient : IAiVideoRenderProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IYEScaleTaskClient _tasks;

    public YEScaleVideoRenderProviderClient(IYEScaleTaskClient tasks)
    {
        _tasks = tasks;
    }

    public string ProviderCode => "yescale";

    public async Task<ProviderVideoSubmitResult> SubmitAsync(ProviderVideoRenderRequest request, CancellationToken ct = default)
    {
        var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
            request.ModelName,
            request.Prompt,
            request.SourceImageUrl,
            request.AspectRatio,
            request.Resolution,
            request.DurationSeconds,
            request.ProviderConfigJson,
            request.CapabilityConfigJson);

        payload.ApiKey = request.CredentialSecret;
        try
        {
            var submit = await _tasks.SubmitAsync(payload, ct);
            var taskId = string.IsNullOrWhiteSpace(submit.TaskId) ? null : submit.TaskId.Trim();
            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new ProviderVideoRenderException("YEScale submit response is missing task_id.", ProviderCode, taskId: taskId);
            }

            return new ProviderVideoSubmitResult
            {
                ProviderTaskId = taskId,
                RawRequestJson = AiSecretRedactor.Redact(JsonSerializer.Serialize(payload, JsonOptions)),
                RawResponseJson = AiSecretRedactor.Redact(JsonSerializer.Serialize(submit, JsonOptions))
            };
        }
        catch (YEScaleTaskException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<ProviderVideoStatusResult> GetStatusAsync(ProviderVideoStatusRequest request, CancellationToken ct = default)
    {
        try
        {
            var status = await _tasks.GetStatusAsync(request.ProviderTaskId, request.CredentialSecret, ct);
            var normalized = status.Status?.Trim().ToUpperInvariant();
            return new ProviderVideoStatusResult
            {
                ProviderTaskId = request.ProviderTaskId,
                State = normalized switch
                {
                    "SUCCESS" => ProviderVideoTaskState.Completed,
                    "FAILURE" or "CANCELLED" or "EXPIRED" => ProviderVideoTaskState.Failed,
                    "QUEUED" or "PENDING" or "SUBMITTED" or "PROCESSING" or "RUNNING" => ProviderVideoTaskState.Processing,
                    _ => ProviderVideoTaskState.PendingReconciliation
                },
                ProviderStatus = status.Status,
                ResultUrl = ExtractVideoUrl(status, request.SourceImageUrl),
                ErrorMessage = ExtractFailureMessage(status),
                RawResponseJson = AiSecretRedactor.Redact(JsonSerializer.Serialize(status, JsonOptions))
            };
        }
        catch (YEScaleTaskException ex)
        {
            throw ToProviderException(ex);
        }
    }

    private ProviderVideoRenderException ToProviderException(YEScaleTaskException ex)
        => new(ex.Message, ProviderCode, ex.IsTransient, ex.StatusCode, ex.ErrorCode, ex.TaskId, ex);

    private static string? ExtractVideoUrl(YEScaleTaskStatusResponse response, string? sourceImageUrl)
    {
        if (response.Extra is null)
        {
            return null;
        }

        foreach (var branchName in new[] { "task_result", "output", "result" })
        {
            if (response.Extra.TryGetValue(branchName, out var branch))
            {
                var value = ExtractVideoUrl(branch, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ExtractVideoUrl(JsonElement element, string? sourceImageUrl)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "video_url", "videoUrl", "url", "output_url", "outputUrl" })
            {
                if (element.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri))
                {
                    var candidate = uri.ToString();
                    if (!string.Equals(candidate, sourceImageUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = ExtractVideoUrl(property.Value, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractVideoUrl(item, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractFailureMessage(YEScaleTaskStatusResponse response)
    {
        if (response.Error is JsonElement error)
        {
            if (error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }

            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString()!;
            }
        }

        return response.IsFailure ? $"YEScale video task failed with status {response.Status ?? "unknown"}." : null;
    }
}
