using System.Text.Json;

namespace TodoX.Web.Services.AiProviders.SeventyNineAI;

/// <summary>
/// 79AI video render provider client implementing IAiVideoRenderProviderClient.
/// Integrates 79AI into the unified video render pipeline.
/// </summary>
public sealed class SeventyNineAiVideoRenderProviderClient : IAiVideoRenderProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISeventyNineAiClient _client;
    private readonly IAiProviderCredentialStore _credentialStore;
    private readonly ILogger<SeventyNineAiVideoRenderProviderClient> _logger;

    public SeventyNineAiVideoRenderProviderClient(
        ISeventyNineAiClient client,
        IAiProviderCredentialStore credentialStore,
        ILogger<SeventyNineAiVideoRenderProviderClient> logger)
    {
        _client = client;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public string ProviderCode => SeventyNineAiConstants.ProviderCode;

    public async Task<ProviderVideoSubmitResult> SubmitAsync(ProviderVideoRenderRequest request, CancellationToken ct = default)
    {
        var config = SeventyNineAiProviderConfig.Parse(request.ProviderConfigJson, request.CapabilityConfigJson);
        var context = BuildExecutionContext(request.CredentialSecret, config);

        var images = string.IsNullOrWhiteSpace(request.SourceImageUrl)
            ? Array.Empty<string>()
            : new[] { request.SourceImageUrl };

        var videoRequest = new SeventyNineAiCreateVideoRequest
        {
            Model = request.ModelName,
            Prompt = request.Prompt,
            Ratio = NormalizeRatio(request.AspectRatio),
            Resolution = NormalizeResolution(request.Resolution),
            Duration = request.DurationSeconds,
            Mode = images.Length > 0 ? "image_to_video" : "text_to_video",
            Images = images
        };

        var submit = await _client.CreateVideoAsync(videoRequest, context, ct);
        var videoInfo = submit.VideoInfo
            ?? throw new ProviderVideoRenderException("79AI video submit response missing videoInfo.", ProviderCode);

        var taskId = videoInfo.IdBase ?? videoInfo.TaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ProviderVideoRenderException("79AI video submit response missing id_base/task_id.", ProviderCode);
        }

        return new ProviderVideoSubmitResult
        {
            ProviderTaskId = taskId.Trim(),
            ProviderCreditFee = videoInfo.CreditFee,
            RawRequestJson = AiSecretRedactor.Redact(JsonSerializer.Serialize(videoRequest, JsonOptions)),
            RawResponseJson = AiSecretRedactor.Redact(submit.RawJson)
        };
    }

    public async Task<ProviderVideoStatusResult> GetStatusAsync(ProviderVideoStatusRequest request, CancellationToken ct = default)
    {
        var config = SeventyNineAiProviderConfig.Parse(request.ProviderConfigJson, request.CapabilityConfigJson);
        var context = BuildExecutionContext(request.CredentialSecret, config);

        var status = await _client.GetVideoStatusAsync(request.ProviderTaskId, context, ct);
        var videoInfo = status.VideoInfo
            ?? throw new ProviderVideoRenderException($"79AI video status response missing videoInfo for task {request.ProviderTaskId}.", ProviderCode, taskId: request.ProviderTaskId);

        var state = MapStatus(videoInfo.Status);
        var resultUrl = ExtractResultUrl(videoInfo, request.SourceImageUrl);

        return new ProviderVideoStatusResult
        {
            ProviderTaskId = request.ProviderTaskId,
            State = state,
            ProviderStatus = videoInfo.Status,
            ResultUrl = resultUrl,
            ProviderCreditFee = videoInfo.CreditFee,
            ErrorMessage = state == ProviderVideoTaskState.Failed ? $"79AI video task failed with status {videoInfo.Status ?? "unknown"}." : null,
            RawResponseJson = AiSecretRedactor.Redact(status.RawJson)
        };
    }

    private SeventyNineAiExecutionContext BuildExecutionContext(string? credentialSecret, SeventyNineAiProviderConfig config)
    {
        var accessToken = credentialSecret;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ProviderVideoRenderException("79AI access token is required.", ProviderCode);
        }

        return new SeventyNineAiExecutionContext
        {
            BaseUrl = SeventyNineAiConstants.DefaultBaseUrl,
            Domain = config.Domain,
            ProjectId = config.ProjectId,
            AccessToken = accessToken
        };
    }

    private static ProviderVideoTaskState MapStatus(string? status)
    {
        var normalized = status?.Trim().ToUpperInvariant();
        return normalized switch
        {
            "COMPLETED" or "DONE" or "SUCCESS" or "FINISHED" => ProviderVideoTaskState.Completed,
            "FAILED" or "ERROR" or "CANCELLED" or "CANCELED" or "EXPIRED" => ProviderVideoTaskState.Failed,
            "PENDING" or "QUEUED" or "WAITING" or "SUBMITTED" => ProviderVideoTaskState.Submitted,
            "PROCESSING" or "RENDERING" or "IN_PROGRESS" or "RUNNING" => ProviderVideoTaskState.Processing,
            _ => ProviderVideoTaskState.PendingReconciliation
        };
    }

    private static string? ExtractResultUrl(SeventyNineAiVideoInfo videoInfo, string? sourceImageUrl)
    {
        var candidates = new[] { videoInfo.DownloadUrl, videoInfo.Url }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Where(x => !string.Equals(x, sourceImageUrl, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.FirstOrDefault();
    }

    private static string NormalizeRatio(string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(aspectRatio))
        {
            return "9_16";
        }

        return aspectRatio.Trim() switch
        {
            "16:9" or "16_9" => "16_9",
            "9:16" or "9_16" => "9_16",
            "1:1" or "1_1" => "1_1",
            "4:3" or "4_3" => "4_3",
            "3:4" or "3_4" => "3_4",
            _ => aspectRatio.Replace(":", "_")
        };
    }

    private static string NormalizeResolution(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return "720p";
        }

        var normalized = resolution.Trim().ToLowerInvariant();
        return normalized switch
        {
            "720p" or "720" or "hd" => "720p",
            "1080p" or "1080" or "full_hd" or "fullhd" => "1080p",
            "480p" or "480" or "sd" => "480p",
            "360p" or "360" => "360p",
            _ => resolution.Trim()
        };
    }
}
