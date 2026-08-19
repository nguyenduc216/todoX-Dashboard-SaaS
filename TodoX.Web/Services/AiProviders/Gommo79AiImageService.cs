using System.Text.Json;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public sealed class Gommo79AiImageService : IAiImageProviderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<PolicyEntry> CompatibilityDefaults =
    [
        new("google_image_gen_banana_2", "vip", "1k"),
        new("imagegen_2_0", "low_basic", "1k"),
        new("seedream_4_5", "vip", "2k")
    ];

    private readonly IAi79TaskClient _client;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Gommo79AiImageService> _logger;

    public Gommo79AiImageService(
        IAi79TaskClient client,
        IProviderCredentialResolver credentials,
        IConfiguration configuration,
        ILogger<Gommo79AiImageService> logger)
    {
        _client = client;
        _credentials = credentials;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OpenRouterImageResponse> GenerateImageAsync(OpenRouterImageRequest request, CancellationToken cancellationToken = default)
    {
        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentials.ResolveAsync("79ai", "access_token", cancellationToken);
        }
        catch (Exception ex)
        {
            return Failure("Không tìm thấy credential 79AI đang hoạt động.", request, ex.Message);
        }

        var providerConfig = ParseObject(request.ProviderConfigJson);
        var capabilityConfig = ParseObject(request.CapabilityConfigJson);
        var baseUrl = FirstNonBlank(
            request.BaseUrlOverride,
            ReadString(providerConfig, "base_url"),
            _configuration["TimelapseProviderWorkers:Default79AiBaseUrl"],
            "https://api.gommo.net/ai")!.TrimEnd('/');
        var domain = FirstNonBlank(ReadString(capabilityConfig, "domain"), ReadString(providerConfig, "domain"), "79ai.net")!;
        var projectId = FirstNonBlank(ReadString(capabilityConfig, "project_id"), ReadString(providerConfig, "project_id"), "default")!;
        var submitPath = FirstNonBlank(request.EndpointPath, ReadString(capabilityConfig, "submit_path"), "/generateImage")!;
        var pollPath = FirstNonBlank(ReadString(capabilityConfig, "poll_path"), "/image")!;
        var listPath = FirstNonBlank(ReadString(capabilityConfig, "list_path"), "/images")!;
        var policy = ReadPolicy(capabilityConfig, providerConfig);
        var modelName = FirstNonBlank(request.RequestedModel, request.Model, policy.FirstOrDefault()?.Model)
            ?? "google_image_gen_banana_2";
        var model = policy.FirstOrDefault(x => string.Equals(x.Model, modelName, StringComparison.OrdinalIgnoreCase))
            ?? new PolicyEntry(modelName, "vip", "1k");
        var requestJson = JsonSerializer.Serialize(new
        {
            provider = "79ai",
            endpoint = submitPath,
            model = model.Model,
            mode = model.Mode,
            resolution = model.Resolution,
            ratio = NormalizeRatio(request.AspectRatio)
        }, JsonOptions);

        try
        {
            var options = new Dictionary<string, string?>
            {
                ["action_type"] = ReadString(capabilityConfig, "action_type") ?? "create",
                ["ratio"] = NormalizeRatio(request.AspectRatio),
                ["mode"] = model.Mode,
                ["resolution"] = model.Resolution,
                ["editImage"] = request.ReferenceImageBase64 is null ? "false" : "true",
                ["project_id"] = projectId
            };
            if (request.ReferenceImageBase64 is not null)
            {
                options["base64Image"] = request.ReferenceImageBase64;
                options["subjects"] = JsonSerializer.Serialize(request.ReferenceImageUrls, JsonOptions);
            }

            var taskId = request.ProviderTaskId;
            if (string.IsNullOrWhiteSpace(taskId))
            {
                var submitted = await _client.SubmitAsync(new Ai79TaskSubmitRequest(
                    baseUrl, submitPath, credential.Secret, domain, model.Model, request.Prompt,
                    Array.Empty<string>(), options, Ai79TaskOperation.Image), cancellationToken);
                taskId = submitted.TaskId;
                await ReportAsync(request, "SCENE_IMAGE_PROVIDER_SUBMITTED", new
                {
                    provider = "79ai", model = model.Model, providerTaskId = taskId, providerStatus = "PENDING_ACTIVE"
                });
                return Pending(request, new Ai79TaskStatusResult(
                    Ai79TaskStatusNormalizer.Running, submitted.SanitizedResponseJson, null, null, null),
                    model.Model, taskId);
            }

            var terminal = await _client.GetStatusAsync(new Ai79TaskStatusRequest(
                baseUrl, pollPath, credential.Secret, domain, taskId, Ai79TaskOperation.Image,
                TaskIdField: "id_base"), cancellationToken);
            if (string.Equals(terminal.NormalizedStatus, Ai79TaskStatusNormalizer.Running, StringComparison.OrdinalIgnoreCase))
            {
                await ReportAsync(request, "SCENE_IMAGE_PROVIDER_PROCESSING", new
                {
                    provider = "79ai", model = model.Model, providerTaskId = taskId, providerStatus = "PENDING_PROCESSING"
                });
                return Pending(request, terminal, model.Model, taskId);
            }

            if (!string.Equals(terminal.NormalizedStatus, Ai79TaskStatusNormalizer.Success, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(terminal.ErrorMessage ?? "79AI image task failed.", request,
                    terminal.SanitizedResponseJson, model.Model, taskId);
            }

            var imageUrl = terminal.OutputUrl;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                await ReportAsync(request, "SCENE_IMAGE_RESULT_DOWNLOADING", new
                {
                    provider = "79ai", model = model.Model, providerTaskId = taskId,
                    providerStatus = "SUCCESS", recovery = "images"
                });
                var recovered = await _client.ListImagesAsync(new Ai79ProviderMediaListRequest(
                    baseUrl, listPath, credential.Secret, domain, projectId,
                    new Dictionary<string, string?> { ["id_base"] = taskId }), cancellationToken);
                imageUrl = recovered.Items.FirstOrDefault(x =>
                    string.Equals(x.IdBase, taskId, StringComparison.OrdinalIgnoreCase))?.Url;
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return Failure("79AI trả về SUCCESS nhưng thiếu URL ảnh.", request,
                    terminal.SanitizedResponseJson, model.Model, taskId);
            }

            await ReportAsync(request, "SCENE_IMAGE_READY", new
            {
                provider = "79ai", model = model.Model, providerTaskId = taskId, providerStatus = "SUCCESS"
            });
            return new OpenRouterImageResponse
            {
                Success = true,
                ExecutionState = AiProviderExecutionState.Success,
                ImageUrl = imageUrl,
                MimeType = "image/jpeg",
                ProviderCode = "79ai_task_image",
                ModelName = model.Model,
                RawRequestJson = requestJson,
                RawResponseJson = terminal.SanitizedResponseJson,
                UsageJson = JsonSerializer.Serialize(new
                {
                    provider = "79ai", providerTaskId = taskId, providerStatus = "SUCCESS"
                }, JsonOptions)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Ai79TaskPollException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RVIDEO_IMAGE_79AI_ATTEMPT_FAILED model={Model} providerTaskId={ProviderTaskId}",
                model.Model, request.ProviderTaskId);
            return Failure(ex.Message, request, ex.Message, model.Model, request.ProviderTaskId);
        }
    }

    private static async Task ReportAsync(OpenRouterImageRequest request, string eventType, object data)
    {
        if (request.ProgressCallback is not null)
        {
            await request.ProgressCallback(eventType, data);
        }
    }

    private static OpenRouterImageResponse Failure(string message, OpenRouterImageRequest request, string? rawResponse, string? model = null, string? taskId = null)
        => new()
        {
            Success = false,
            ExecutionState = AiProviderExecutionState.Failed,
            ProviderCode = "79ai_task_image",
            ModelName = model ?? request.Model,
            RawResponseJson = rawResponse,
            ErrorMessage = message,
            UsageJson = taskId is null
                ? null
                : JsonSerializer.Serialize(new { providerTaskId = taskId }, JsonOptions)
        };

    private static OpenRouterImageResponse Pending(OpenRouterImageRequest request, Ai79TaskStatusResult status, string model, string taskId)
        => new()
        {
            Success = false,
            ExecutionState = AiProviderExecutionState.Pending,
            ProviderCode = "79ai_task_image",
            ModelName = model,
            RawResponseJson = status.SanitizedResponseJson,
            ErrorMessage = "79AI image task is still pending.",
            UsageJson = JsonSerializer.Serialize(new
            {
                provider = "79ai",
                providerTaskId = taskId,
                providerStatus = status.NormalizedStatus,
                pending = true,
                providerResponse = status.SanitizedResponseJson
            }, JsonOptions)
        };

    private static IReadOnlyList<PolicyEntry> ReadPolicy(JsonElement capabilityConfig, JsonElement providerConfig)
    {
        var policy = ReadPolicyArray(capabilityConfig, "models") ?? ReadPolicyArray(providerConfig, "models");
        if (policy is { Count: > 0 }) return policy;
        return CompatibilityDefaults;
    }

    private static List<PolicyEntry>? ReadPolicyArray(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;
        var result = new List<PolicyEntry>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var model = ReadString(item, "model");
            if (string.IsNullOrWhiteSpace(model)) continue;
            result.Add(new(model, ReadString(item, "mode") ?? "vip", ReadString(item, "resolution") ?? "1k"));
        }
        return result;
    }

    private static JsonElement ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonSerializer.SerializeToElement(new { }, JsonOptions);
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { }, JsonOptions);
        }
    }

    private static string NormalizeRatio(string? ratio)
        => string.Equals(ratio, "16:9", StringComparison.Ordinal) ? "16_9" : "9_16";

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record PolicyEntry(string Model, string Mode, string Resolution);
}
