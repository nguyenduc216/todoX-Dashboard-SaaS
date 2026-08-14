using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseProviderRuntime
{
    Task ProcessImageAsync(TimelapseImageWorkItem item, CancellationToken ct = default);
    Task ProcessVideoAsync(TimelapseVideoWorkItem item, CancellationToken ct = default);
}

public sealed class TimelapseProviderRuntime : ITimelapseProviderRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AiProviderRepository _providerRepository;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderCredentialRepository _credentialRepository;
    private readonly IAi79TaskClient _taskClient;
    private readonly IMediaFileService _media;
    private readonly ITimelapseWorkerRepository _repo;
    private readonly IRenderJobService _renderJobs;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseProviderRuntime> _logger;

    public TimelapseProviderRuntime(
        AiProviderRepository providerRepository,
        IProviderCredentialResolver credentials,
        IProviderCredentialRepository credentialRepository,
        IAi79TaskClient taskClient,
        IMediaFileService media,
        ITimelapseWorkerRepository repo,
        IRenderJobService renderJobs,
        IOptions<TimelapseProviderWorkerOptions> options,
        ILogger<TimelapseProviderRuntime> logger)
    {
        _providerRepository = providerRepository;
        _credentials = credentials;
        _credentialRepository = credentialRepository;
        _taskClient = taskClient;
        _media = media;
        _repo = repo;
        _renderJobs = renderJobs;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessImageAsync(TimelapseImageWorkItem item, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.ProviderTaskId))
            {
                await SubmitImageAsync(item, ct);
                return;
            }

            var status = await PollAsync(
                item.ProviderCode,
                item.ProviderTaskId,
                _options.ImageCapabilityCode,
                _options.ImageModelName,
                isImage: true,
                "Chưa cấu hình model Seedream cho Timelapse.",
                item.ProviderModel,
                ct);
            _logger.LogInformation("TIMELAPSE_IMAGE_POLL jobId={JobId} progress={Progress} attempt={Attempt} taskId={TaskId} status={Status}",
                item.JobId, item.ProgressPercent, item.Attempt, item.ProviderTaskId, status.NormalizedStatus);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_POLL", "Timelapse image task polled.",
                new { item.ProgressPercent, item.Attempt, taskId = item.ProviderTaskId, status = status.NormalizedStatus }, ct: ct);

            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Running)
            {
                await _repo.ReleaseImageClaimAsync(item.Id, item.Attempt, ct);
                return;
            }

            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Failed)
            {
                await FailImageAsync(item, status.ErrorCode, status.ErrorMessage ?? "79AI image task failed.", status.SanitizedResponseJson, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(status.OutputUrl))
            {
                await FailImageAsync(item, "missing_output", "79AI image task completed without an output URL.", status.SanitizedResponseJson, ct);
                return;
            }

            var objectKey = BuildObjectKey(item.JobId, $"image-{item.ProgressPercent}-attempt-{item.Attempt}.png");
            var media = await _media.DownloadAndSaveImageAtObjectKeyAsync(
                status.OutputUrl,
                objectKey,
                "timelapse_generated_image",
                item.UserId,
                item.CustomerId,
                item.TenantId,
                ct);

            await _repo.SaveImageCompletedAsync(item.Id, item.Attempt, media.Id, media.ObjectKey!, media.PublicUrl ?? media.FileUrl!, status.SanitizedResponseJson, ct);
            _logger.LogInformation("TIMELAPSE_IMAGE_COMPLETE jobId={JobId} progress={Progress} attempt={Attempt} taskId={TaskId} mediaId={MediaId}",
                item.JobId, item.ProgressPercent, item.Attempt, item.ProviderTaskId, media.Id);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_COMPLETE", "Timelapse image saved to TodoX media.",
                new { item.ProgressPercent, item.Attempt, taskId = item.ProviderTaskId, mediaId = media.Id }, ct: ct);
            await _repo.AdvanceAfterImageCompletedAsync(item.JobId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TIMELAPSE_IMAGE_FAILED jobId={JobId} progress={Progress} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ProgressPercent, item.Attempt, item.ProviderTaskId);
            await FailImageAsync(item, ex.GetType().Name, ex.Message, "{}", ct);
        }
    }

    public async Task ProcessVideoAsync(TimelapseVideoWorkItem item, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.ProviderTaskId))
            {
                await SubmitVideoAsync(item, ct);
                return;
            }

            var status = await PollAsync(
                item.ProviderCode,
                item.ProviderTaskId,
                _options.VideoCapabilityCode,
                _options.VideoModelName,
                isImage: false,
                "Chưa cấu hình model Seedance cho Timelapse.",
                item.ProviderModel,
                ct);
            _logger.LogInformation("TIMELAPSE_VIDEO_POLL jobId={JobId} clip={ClipIndex} attempt={Attempt} taskId={TaskId} status={Status}",
                item.JobId, item.ClipIndex, item.Attempt, item.ProviderTaskId, status.NormalizedStatus);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_POLL", "Timelapse video task polled.",
                new { item.ClipIndex, item.Attempt, taskId = item.ProviderTaskId, status = status.NormalizedStatus }, ct: ct);

            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Running)
            {
                await _repo.ReleaseVideoClaimAsync(item.Id, item.Attempt, ct);
                return;
            }

            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Failed)
            {
                await FailVideoAsync(item, status.ErrorCode, status.ErrorMessage ?? "79AI video task failed.", status.SanitizedResponseJson, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(status.OutputUrl))
            {
                await FailVideoAsync(item, "missing_output", "79AI video task completed without an output URL.", status.SanitizedResponseJson, ct);
                return;
            }

            var objectKey = BuildObjectKey(item.JobId, $"clip-{item.ClipIndex}-attempt-{item.Attempt}.mp4");
            var media = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(
                status.OutputUrl,
                objectKey,
                "timelapse_video_clip",
                "video/mp4",
                item.UserId,
                item.CustomerId,
                item.TenantId,
                ct);

            await _repo.SaveVideoCompletedAsync(item.Id, item.Attempt, media.Id, media.ObjectKey!, media.PublicUrl ?? media.FileUrl!, status.SanitizedResponseJson, ct);
            _logger.LogInformation("TIMELAPSE_VIDEO_COMPLETE jobId={JobId} clip={ClipIndex} attempt={Attempt} taskId={TaskId} mediaId={MediaId}",
                item.JobId, item.ClipIndex, item.Attempt, item.ProviderTaskId, media.Id);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_COMPLETE", "Timelapse video clip saved to TodoX media.",
                new { item.ClipIndex, item.Attempt, taskId = item.ProviderTaskId, mediaId = media.Id }, ct: ct);
            await _repo.AdvanceAfterVideoCompletedAsync(item.JobId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TIMELAPSE_VIDEO_FAILED jobId={JobId} clip={ClipIndex} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ClipIndex, item.Attempt, item.ProviderTaskId);
            await FailVideoAsync(item, ex.GetType().Name, ex.Message, "{}", ct);
        }
    }

    private async Task SubmitImageAsync(TimelapseImageWorkItem item, CancellationToken ct)
    {
        var provider = await Resolve79AiRuntimeAsync(
            _options.ImageCapabilityCode,
            _options.ImageModelName,
            isImage: true,
            "Chưa cấu hình model Seedream cho Timelapse.",
            ct: ct);
        var dependencyUrl = item.DependencyPublicUrl ?? item.Snapshot.OriginalImage.PublicUrl
            ?? throw new InvalidOperationException("Missing Timelapse dependency image URL.");
        var prompt = TimelapsePromptResolver.ResolveImagePrompt(item.Snapshot, item.ProgressPercent, item.PromptSnapshotJson);
        var request = BuildSubmitRequest(provider, prompt, [dependencyUrl], new Dictionary<string, string?>
        {
            ["type"] = "image",
            ["progress_percent"] = item.ProgressPercent.ToString(),
            ["ratio"] = item.Snapshot.Ratio
        }, _options.DefaultImageReferenceField, null);

        var submit = await _taskClient.SubmitAsync(request.Raw, ct);
        await _repo.SaveImageSubmittedAsync(item.Id, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId, request.SanitizedJson, submit.SanitizedResponseJson, ct);
        _logger.LogInformation("TIMELAPSE_IMAGE_SUBMIT jobId={JobId} progress={Progress} attempt={Attempt} provider={ProviderCode} model={Model} taskId={TaskId}",
            item.JobId, item.ProgressPercent, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_SUBMIT", "Timelapse image task submitted to 79AI.",
            new { item.ProgressPercent, item.Attempt, provider.ProviderCode, model = provider.Model, taskId = submit.TaskId }, ct: ct);
    }

    private async Task SubmitVideoAsync(TimelapseVideoWorkItem item, CancellationToken ct)
    {
        var provider = await Resolve79AiRuntimeAsync(
            _options.VideoCapabilityCode,
            _options.VideoModelName,
            isImage: false,
            "Chưa cấu hình model Seedance cho Timelapse.",
            ct: ct);
        if (string.IsNullOrWhiteSpace(item.StartPublicUrl) || string.IsNullOrWhiteSpace(item.EndPublicUrl))
        {
            throw new InvalidOperationException("Missing Timelapse start or end image URL.");
        }

        var prompt = TimelapsePromptResolver.ResolveVideoPrompt(item.Snapshot, item.ClipIndex, item.StartProgressPercent, item.EndProgressPercent);
        var request = BuildSubmitRequest(provider, prompt, [item.StartPublicUrl!, item.EndPublicUrl!], new Dictionary<string, string?>
        {
            ["type"] = "video",
            ["duration"] = item.DurationSeconds.ToString(),
            ["mode"] = item.VideoMode,
            ["ratio"] = NormalizeRatio(item.Ratio),
            ["start_progress_percent"] = item.StartProgressPercent.ToString(),
            ["end_progress_percent"] = item.EndProgressPercent.ToString()
        }, _options.DefaultVideoStartImageField, _options.DefaultVideoEndImageField);

        var submit = await _taskClient.SubmitAsync(request.Raw, ct);
        await _repo.SaveVideoSubmittedAsync(item.Id, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId, request.SanitizedJson, submit.SanitizedResponseJson, ct);
        _logger.LogInformation("TIMELAPSE_VIDEO_SUBMIT jobId={JobId} clip={ClipIndex} attempt={Attempt} provider={ProviderCode} model={Model} taskId={TaskId}",
            item.JobId, item.ClipIndex, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_SUBMIT", "Timelapse video task submitted to 79AI.",
            new { item.ClipIndex, item.Attempt, provider.ProviderCode, model = provider.Model, taskId = submit.TaskId }, ct: ct);
    }

    private async Task<Ai79TaskStatusResult> PollAsync(
        string? providerCode,
        string? taskId,
        string capabilityCode,
        string modelName,
        bool isImage,
        string unavailableMessage,
        string? persistedModel,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new InvalidOperationException("Missing 79AI provider task id.");
        }

        var provider = await Resolve79AiRuntimeAsync(
            capabilityCode,
            modelName,
            isImage,
            unavailableMessage,
            providerCode,
            persistedModel,
            ct);
        return await _taskClient.GetStatusAsync(new Ai79TaskStatusRequest(
            provider.BaseUrl,
            provider.PollPath,
            provider.Credential.Secret,
            provider.Domain,
            taskId),
            ct);
    }

    private async Task<Ai79RuntimeProvider> Resolve79AiRuntimeAsync(
        string capabilityCode,
        string modelName,
        bool isImage,
        string unavailableMessage,
        string? expectedProviderCode = null,
        string? expectedModel = null,
        CancellationToken ct = default)
    {
        var option = await _providerRepository.GetEnabledProviderModelAsync(
            _options.ProviderCode,
            capabilityCode,
            modelName,
            ct) ?? throw new InvalidOperationException(unavailableMessage);

        if (!string.IsNullOrWhiteSpace(expectedProviderCode)
            && !string.Equals(expectedProviderCode, option.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Persisted Timelapse task provider no longer matches configured 79AI provider.");
        }

        if (!string.IsNullOrWhiteSpace(expectedModel)
            && !string.Equals(expectedModel, option.ModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Persisted Timelapse task model no longer matches the configured 79AI model.");
        }

        var provider = await _providerRepository.GetProviderAsync(option.ProviderId, ct)
            ?? throw new InvalidOperationException("Configured 79AI provider was not found.");
        var capability = provider.Capabilities.FirstOrDefault(x => x.Id == option.ProviderCapabilityId)
            ?? throw new InvalidOperationException("Configured 79AI capability was not found.");
        var credential = await _credentials.ResolveAsync(option.ProviderCode, "access_token", ct);
        var account = await _credentialRepository.GetAccountByIdAsync(credential.ProviderAccountId, ct);
        var domain = FirstNonBlank(ReadString(account?.ConfigJson, "domain"), ReadString(capability.ConfigJson, "domain"), ReadString(provider.ConfigJson, "domain"), "79ai.net")!;
        var baseUrl = FirstNonBlank(provider.BaseUrl, ReadString(provider.ConfigJson, "base_url"), _options.Default79AiBaseUrl)!;
        var (submitPath, pollPath) = Resolve79AiPaths(
            isImage,
            capability.ConfigJson,
            provider.ConfigJson,
            capability.EndpointPath,
            _options);
        var model = capability.ModelName
            ?? throw new InvalidOperationException(unavailableMessage);

        return new Ai79RuntimeProvider(option.ProviderCode, model, baseUrl, submitPath, pollPath, domain, credential);
    }

    private SubmitRequestEnvelope BuildSubmitRequest(
        Ai79RuntimeProvider provider,
        string prompt,
        IReadOnlyList<string> images,
        IReadOnlyDictionary<string, string?> options,
        string? firstImageField,
        string? secondImageField)
    {
        var raw = new Ai79TaskSubmitRequest(
            provider.BaseUrl,
            provider.SubmitPath,
            provider.Credential.Secret,
            provider.Domain,
            provider.Model,
            prompt,
            images,
            options,
            firstImageField,
            secondImageField);
        var sanitized = JsonSerializer.Serialize(new
        {
            provider.ProviderCode,
            provider.Model,
            provider.BaseUrl,
            endpointPath = provider.SubmitPath,
            provider.Domain,
            prompt,
            images,
            firstImageField,
            secondImageField,
            options
        }, JsonOptions);
        return new SubmitRequestEnvelope(raw, sanitized);
    }

    private static (string SubmitPath, string PollPath) Resolve79AiPaths(
        bool isImage,
        string? capabilityConfigJson,
        string? providerConfigJson,
        string? capabilityEndpointPath,
        TimelapseProviderWorkerOptions options)
    {
        var expected = isImage
            ? (SubmitPath: options.DefaultImageSubmitPath, PollPath: options.DefaultImagePollPath)
            : (SubmitPath: options.DefaultVideoSubmitPath, PollPath: options.DefaultVideoPollPath);
        var verified = isImage
            ? (SubmitPath: "/generateImage", PollPath: "/image")
            : (SubmitPath: "/create-video", PollPath: "/video");

        if (!PathsEqual(expected.SubmitPath, verified.SubmitPath)
            || !PathsEqual(expected.PollPath, verified.PollPath))
        {
            throw new InvalidOperationException("79AI Timelapse endpoint configuration does not match the verified production contract.");
        }

        var configuredSubmit = FirstNonBlank(
            ReadString(capabilityConfigJson, "submit_path"),
            ReadString(providerConfigJson, "submit_path"),
            capabilityEndpointPath);
        var configuredPoll = FirstNonBlank(
            ReadString(capabilityConfigJson, "poll_path"),
            ReadString(providerConfigJson, "poll_path"));

        if (configuredSubmit is not null && !PathsEqual(configuredSubmit, expected.SubmitPath))
        {
            throw new InvalidOperationException($"79AI Timelapse {(isImage ? "image" : "video")} submit endpoint is not the verified production path.");
        }

        if (configuredPoll is not null && !PathsEqual(configuredPoll, expected.PollPath))
        {
            throw new InvalidOperationException($"79AI Timelapse {(isImage ? "image" : "video")} poll endpoint is not the verified production path.");
        }

        return expected;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(left.Trim().TrimStart('/'), right.Trim().TrimStart('/'), StringComparison.OrdinalIgnoreCase);

    private async Task FailImageAsync(TimelapseImageWorkItem item, string? errorCode, string errorMessage, string responseJson, CancellationToken ct)
    {
        await _repo.SaveImageFailedAsync(item.Id, item.Attempt, errorCode, errorMessage, responseJson, ct);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_FAILED", "Timelapse image task failed.",
            new { item.ProgressPercent, item.Attempt, taskId = item.ProviderTaskId, errorCode, errorMessage }, "error", ct);
    }

    private async Task FailVideoAsync(TimelapseVideoWorkItem item, string? errorCode, string errorMessage, string responseJson, CancellationToken ct)
    {
        await _repo.SaveVideoFailedAsync(item.Id, item.Attempt, errorCode, errorMessage, responseJson, ct);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_FAILED", "Timelapse video task failed.",
            new { item.ClipIndex, item.Attempt, taskId = item.ProviderTaskId, errorCode, errorMessage }, "error", ct);
    }

    private static string BuildObjectKey(Guid jobId, string fileName)
        => $"timelapse/{DateTime.UtcNow:yyyyMM}/{jobId:N}/{fileName}";

    private static string NormalizeRatio(string? ratio)
        => string.Equals(ratio, TimelapseRequestRules.PortraitRatio, StringComparison.OrdinalIgnoreCase) ? "9:16" : "16:9";

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string? ReadString(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SubmitRequestEnvelope(Ai79TaskSubmitRequest Raw, string SanitizedJson);

    private sealed record Ai79RuntimeProvider(
        string ProviderCode,
        string Model,
        string BaseUrl,
        string SubmitPath,
        string PollPath,
        string Domain,
        ResolvedProviderCredential Credential);
}

public static class TimelapsePromptResolver
{
    public static string ResolveImagePrompt(TimelapseJobSnapshot snapshot, int progressPercent, string promptSnapshotJson)
    {
        var profileText = ExtractProfilePrompt(promptSnapshotJson);
        return string.Join("\n", new[]
        {
            profileText,
            $"Timelapse construction progress: {progressPercent}%.",
            $"Profile: {snapshot.ProfileName}."
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static string ResolveVideoPrompt(TimelapseJobSnapshot snapshot, int clipIndex, int startProgress, int endProgress)
        => $"Use the configured Timelapse profile semantics for {snapshot.ProfileName}. Transition clip {clipIndex} from {startProgress}% to {endProgress}% construction progress.";

    private static string ExtractProfilePrompt(string promptSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(promptSnapshotJson))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(promptSnapshotJson);
            if (doc.RootElement.TryGetProperty("profileJson", out var camel))
            {
                return ExtractPromptField(camel);
            }

            if (doc.RootElement.TryGetProperty("ProfileJson", out var pascal))
            {
                return ExtractPromptField(pascal);
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string ExtractPromptField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "prompt", "image_prompt", "video_prompt", "construction_prompt", "base_prompt", "system_prompt" })
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }
            }
        }

        return element.GetRawText();
    }
}
