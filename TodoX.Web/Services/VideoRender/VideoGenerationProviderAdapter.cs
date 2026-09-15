namespace TodoX.Web.Services.VideoRender;

public enum VideoProviderTaskStatus
{
    Queued,
    Processing,
    ResourceUnavailable,
    Success,
    Failed
}

public enum VideoProviderHttpSubmitDiagnosticStage
{
    Request,
    Response,
    ResponseParseFailed
}

public sealed record VideoProviderHttpSubmitDiagnostic(
    VideoProviderHttpSubmitDiagnosticStage Stage,
    string Endpoint,
    string ActualModel,
    string? Mode,
    int DurationSeconds,
    string Ratio,
    string Resolution,
    IReadOnlyList<string> ImageUrls,
    string? PromptPreview = null,
    int? HttpStatus = null,
    string? ProviderTaskId = null,
    string? ProviderVideoIdBase = null,
    string? ProviderStatus = null,
    int? CountTasks = null,
    string? ProviderMessage = null,
    string? SanitizedResponseJson = null);

public sealed record VideoProviderSourceImage(
    Guid? MediaId,
    string? ObjectKey,
    string? PublicUrl,
    string? FileName,
    string? MimeType);

public sealed record VideoProviderSubmitRequest(
    long ProviderId,
    long ProviderCapabilityId,
    string ProviderCode,
    string CapabilityCode,
    string RequestedModel,
    string? ModelMode,
    string Prompt,
    string AspectRatio,
    string Resolution,
    int DurationSeconds,
    VideoProviderSourceImage? SourceImage,
    IReadOnlyList<VideoProviderSourceImage> ReferenceImages,
    Func<VideoProviderHttpSubmitDiagnostic, CancellationToken, Task>? HttpSubmitDiagnostic = null);

public sealed record VideoProviderSubmitResult(
    string ProviderCode,
    string ProviderTaskId,
    string? ActualModel,
    string SanitizedRequestJson,
    string SanitizedResponseJson,
    string? ProviderVideoIdBase = null);

public sealed record VideoProviderPollRequest(
    long ProviderId,
    long ProviderCapabilityId,
    string ProviderCode,
    string CapabilityCode,
    string ProviderTaskId);

public sealed record VideoProviderPollResult(
    VideoProviderTaskStatus Status,
    string ProviderTaskId,
    string? OutputUrl,
    string? ActualModel,
    string? ErrorCode,
    string? ErrorMessage,
    string SanitizedResponseJson);

public interface IVideoGenerationProviderAdapter
{
    bool CanHandle(string providerCode, string capabilityCode);
    Task<VideoProviderSubmitResult> SubmitAsync(VideoProviderSubmitRequest request, CancellationToken ct = default);
    Task<VideoProviderPollResult> PollAsync(VideoProviderPollRequest request, CancellationToken ct = default);
}

public interface IVideoGenerationProviderAdapterResolver
{
    IVideoGenerationProviderAdapter Resolve(string providerCode, string capabilityCode);
}

public sealed class VideoGenerationProviderAdapterResolver : IVideoGenerationProviderAdapterResolver
{
    private readonly IEnumerable<IVideoGenerationProviderAdapter> _adapters;

    public VideoGenerationProviderAdapterResolver(IEnumerable<IVideoGenerationProviderAdapter> adapters)
    {
        _adapters = adapters;
    }

    public IVideoGenerationProviderAdapter Resolve(string providerCode, string capabilityCode)
        => _adapters.FirstOrDefault(adapter => adapter.CanHandle(providerCode, capabilityCode))
           ?? throw new InvalidOperationException(
               $"VIDEO_PROVIDER_ADAPTER_UNAVAILABLE provider={providerCode} capability={capabilityCode}");
}

public sealed class VideoProviderTransientException : InvalidOperationException
{
    public VideoProviderTransientException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
