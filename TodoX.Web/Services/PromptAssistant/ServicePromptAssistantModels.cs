using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.PromptAssistant;

public sealed class ServicePromptAssistantOptions
{
    public const string SectionName = "PromptAssistant";

    public bool EnabledByDefault { get; set; } = false;
    public string ProviderCode { get; set; } = "79ai";
    public string ApiUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxTemplateBytes { get; set; } = 2_000_000;
    public int MaxDescriptionBytes { get; set; } = 1_000_000;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 600));
    public int TemplateLimit => Math.Clamp(MaxTemplateBytes, 1_024, 10_000_000);
    public int DescriptionLimit => Math.Clamp(MaxDescriptionBytes, 1_024, 10_000_000);
}

public enum ServicePromptTrainingStatus
{
    Draft,
    Published,
    Archived
}

public enum ServicePromptGenerationStatus
{
    Success,
    ValidationFailed,
    ProviderFailed,
    RepairFailed
}

public sealed class ServicePromptAssistantDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public bool Enabled { get; set; }
    public string ProviderCode { get; set; } = "79ai";
    public string ModelCode { get; set; } = string.Empty;
    public decimal? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public int MaxRepairAttempts { get; set; } = 1;
    public Guid? ActiveTrainingVersionId { get; set; }
}

public sealed class ServicePromptTrainingVersionDto
{
    public Guid Id { get; set; }
    public Guid ServicePromptAssistantId { get; set; }
    public int VersionNo { get; set; }
    public string? VersionName { get; set; }
    public ServicePromptTrainingStatus Status { get; set; }
    public string TemplateFileName { get; set; } = string.Empty;
    public string TemplateJson { get; set; } = string.Empty;
    public string DescriptionFileName { get; set; } = string.Empty;
    public string DescriptionContent { get; set; } = string.Empty;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class ServicePromptGenerationDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ServicePromptAssistantId { get; set; }
    public Guid TrainingVersionId { get; set; }
    public string UserInput { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string? GeneratedJson { get; set; }
    public bool ValidationPassed { get; set; }
    public IReadOnlyList<ServicePromptValidationError> ValidationErrors { get; set; } = [];
    public int RepairAttemptCount { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public ServicePromptGenerationStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public TimeSpan? Latency { get; set; }
}

public sealed class ServicePromptGenerationResult
{
    public Guid GenerationId { get; init; }
    public string? GeneratedJson { get; init; }
    public bool ValidationPassed { get; init; }
    public IReadOnlyList<ServicePromptValidationError> ValidationErrors { get; init; } = [];
    public int RepairAttemptCount { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ModelCode { get; init; } = string.Empty;
    public ServicePromptGenerationStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Latency { get; init; }
}

public sealed record ServicePromptValidationError(string Path, string Code, string Message);

public sealed record ServicePromptProviderRequest(
    string ApiUrl,
    string ProviderCode,
    string ModelCode,
    string SystemPrompt,
    string UserInput,
    decimal? Temperature,
    int? MaxTokens);

public sealed record ServicePromptProviderResponse(
    string Content,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    string SanitizedRawResponse,
    string? ProviderRequestId = null);

public sealed class ServicePromptProviderException : InvalidOperationException
{
    public ServicePromptProviderException(string code, string message, string sanitizedResponse, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        SanitizedResponse = sanitizedResponse;
    }

    public string Code { get; }
    public string SanitizedResponse { get; }
}

public sealed class ServicePromptGenerationRequest
{
    public Guid ServiceId { get; init; }
    public string UserInput { get; init; } = string.Empty;
    public CurrentUserSession? UserSession { get; init; }
    public Guid? CustomerId => UserSession?.CustomerId;
}

public sealed record ServicePromptAssistantSaveRequest(
    Guid ServiceId,
    bool Enabled,
    string ProviderCode,
    string ModelCode,
    decimal? Temperature,
    int? MaxTokens,
    int MaxRepairAttempts);

public sealed record ServicePromptTrainingSaveRequest(
    Guid ServiceId,
    string VersionName,
    string TemplateFileName,
    string TemplateJson,
    string DescriptionFileName,
    string DescriptionContent);

public sealed class ServicePromptAssistantWorkspace
{
    public ServicePromptAssistantDto Assistant { get; init; } = new();
    public IReadOnlyList<ServicePromptTrainingVersionDto> TrainingVersions { get; init; } = [];
    public IReadOnlyList<ServicePromptGenerationDto> Generations { get; init; } = [];
}

public sealed class ServicePromptServiceContext
{
    public Guid Id { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public static class ServicePromptJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
