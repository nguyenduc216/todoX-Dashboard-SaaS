using System.Text.Json;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoVideoModelPolicyEntry(
    int AttemptIndex,
    string ProviderCode,
    string Model,
    string? Mode);

public static class RVideoVideoModelPolicy
{
    public const string CapabilityCode = "rvideo_scene_video_generation";
    public const string ProviderCode = "79ai";

    public static readonly IReadOnlyList<RVideoVideoModelPolicyEntry> Models =
    [
        new(0, ProviderCode, "veo_omni", "flash"),
        new(1, ProviderCode, "veo_3_1", "fast"),
        new(2, ProviderCode, "veo_3_1", "lite"),
        new(3, ProviderCode, "grok_video_heavy", null)
    ];

    public static RVideoVideoModelPolicyEntry GetInitial() => Models[0];

    public static RVideoVideoModelPolicyEntry? GetByAttemptIndex(int attemptIndex)
        => Models.FirstOrDefault(x => x.AttemptIndex == attemptIndex);

    public static RVideoVideoModelPolicyEntry? GetNext(int currentAttemptIndex)
        => Models.FirstOrDefault(x => x.AttemptIndex == currentAttemptIndex + 1);

    public static bool Is79AiProvider(string? providerCode)
    {
        var value = (providerCode ?? string.Empty).Trim();
        return value.Equals("79ai", StringComparison.OrdinalIgnoreCase)
               || value.Equals("79ai_video", StringComparison.OrdinalIgnoreCase)
               || value.Equals("79ai_task_video", StringComparison.OrdinalIgnoreCase)
               || value.Equals("gommo_video", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RVideo79AiRuntime(
    long ProviderId,
    long ProviderCapabilityId,
    string ProviderCode,
    string BaseUrl,
    string SubmitPath,
    string PollPath,
    string ImageUploadPath,
    string Domain,
    string ProjectId,
    ResolvedProviderCredential Credential,
    string? ProviderConfigJson,
    string? CapabilityConfigJson,
    decimal UnitCostPoints);

public sealed record RVideo79AiVideoSourceImage(
    Guid? MediaId,
    string? ObjectKey,
    string? PublicUrl,
    string? FileName,
    string? MimeType);

public sealed record RVideo79AiProviderImageAsset(
    string IdBase,
    string ProjectId,
    string Url,
    string FileName,
    string SanitizedResponseJson);

public sealed record RVideo79AiVideoSubmitRequest(
    RVideo79AiRuntime Runtime,
    RVideoVideoModelPolicyEntry Model,
    string Prompt,
    string AspectRatio,
    string Resolution,
    int DurationSeconds,
    RVideo79AiProviderImageAsset? SourceImageAsset,
    IReadOnlyList<RVideo79AiProviderImageAsset> ReferenceImageAssets);

public sealed record RVideo79AiVideoSubmitResult(string TaskId, string SanitizedResponseJson, string SanitizedRequestJson);

public interface IRVideo79AiVideoService
{
    Task<RVideo79AiRuntime> ResolveRuntimeAsync(long providerId, long providerCapabilityId, string providerCode, CancellationToken ct = default);
    Task<RVideo79AiProviderImageAsset> UploadSourceImageAsync(RVideo79AiRuntime runtime, RVideo79AiVideoSourceImage source, CancellationToken ct = default);
    Task<RVideo79AiVideoSubmitResult> SubmitAsync(RVideo79AiVideoSubmitRequest request, CancellationToken ct = default);
    Task<Ai79TaskStatusResult> PollAsync(RVideo79AiRuntime runtime, string taskId, CancellationToken ct = default);
}

public sealed class RVideo79AiVideoService : IRVideo79AiVideoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AiProviderRepository _providerRepository;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderCredentialRepository _credentialRepository;
    private readonly IAi79TaskClient _client;
    private readonly IMediaFileService _media;
    private readonly IConfiguration _configuration;

    public RVideo79AiVideoService(
        AiProviderRepository providerRepository,
        IProviderCredentialResolver credentials,
        IProviderCredentialRepository credentialRepository,
        IAi79TaskClient client,
        IMediaFileService media,
        IConfiguration configuration)
    {
        _providerRepository = providerRepository;
        _credentials = credentials;
        _credentialRepository = credentialRepository;
        _client = client;
        _media = media;
        _configuration = configuration;
    }

    public async Task<RVideo79AiRuntime> ResolveRuntimeAsync(long providerId, long providerCapabilityId, string providerCode, CancellationToken ct = default)
    {
        if (!RVideoVideoModelPolicy.Is79AiProvider(providerCode))
        {
            throw new InvalidOperationException("RVIDEO_VIDEO_PROVIDER_MUST_BE_79AI");
        }

        var provider = await _providerRepository.GetProviderAsync(providerId, ct)
            ?? throw new InvalidOperationException("Configured 79AI provider could not be loaded.");
        var capability = provider.Capabilities.FirstOrDefault(x => x.Id == providerCapabilityId)
            ?? throw new InvalidOperationException("Configured RVIDEO 79AI video capability could not be loaded.");
        var credential = await _credentials.ResolveAsync(providerCode, "access_token", ct);
        var account = await _credentialRepository.GetAccountByIdAsync(credential.ProviderAccountId, ct);
        return new RVideo79AiRuntime(
            providerId,
            providerCapabilityId,
            providerCode,
            FirstNonBlank(provider.BaseUrl, ReadString(account?.ConfigJson, "base_url"), ReadString(provider.ConfigJson, "base_url"), _configuration["TimelapseProviderWorkers:Default79AiBaseUrl"], "https://api.gommo.net/ai")!,
            RequirePath(FirstNonBlank(ReadString(capability.ConfigJson, "submit_path"), ReadString(provider.ConfigJson, "video_submit_path"), capability.EndpointPath, "/create-video"), "/create-video"),
            RequirePath(FirstNonBlank(ReadString(capability.ConfigJson, "poll_path"), ReadString(provider.ConfigJson, "video_poll_path"), "/video"), "/video"),
            RequirePath(FirstNonBlank(ReadString(capability.ConfigJson, "image_upload_path"), ReadString(provider.ConfigJson, "image_upload_path"), _configuration["TimelapseProviderWorkers:DefaultImageUploadPath"], "/image-upload"), "/image-upload"),
            FirstNonBlank(ReadString(account?.ConfigJson, "domain"), ReadString(capability.ConfigJson, "domain"), ReadString(provider.ConfigJson, "domain"), "79ai.net")!,
            FirstNonBlank(ReadString(capability.ConfigJson, "project_id"), ReadString(provider.ConfigJson, "project_id"), _configuration["TimelapseProviderWorkers:DefaultImageProjectId"], "default")!,
            credential,
            provider.ConfigJson,
            capability.ConfigJson,
            capability.UnitCostPoints);
    }

    public async Task<RVideo79AiProviderImageAsset> UploadSourceImageAsync(RVideo79AiRuntime runtime, RVideo79AiVideoSourceImage source, CancellationToken ct = default)
    {
        MediaFileDto? media = null;
        if (source.MediaId is Guid mediaId && mediaId != Guid.Empty)
        {
            media = await _media.GetAsync(mediaId, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(source.ObjectKey))
        {
            media = await _media.GetByObjectKeyAsync(source.ObjectKey, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(source.PublicUrl))
        {
            media = await _media.GetByPublicUrlAsync(source.PublicUrl, ct);
        }

        if (media is null || !media.IsActive)
        {
            throw new InvalidOperationException("RVIDEO_SOURCE_IMAGE_UNAVAILABLE");
        }

        var bytes = await _media.ReadBytesAsync(media.Id, ct);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException("RVIDEO_SOURCE_IMAGE_UNAVAILABLE");
        }

        var upload = await _client.UploadImageAsync(new Ai79ImageUploadRequest(
            runtime.BaseUrl,
            runtime.ImageUploadPath,
            runtime.Credential.Secret,
            runtime.Domain,
            Convert.ToBase64String(bytes),
            runtime.ProjectId,
            FirstNonBlank(media.FileName, source.FileName, "rvideo-source-image.jpg")!,
            bytes.Length), ct);
        return new RVideo79AiProviderImageAsset(upload.IdBase, upload.ProjectId, ResolveProviderImageUrl(upload.Url), upload.FileName, upload.SanitizedResponseJson);
    }

    public async Task<RVideo79AiVideoSubmitResult> SubmitAsync(RVideo79AiVideoSubmitRequest request, CancellationToken ct = default)
    {
        var options = new Dictionary<string, string?>
        {
            ["type"] = "video",
            ["duration"] = Math.Max(1, request.DurationSeconds).ToString(),
            ["ratio"] = NormalizeRatio(request.AspectRatio),
            ["resolution"] = NormalizeResolution(request.Resolution),
            ["privacy"] = "PRIVATE",
            ["translate_to_en"] = "false",
            ["project_id"] = request.Runtime.ProjectId
        };
        var providerImages = new List<RVideo79AiProviderImageAsset>();
        if (request.SourceImageAsset is not null)
        {
            providerImages.Add(request.SourceImageAsset);
        }
        providerImages.AddRange(request.ReferenceImageAssets);
        if (providerImages.Count > 0)
        {
            options["images"] = JsonSerializer.Serialize(providerImages.Select(image => new
            {
                id_base = image.IdBase,
                project_id = image.ProjectId,
                url = image.Url,
                file_name = image.FileName
            }), JsonOptions);
        }
        if (!string.IsNullOrWhiteSpace(request.Model.Mode))
        {
            options["mode"] = request.Model.Mode;
        }
        var imageUrls = providerImages
            .Select(image => image.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToArray();
        var raw = new Ai79TaskSubmitRequest(
            request.Runtime.BaseUrl,
            request.Runtime.SubmitPath,
            request.Runtime.Credential.Secret,
            request.Runtime.Domain,
            request.Model.Model,
            request.Prompt,
            imageUrls,
            options,
            Ai79TaskOperation.Video,
            FirstImageField: "image",
            SecondImageField: "image_2");
        var sanitizedRequest = JsonSerializer.Serialize(new
        {
            provider = "79ai",
            endpoint = request.Runtime.SubmitPath,
            model = request.Model.Model,
            mode = request.Model.Mode,
            ratio = options["ratio"],
            resolution = options["resolution"],
            duration = options["duration"],
            sourceImage = request.SourceImageAsset is null ? null : new
            {
                request.SourceImageAsset.IdBase,
                request.SourceImageAsset.ProjectId,
                request.SourceImageAsset.Url,
                request.SourceImageAsset.FileName
            },
            referenceImages = request.ReferenceImageAssets.Select(image => new
            {
                image.IdBase,
                image.ProjectId,
                image.Url,
                image.FileName
            })
        }, JsonOptions);
        var submit = await _client.SubmitAsync(raw, ct);
        return new RVideo79AiVideoSubmitResult(submit.TaskId, submit.SanitizedResponseJson, sanitizedRequest);
    }

    public Task<Ai79TaskStatusResult> PollAsync(RVideo79AiRuntime runtime, string taskId, CancellationToken ct = default)
        => _client.GetStatusAsync(new Ai79TaskStatusRequest(
            runtime.BaseUrl,
            runtime.PollPath,
            runtime.Credential.Secret,
            runtime.Domain,
            taskId,
            Ai79TaskOperation.Video,
            TaskIdField: "videoId",
            ProjectId: runtime.ProjectId), ct);

    private static string NormalizeRatio(string? ratio)
        => (ratio ?? string.Empty).Trim() switch
        {
            "16:9" or "16_9" => "16:9",
            "9:16" or "9_16" => "9:16",
            "1:1" or "1_1" => "1:1",
            _ => "9:16"
        };

    private static string NormalizeResolution(string? resolution)
        => (resolution ?? "720p").Trim().ToLowerInvariant() switch
        {
            "480p" => "480p",
            "720p" => "720p",
            "1080p" => "1080p",
            "4k" => "4k",
            _ => "720p"
        };

    private static string RequirePath(string? configured, string expected)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return expected;
        }

        if (!string.Equals(configured.Trim().TrimStart('/'), expected.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RVIDEO_79AI_VIDEO_ENDPOINT_CONTRACT_MISMATCH");
        }

        return expected;
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

        throw new InvalidOperationException("RVIDEO_SOURCE_IMAGE_PROVIDER_URL_INVALID");
    }

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
}
