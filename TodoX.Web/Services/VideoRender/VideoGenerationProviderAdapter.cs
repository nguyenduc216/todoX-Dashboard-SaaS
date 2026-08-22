namespace TodoX.Web.Services.VideoRender;

public enum VideoProviderTaskStatus
{
    Queued,
    Processing,
    Success,
    Failed
}

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
    VideoProviderSourceImage SourceImage);

public sealed record VideoProviderSubmitResult(
    string ProviderCode,
    string ProviderTaskId,
    string? ActualModel,
    string SanitizedRequestJson,
    string SanitizedResponseJson);

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
