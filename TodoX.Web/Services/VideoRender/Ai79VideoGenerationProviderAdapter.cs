using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public sealed class Ai79VideoGenerationProviderAdapter : IVideoGenerationProviderAdapter
{
    private readonly IRVideo79AiVideoService _video;

    public Ai79VideoGenerationProviderAdapter(IRVideo79AiVideoService video)
    {
        _video = video;
    }

    public bool CanHandle(string providerCode, string capabilityCode)
        => RVideoVideoModelPolicy.Is79AiProvider(providerCode)
           && string.Equals(capabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase);

    public async Task<VideoProviderSubmitResult> SubmitAsync(VideoProviderSubmitRequest request, CancellationToken ct = default)
    {
        try
        {
            var runtime = await _video.ResolveRuntimeAsync(
                request.ProviderId,
                request.ProviderCapabilityId,
                request.ProviderCode,
                ct);
            var source = await _video.UploadSourceImageAsync(runtime, new RVideo79AiVideoSourceImage(
                request.SourceImage.MediaId,
                request.SourceImage.ObjectKey,
                request.SourceImage.PublicUrl,
                request.SourceImage.FileName,
                request.SourceImage.MimeType), ct);
            var result = await _video.SubmitAsync(new RVideo79AiVideoSubmitRequest(
                runtime,
                new RVideoVideoModelPolicyEntry(0, request.ProviderCode, request.RequestedModel, request.ModelMode),
                request.Prompt,
                request.AspectRatio,
                request.Resolution,
                request.DurationSeconds,
                source), ct);
            return new VideoProviderSubmitResult(
                request.ProviderCode,
                result.TaskId,
                request.RequestedModel,
                result.SanitizedRequestJson,
                result.SanitizedResponseJson);
        }
        catch (Ai79TaskSubmitException ex) when (ex.HttpStatusCode is null or >= System.Net.HttpStatusCode.InternalServerError
            || ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new VideoProviderTransientException(ex.Message, ex.ErrorCode, ex);
        }
    }

    public async Task<VideoProviderPollResult> PollAsync(VideoProviderPollRequest request, CancellationToken ct = default)
    {
        try
        {
            var runtime = await _video.ResolveRuntimeAsync(
                request.ProviderId,
                request.ProviderCapabilityId,
                request.ProviderCode,
                ct);
            var status = await _video.PollAsync(runtime, request.ProviderTaskId, ct);
            return new VideoProviderPollResult(
                status.NormalizedStatus switch
                {
                    Ai79TaskStatusNormalizer.Success => VideoProviderTaskStatus.Success,
                    Ai79TaskStatusNormalizer.Failed => VideoProviderTaskStatus.Failed,
                    _ => VideoProviderTaskStatus.Processing
                },
                request.ProviderTaskId,
                status.OutputUrl,
                null,
                status.ErrorCode,
                status.ErrorMessage,
                status.SanitizedResponseJson);
        }
        catch (Ai79TaskPollException ex)
        {
            throw new VideoProviderTransientException(ex.Message, ex.GetType().Name, ex);
        }
    }
}
