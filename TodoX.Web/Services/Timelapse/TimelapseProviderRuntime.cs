using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Platform;
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
    private readonly ITimelapseCoreLifecycleBridge _coreLifecycle;
    private readonly IRenderJobService _renderJobs;
    private readonly IConfiguration _configuration;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseProviderRuntime> _logger;

    public TimelapseProviderRuntime(
        AiProviderRepository providerRepository,
        IProviderCredentialResolver credentials,
        IProviderCredentialRepository credentialRepository,
        IAi79TaskClient taskClient,
        IMediaFileService media,
        ITimelapseWorkerRepository repo,
        ITimelapseCoreLifecycleBridge coreLifecycle,
        IRenderJobService renderJobs,
        IConfiguration configuration,
        IOptions<TimelapseProviderWorkerOptions> options,
        ILogger<TimelapseProviderRuntime> logger)
    {
        _providerRepository = providerRepository;
        _credentials = credentials;
        _credentialRepository = credentialRepository;
        _taskClient = taskClient;
        _media = media;
        _repo = repo;
        _coreLifecycle = coreLifecycle;
        _renderJobs = renderJobs;
        _configuration = configuration;
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
            await _coreLifecycle.AdvanceAsync(
                item.JobId,
                item.UserId,
                item.CustomerId,
                item.Snapshot,
                ct);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "TIMELAPSE_IMAGE_CANCELLED jobId={JobId} progress={Progress} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ProgressPercent, item.Attempt, item.ProviderTaskId);
            await _repo.ReleaseImageClaimAsync(item.Id, item.Attempt, CancellationToken.None);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }
        catch (Ai79TaskPollException ex) when (IsTransientPollFailure(ex))
        {
            _logger.LogWarning(ex, "TIMELAPSE_IMAGE_POLL_TRANSIENT jobId={JobId} progress={Progress} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ProgressPercent, item.Attempt, item.ProviderTaskId);
            await _repo.ReleaseImageClaimAsync(item.Id, item.Attempt, CancellationToken.None);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_POLL_TRANSIENT", "Timelapse image poll had a transient provider error.",
                new { item.ProgressPercent, item.Attempt, taskId = item.ProviderTaskId, httpStatus = ex.HttpStatusCode is null ? (int?)null : (int)ex.HttpStatusCode }, "warning", CancellationToken.None);
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
            await _coreLifecycle.AdvanceAsync(
                item.JobId,
                item.UserId,
                item.CustomerId,
                item.Snapshot,
                ct);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "TIMELAPSE_VIDEO_CANCELLED jobId={JobId} clip={ClipIndex} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ClipIndex, item.Attempt, item.ProviderTaskId);
            await _repo.ReleaseVideoClaimAsync(item.Id, item.Attempt, CancellationToken.None);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }
        catch (Ai79TaskPollException ex) when (IsTransientPollFailure(ex))
        {
            _logger.LogWarning(ex, "TIMELAPSE_VIDEO_POLL_TRANSIENT jobId={JobId} clip={ClipIndex} attempt={Attempt} taskId={TaskId}",
                item.JobId, item.ClipIndex, item.Attempt, item.ProviderTaskId);
            await _repo.ReleaseVideoClaimAsync(item.Id, item.Attempt, CancellationToken.None);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_POLL_TRANSIENT", "Timelapse video poll had a transient provider error.",
                new { item.ClipIndex, item.Attempt, taskId = item.ProviderTaskId, httpStatus = ex.HttpStatusCode is null ? (int?)null : (int)ex.HttpStatusCode }, "warning", CancellationToken.None);
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
        var reference = await ResolveImageReferenceAsync(item, ct);
        var prompt = TimelapsePromptResolver.ResolveImagePrompt(item.Snapshot, item.ProgressPercent, item.PromptSnapshotJson);
        var request = BuildImageSubmitRequest(provider, prompt, reference, NormalizeImageRatio(item.Snapshot.Ratio));

        Ai79TaskSubmitResult submit;
        try
        {
            submit = await _taskClient.SubmitAsync(request.Raw, ct);
        }
        catch (Ai79TaskSubmitException ex)
        {
            await _repo.SaveImageSubmitFailedAsync(
                item.Id,
                item.Attempt,
                provider.ProviderCode,
                provider.Model,
                ex.ErrorCode ?? "submit_failed",
                ex.ErrorMessage,
                request.SanitizedJson,
                ex.SanitizedResponseJson,
                ct);
            _logger.LogError(
                ex,
                "TIMELAPSE_IMAGE_SUBMIT_FAILED jobId={JobId} progress={Progress} attempt={Attempt} provider={ProviderCode} model={Model} httpStatus={HttpStatus} errorCode={ErrorCode}",
                item.JobId,
                item.ProgressPercent,
                item.Attempt,
                provider.ProviderCode,
                provider.Model,
                ex.HttpStatusCode is null ? null : (int)ex.HttpStatusCode,
                ex.ErrorCode);
            await TryAddSubmitFailureEventAsync(
                item.JobId,
                "TIMELAPSE_IMAGE_FAILED",
                "Timelapse image submit failed.",
                new { item.ProgressPercent, item.Attempt, provider.ProviderCode, model = provider.Model, errorCode = ex.ErrorCode, errorMessage = ex.ErrorMessage },
                ct);
            await _coreLifecycle.FailAsync(
                item.JobId,
                item.Snapshot,
                ex.ErrorCode ?? "submit_failed",
                ex.ErrorMessage,
                CoreFailureBillingPolicy.ReleaseReservation,
                ct);
            return;
        }

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

        var prompt = TimelapsePromptResolver.ResolveVideoPrompt(
            item.Snapshot,
            item.ClipIndex,
            item.StartProgressPercent,
            item.EndProgressPercent,
            FirstNonBlank(item.StartPromptSnapshotJson, item.EndPromptSnapshotJson));
        var resolution = ResolveVideoResolution(item.VideoMode);
        var startDescriptor = await BuildVideoImageDescriptorAsync(
            provider,
            item.StartProgressPercent,
            item.StartMediaId,
            item.StartPublicUrl!,
            item.StartObjectKey,
            item.StartResponseJson,
            ct);
        var endDescriptor = await BuildVideoImageDescriptorAsync(
            provider,
            item.EndProgressPercent,
            item.EndMediaId,
            item.EndPublicUrl!,
            item.EndObjectKey,
            item.EndResponseJson,
            ct);
        var imagesJson = JsonSerializer.Serialize(new[] { startDescriptor, endDescriptor }, JsonOptions);
        var request = BuildSubmitRequest(provider, prompt, [], new Dictionary<string, string?>
        {
            ["type"] = "video",
            ["duration"] = item.DurationSeconds.ToString(),
            ["mode"] = item.VideoMode,
            ["ratio"] = NormalizeRatio(item.Ratio),
            ["resolution"] = resolution,
            ["privacy"] = "PRIVATE",
            ["translate_to_en"] = "false",
            ["project_id"] = _options.DefaultImageProjectId,
            ["images"] = imagesJson,
            ["start_progress_percent"] = item.StartProgressPercent.ToString(),
            ["end_progress_percent"] = item.EndProgressPercent.ToString()
        }, Ai79TaskOperation.Video, null, null);

        Ai79TaskSubmitResult submit;
        try
        {
            submit = await _taskClient.SubmitAsync(request.Raw, ct);
        }
        catch (Ai79TaskSubmitException ex)
        {
            await _repo.SaveVideoSubmitFailedAsync(
                item.Id,
                item.Attempt,
                provider.ProviderCode,
                provider.Model,
                ex.ErrorCode ?? "submit_failed",
                ex.ErrorMessage,
                request.SanitizedJson,
                ex.SanitizedResponseJson,
                ct);
            _logger.LogError(
                ex,
                "TIMELAPSE_VIDEO_SUBMIT_FAILED jobId={JobId} clip={ClipIndex} attempt={Attempt} provider={ProviderCode} model={Model} httpStatus={HttpStatus} errorCode={ErrorCode}",
                item.JobId,
                item.ClipIndex,
                item.Attempt,
                provider.ProviderCode,
                provider.Model,
                ex.HttpStatusCode is null ? null : (int)ex.HttpStatusCode,
                ex.ErrorCode);
            await TryAddSubmitFailureEventAsync(
                item.JobId,
                "TIMELAPSE_VIDEO_FAILED",
                "Timelapse video submit failed.",
                new { item.ClipIndex, item.Attempt, provider.ProviderCode, model = provider.Model, errorCode = ex.ErrorCode, errorMessage = ex.ErrorMessage },
                ct);
            await _coreLifecycle.FailAsync(
                item.JobId,
                item.Snapshot,
                ex.ErrorCode ?? "submit_failed",
                ex.ErrorMessage,
                CoreFailureBillingPolicy.ReleaseReservation,
                ct);
            return;
        }

        await _repo.SaveVideoSubmittedAsync(item.Id, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId, request.SanitizedJson, submit.SanitizedResponseJson, ct);
        _logger.LogInformation("TIMELAPSE_VIDEO_SUBMIT jobId={JobId} clip={ClipIndex} attempt={Attempt} provider={ProviderCode} model={Model} taskId={TaskId}",
            item.JobId, item.ClipIndex, item.Attempt, provider.ProviderCode, provider.Model, submit.TaskId);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_SUBMIT", "Timelapse video task submitted to 79AI.",
            new { item.ClipIndex, item.Attempt, provider.ProviderCode, model = provider.Model, taskId = submit.TaskId }, ct: ct);
    }

    private string ResolveVideoResolution(string? videoMode)
    {
        var configuredResolution = videoMode?.Trim().ToLowerInvariant() switch
        {
            TimelapseRequestRules.FastMode => _options.DefaultVideoResolution,
            TimelapseRequestRules.ProfessionalMode => _options.DefaultVideoResolution,
            _ => _options.DefaultVideoResolution
        };

        return TimelapseProviderWorkerOptions.NormalizeVideoResolution(configuredResolution);
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
            taskId,
            isImage ? Ai79TaskOperation.Image : Ai79TaskOperation.Video),
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
        var imageMode = isImage
            ? FirstNonBlank(
                ReadString(capability.ConfigJson, "mode"),
                ReadString(capability.ConfigJson, "default_mode"),
                ReadString(provider.ConfigJson, "image_mode"),
                ReadString(provider.ConfigJson, "default_image_mode"),
                _options.DefaultImageMode)
            : null;
        var imageResolution = isImage
            ? FirstNonBlank(
                ReadString(capability.ConfigJson, "resolution"),
                ReadString(capability.ConfigJson, "default_resolution"),
                ReadString(provider.ConfigJson, "image_resolution"),
                ReadString(provider.ConfigJson, "default_image_resolution"),
                _options.DefaultImageResolution)
            : null;

        return new Ai79RuntimeProvider(
            option.ProviderCode,
            model,
            baseUrl,
            submitPath,
            pollPath,
            domain,
            credential,
            imageMode,
            imageResolution);
    }

    private SubmitRequestEnvelope BuildImageSubmitRequest(
        Ai79RuntimeProvider provider,
        string prompt,
        ImageReferencePayload reference,
        string ratio)
    {
        var mode = provider.ImageMode
            ?? throw new InvalidOperationException("Missing 79AI Timelapse image mode.");
        var resolution = provider.ImageResolution
            ?? throw new InvalidOperationException("Missing 79AI Timelapse image resolution.");
        var options = new Dictionary<string, string?>
        {
            ["action_type"] = "create",
            ["editImage"] = "true",
            ["project_id"] = _options.DefaultImageProjectId,
            ["subjects"] = "[]",
            ["ratio"] = ratio,
            ["resolution"] = resolution,
            ["mode"] = mode
        };
        var raw = new Ai79TaskSubmitRequest(
            provider.BaseUrl,
            provider.SubmitPath,
            provider.Credential.Secret,
            provider.Domain,
            provider.Model,
            prompt,
            [reference.DataUri],
            options,
            Ai79TaskOperation.Image,
            _options.DefaultImageReferenceField);
        var sanitized = JsonSerializer.Serialize(new
        {
            provider.ProviderCode,
            provider.Model,
            provider.BaseUrl,
            endpointPath = provider.SubmitPath,
            provider.Domain,
            prompt,
            action_type = "create",
            editImage = true,
            project_id = _options.DefaultImageProjectId,
            subjects = Array.Empty<string>(),
            ratio,
            resolution,
            mode,
            base64ImagePresent = true,
            base64ImageMime = reference.MimeType,
            base64ImageBytes = reference.Bytes
        }, JsonOptions);
        return new SubmitRequestEnvelope(raw, sanitized);
    }

    private SubmitRequestEnvelope BuildSubmitRequest(
        Ai79RuntimeProvider provider,
        string prompt,
        IReadOnlyList<string> images,
        IReadOnlyDictionary<string, string?> options,
        Ai79TaskOperation operation,
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
            operation,
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

    private async Task<ImageReferencePayload> ResolveImageReferenceAsync(TimelapseImageWorkItem item, CancellationToken ct)
    {
        MediaFileDto? media = null;
        var mediaId = item.DependencyMediaId;
        if (mediaId is null || mediaId == Guid.Empty)
        {
            mediaId = item.Snapshot.OriginalImage.MediaId;
        }

        if (mediaId is Guid id && id != Guid.Empty)
        {
            media = await _media.GetAsync(id, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(item.DependencyObjectKey))
        {
            media = await _media.GetByObjectKeyAsync(item.DependencyObjectKey, ct);
        }

        var dependencyUrl = item.DependencyPublicUrl ?? item.Snapshot.OriginalImage.PublicUrl;
        if (media is null && !string.IsNullOrWhiteSpace(dependencyUrl))
        {
            media = await _media.GetByPublicUrlAsync(dependencyUrl, ct);
        }

        if (media is null || !media.IsActive)
        {
            throw new InvalidOperationException("Missing Timelapse dependency image in TodoX media storage.");
        }

        var bytes = await _media.ReadBytesAsync(media.Id, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Timelapse dependency image content could not be read.");
        }

        var mimeType = NormalizeImageMime(media.MimeType ?? item.Snapshot.OriginalImage.MimeType);
        return new ImageReferencePayload(
            $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}",
            mimeType,
            bytes.Length);
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

    private async Task<VideoImageDescriptor> BuildVideoImageDescriptorAsync(
        Ai79RuntimeProvider provider,
        int progressPercent,
        Guid? mediaId,
        string publicUrl,
        string? objectKey,
        string? responseJson,
        CancellationToken ct)
    {
        var idBase = ExtractImageIdBase(responseJson);
        var url = ResolveProviderImageUrl(FirstNonBlank(ExtractImageInfoString(responseJson, "url"), publicUrl)!);
        var fileName = FirstNonBlank(ExtractImageInfoString(responseJson, "file_name"), ExtractFileName(url), objectKey, $"timelapse-{progressPercent}.png")!;
        if (!string.IsNullOrWhiteSpace(idBase))
        {
            var projectId = FirstNonBlank(ExtractImageInfoString(responseJson, "project_id"), _options.DefaultImageProjectId)!;
            return new VideoImageDescriptor(idBase!, projectId, url, fileName);
        }

        var media = await ResolveVideoInputMediaAsync(mediaId, objectKey, publicUrl, ct);
        var bytes = await _media.ReadBytesAsync(media.Id, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException($"Timelapse video input image content could not be read for {progressPercent}%.");
        }

        var upload = await _taskClient.UploadImageAsync(new Ai79ImageUploadRequest(
            provider.BaseUrl,
            _options.DefaultImageUploadPath,
            provider.Credential.Secret,
            provider.Domain,
            Convert.ToBase64String(bytes),
            _options.DefaultImageProjectId,
            FirstNonBlank(media.FileName, fileName, $"timelapse-{progressPercent}.jpg")!,
            bytes.Length),
            ct);
        var uploadedUrl = ResolveProviderImageUrl(upload.Url);
        return new VideoImageDescriptor(upload.IdBase, upload.ProjectId, uploadedUrl, upload.FileName);
    }

    private async Task<MediaFileDto> ResolveVideoInputMediaAsync(Guid? mediaId, string? objectKey, string publicUrl, CancellationToken ct)
    {
        MediaFileDto? media = null;
        if (mediaId is Guid id && id != Guid.Empty)
        {
            media = await _media.GetAsync(id, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(objectKey))
        {
            media = await _media.GetByObjectKeyAsync(objectKey, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(publicUrl))
        {
            media = await _media.GetByPublicUrlAsync(publicUrl, ct);
        }

        if (media is null || !media.IsActive)
        {
            throw new InvalidOperationException("Missing Timelapse video input image in TodoX media storage.");
        }

        return media;
    }

    private string ResolveProviderImageUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var publicBaseUrl = FirstNonBlank(
            _configuration["TodoX:PublicBaseUrl"],
            _configuration["App:PublicBaseUrl"],
            _configuration["Storage:PublicBaseUrl"]);
        if (!string.IsNullOrWhiteSpace(publicBaseUrl)
            && Uri.TryCreate(new Uri(publicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute), value.TrimStart('/'), out var resolved)
            && (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
        {
            return resolved.ToString();
        }

        throw new InvalidOperationException("Timelapse video input image URL must be an absolute HTTP(S) URL for 79AI.");
    }

    private static string? ExtractImageIdBase(string? responseJson)
        => ExtractImageInfoString(responseJson, "id_base");

    private static string? ExtractImageInfoString(string? responseJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            foreach (var imageInfo in ImageInfoContainers(doc.RootElement))
            {
                if (imageInfo.ValueKind == JsonValueKind.Object
                    && imageInfo.TryGetProperty(fieldName, out var value))
                {
                    var found = ScalarString(value);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<JsonElement> ImageInfoContainers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("imageInfo", out var imageInfo))
        {
            yield return imageInfo;
        }

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("imageInfo", out var nestedImageInfo))
        {
            yield return nestedImageInfo;
        }
    }

    private static string? ScalarString(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.ToString()
                : null;

    private static string? ExtractFileName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static bool IsTransientPollFailure(Ai79TaskPollException ex)
        => ex.HttpStatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private async Task FailImageAsync(TimelapseImageWorkItem item, string? errorCode, string errorMessage, string responseJson, CancellationToken ct)
    {
        await _repo.SaveImageFailedAsync(item.Id, item.Attempt, errorCode, errorMessage, responseJson, ct);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_IMAGE_FAILED", "Timelapse image task failed.",
            new { item.ProgressPercent, item.Attempt, taskId = item.ProviderTaskId, errorCode, errorMessage }, "error", ct);
        await _coreLifecycle.FailAsync(
            item.JobId,
            item.Snapshot,
            errorCode,
            errorMessage,
            FailurePolicy(item.ProviderTaskId),
            ct);
    }

    private async Task FailVideoAsync(TimelapseVideoWorkItem item, string? errorCode, string errorMessage, string responseJson, CancellationToken ct)
    {
        await _repo.SaveVideoFailedAsync(item.Id, item.Attempt, errorCode, errorMessage, responseJson, ct);
        await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_VIDEO_FAILED", "Timelapse video task failed.",
            new { item.ClipIndex, item.Attempt, taskId = item.ProviderTaskId, errorCode, errorMessage }, "error", ct);
        await _coreLifecycle.FailAsync(
            item.JobId,
            item.Snapshot,
            errorCode,
            errorMessage,
            FailurePolicy(item.ProviderTaskId),
            ct);
    }

    internal static CoreFailureBillingPolicy FailurePolicy(string? providerTaskId)
        => string.IsNullOrWhiteSpace(providerTaskId)
            ? CoreFailureBillingPolicy.ReleaseReservation
            : CoreFailureBillingPolicy.KeepCharge;

    private async Task TryAddSubmitFailureEventAsync(Guid jobId, string eventType, string message, object metadata, CancellationToken ct)
    {
        try
        {
            await _renderJobs.AddEventAsync(jobId, eventType, message, metadata, "error", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TIMELAPSE_SUBMIT_FAILURE_EVENT_WRITE_FAILED jobId={JobId} eventType={EventType}", jobId, eventType);
        }
    }

    private static string BuildObjectKey(Guid jobId, string fileName)
        => $"timelapse/{DateTime.UtcNow:yyyyMM}/{jobId:N}/{fileName}";

    private static string NormalizeRatio(string? ratio)
        => string.Equals(ratio, TimelapseRequestRules.PortraitRatio, StringComparison.OrdinalIgnoreCase) ? "9:16" : "16:9";

    private static string NormalizeImageRatio(string? ratio)
        => (ratio ?? string.Empty).Trim() switch
        {
            "16:9" or "16_9" => "16_9",
            "9:16" or "9_16" => "9_16",
            "1:1" or "1_1" => "1_1",
            _ => throw new InvalidOperationException("Unsupported 79AI Timelapse image ratio.")
        };

    private static string NormalizeImageMime(string? mimeType)
        => (mimeType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ => throw new InvalidOperationException("Timelapse dependency media is not a supported image.")
        };

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

    private sealed record ImageReferencePayload(string DataUri, string MimeType, int Bytes);

    private sealed record VideoImageDescriptor(
        string id_base,
        string project_id,
        string url,
        string file_name);

    private sealed record Ai79RuntimeProvider(
        string ProviderCode,
        string Model,
        string BaseUrl,
        string SubmitPath,
        string PollPath,
        string Domain,
        ResolvedProviderCredential Credential,
        string? ImageMode,
        string? ImageResolution);
}

public static class TimelapsePromptResolver
{
    public static string ResolveImagePrompt(TimelapseJobSnapshot snapshot, int progressPercent, string promptSnapshotJson)
    {
        var customerOverride = TimelapsePromptSnapshot.GetCustomerOverride(promptSnapshotJson);
        if (!string.IsNullOrWhiteSpace(customerOverride))
        {
            return customerOverride;
        }

        var profileText = ExtractProfilePrompt(promptSnapshotJson);
        return string.Join("\n", new[]
        {
            profileText,
            $"Timelapse construction progress: {progressPercent}%.",
            $"Profile: {snapshot.ProfileName}."
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static string ResolveVideoPrompt(
        TimelapseJobSnapshot snapshot,
        int clipIndex,
        int startProgress,
        int endProgress,
        string? promptSnapshotJson = null)
        => string.Join("\n", new[]
        {
            ExtractProfilePrompt(promptSnapshotJson ?? string.Empty),
            $"Use the configured TodoX Construction Timelapse profile semantics for {snapshot.ProfileName}.",
            $"Create clip {clipIndex} as a smooth construction progress transition from {startProgress}% to {endProgress}%.",
            "Use @image1 as the exact starting frame and @image2 as the exact ending frame.",
            "The scene must remain the same building, architecture, footprint, floor count, window/opening layout, roof geometry, camera, lens, perspective, framing, and environment.",
            "Never remove permanent elements visible in @image1.",
            "Do not demolish, reset, rebuild from scratch, duplicate, morph, or scene-cut the construction.",
            "Only add or advance work necessary to reach @image2.",
            "The final frame must converge visually to @image2.",
            "Workers may move naturally and perform temporary construction actions, but they must not alter the architecture randomly.",
            "No subtitles, no captions, no watermarks, no logos, no UI, no text overlays.",
            $"Duration requirement: exactly {TimelapseRequestRules.RuntimeClipDurationSeconds} seconds."
        });

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
