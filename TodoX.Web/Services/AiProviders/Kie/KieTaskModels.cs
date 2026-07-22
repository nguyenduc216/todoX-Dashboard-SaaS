using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.Web.Services.AiProviders.Kie;

public sealed class KieMotionControlRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("callBackUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallBackUrl { get; set; }

    [JsonPropertyName("input")]
    public KieMotionControlInput Input { get; set; } = new();
}

public sealed class KieMotionControlInput
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("input_urls")]
    public List<string> InputUrls { get; set; } = new();

    [JsonPropertyName("video_urls")]
    public List<string> VideoUrls { get; set; } = new();

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "720p";

    [JsonPropertyName("character_orientation")]
    public string CharacterOrientation { get; set; } = "image";
}

public sealed class KieImageToImageRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("callBackUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallBackUrl { get; set; }

    [JsonPropertyName("input")]
    public KieImageToImageInput Input { get; set; } = new();
}

public sealed class KieImageToImageInput
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("input_urls")]
    public List<string> InputUrls { get; set; } = new();

    [JsonPropertyName("aspect_ratio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AspectRatio { get; set; }
}

public sealed class KieEnvelope<T>
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Message { get; set; }

    [JsonPropertyName("message")]
    public string? AlternateMessage { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class KieCreateTaskData
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }
}

public sealed class KieTaskDetailData
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("resultJson")]
    public string? ResultJson { get; set; }

    [JsonPropertyName("failCode")]
    public string? FailCode { get; set; }

    [JsonPropertyName("failMsg")]
    public string? FailMsg { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("param")]
    public JsonElement? Param { get; set; }

    [JsonPropertyName("costTime")]
    public decimal? CostTime { get; set; }

    [JsonPropertyName("completeTime")]
    public long? CompleteTime { get; set; }

    [JsonPropertyName("createTime")]
    public long? CreateTime { get; set; }

    [JsonPropertyName("updateTime")]
    public long? UpdateTime { get; set; }

    [JsonPropertyName("progress")]
    public decimal? Progress { get; set; }

    [JsonPropertyName("creditsConsumed")]
    public decimal? CreditsConsumed { get; set; }
}

public sealed class KieCreateTaskResult
{
    public string? TaskId { get; set; }
    public int HttpStatus { get; set; }
    public string RawResponse { get; set; } = string.Empty;
    public bool IsSuccess => !string.IsNullOrWhiteSpace(TaskId);
}

public sealed class KieTaskDetailResult
{
    public string? TaskId { get; set; }
    public string? ProviderState { get; set; }
    public string Status { get; set; } = KieTaskStatuses.Unknown;
    public string? ResultJson { get; set; }
    public IReadOnlyList<string> ResultUrls { get; set; } = Array.Empty<string>();
    public string? ResultParseError { get; set; }
    public string? FailCode { get; set; }
    public string? FailMsg { get; set; }
    public string? Model { get; set; }
    public string? ParamJson { get; set; }
    public decimal? CostTime { get; set; }
    public DateTimeOffset? CompleteTime { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public DateTimeOffset? UpdateTime { get; set; }
    public decimal? Progress { get; set; }
    public decimal? CreditsConsumed { get; set; }
    public int HttpStatus { get; set; }
    public string RawResponse { get; set; } = string.Empty;
    public bool IsTerminal => KieTaskStatusMapper.IsTerminal(Status);
    public bool IsSuccess => KieTaskStatusMapper.IsSuccess(Status);
    public bool IsFailure => KieTaskStatusMapper.IsFailure(Status);
}

public sealed class KieCallbackResult
{
    public string? TaskId { get; set; }
    public string? ProviderState { get; set; }
    public string Status { get; set; } = KieTaskStatuses.Unknown;
    public string RawJson { get; set; } = string.Empty;
    public IReadOnlyList<string> ResultUrls { get; set; } = Array.Empty<string>();
    public string? ResultJson { get; set; }
    public string? ResultParseError { get; set; }
    public string? FailCode { get; set; }
    public string? FailMsg { get; set; }
    public string? Model { get; set; }
    public string? ParamJson { get; set; }
    public decimal? CostTime { get; set; }
    public DateTimeOffset? CompleteTime { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public DateTimeOffset? UpdateTime { get; set; }
    public decimal? Progress { get; set; }
    public decimal? CreditsConsumed { get; set; }
}

public sealed class KieProviderError
{
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? HttpStatus { get; set; }
    public string? RawResponse { get; set; }
}

public static class KieTaskStatuses
{
    public const string Queued = "queued";
    public const string Rendering = "rendering";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Unknown = "unknown";
}

public sealed class KieProviderException : Exception
{
    public KieProviderException(
        string message,
        string? errorCode = null,
        bool transient = false,
        int? statusCode = null,
        string? rawResponse = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsTransient = transient;
        StatusCode = statusCode;
        RawResponse = rawResponse;
        RetryAfter = retryAfter;
    }

    public string? ErrorCode { get; }
    public bool IsTransient { get; }
    public int? StatusCode { get; }
    public string? RawResponse { get; }
    public TimeSpan? RetryAfter { get; }
}

public static class KieJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
