using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.Web.Services.AiProviders;

public sealed class YEScaleTaskSubmitRequest
{
    [JsonIgnore]
    public string? ApiKey { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public YEScaleTaskConfig Config { get; set; } = new();
}

public class YEScaleTaskConfig
{
    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Images { get; set; }

    [JsonPropertyName("videos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Videos { get; set; }

    [JsonPropertyName("aspect_ratio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Duration { get; set; }

    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Size { get; set; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    [JsonPropertyName("quality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Quality { get; set; }

    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Background { get; set; }

    [JsonPropertyName("google_search")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GoogleSearch { get; set; }

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; set; }
}

public sealed class YEScaleImageTaskConfig : YEScaleTaskConfig
{
}

public sealed class YEScaleTaskSubmitResponse
{
    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class YEScaleTaskStatusResponse
{
    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsSuccess => string.Equals(Status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsFailure => string.Equals(Status, "FAILURE", StringComparison.OrdinalIgnoreCase);
}

public sealed class YEScaleTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public YEScaleTaskStatusResponse TerminalResponse { get; set; } = new();
    public string SubmitResponseJson { get; set; } = "{}";
    public string TerminalResponseJson { get; set; } = "{}";
    public TimeSpan Duration { get; set; }
}

public sealed class YEScaleTaskException : Exception
{
    public YEScaleTaskException(string message, int? statusCode = null, string? errorCode = null, bool transient = false, string? taskId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        IsTransient = transient;
        TaskId = taskId;
    }

    public int? StatusCode { get; }
    public string? ErrorCode { get; }
    public bool IsTransient { get; }
    public string? TaskId { get; }
}
