using System.Net;
using System.Text.Json;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public interface IYEScaleImageService : IAiImageProviderService
{
}

public sealed class YEScaleImageService : IYEScaleImageService
{
    private readonly IYEScaleTaskClient _tasks;
    private readonly ILogger<YEScaleImageService> _logger;

    public YEScaleImageService(IYEScaleTaskClient tasks, ILogger<YEScaleImageService> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public async Task<OpenRouterImageResponse> GenerateImageAsync(OpenRouterImageRequest request, CancellationToken cancellationToken = default)
    {
        var config = YEScaleImageModelMapper.ParseConfig(request.ProviderConfigJson, request.CapabilityConfigJson);
        var models = YEScaleImageModelMapper.BuildAttemptChain(request.Model, config);
        if (models.Length == 0)
        {
            return Fail("Chưa cấu hình model ảnh YEScale.");
        }

        var fallbackTrail = new List<object>();
        for (var i = 0; i < models.Length; i++)
        {
            var model = models[i];
            var modelConfig = YEScaleImageModelMapper.ForModel(config, model);
            var submitRequest = YEScaleImageModelMapper.BuildSubmitRequest(request, model, modelConfig);
            var requestJson = JsonSerializer.Serialize(submitRequest, JsonOptions());
            try
            {
                var result = await _tasks.SubmitAndWaitAsync(submitRequest, cancellationToken);
                if (!result.TerminalResponse.IsSuccess)
                {
                    var terminalError = TerminalError.From(result.TerminalResponse, result.TaskId);
                    if (ShouldFallback(terminalError, config) && i < models.Length - 1)
                    {
                        fallbackTrail.Add(new
                        {
                            from = model,
                            to = models[i + 1],
                            taskId = result.TaskId,
                            errorCode = terminalError.Code,
                            reason = terminalError.Reason
                        });
                        _logger.LogWarning("YESCALE_IMAGE_FALLBACK fromModel={FromModel} toModel={ToModel} taskId={TaskId} errorCode={ErrorCode} reason={Reason}",
                            model, models[i + 1], result.TaskId, terminalError.Code, terminalError.Reason);
                        continue;
                    }

                    return Fail(terminalError.SafeMessage ?? "Task ảnh YEScale thất bại.", requestJson, result.TerminalResponseJson, model, result.TaskId);
                }

                var image = ExtractImage(result.TerminalResponse);
                if (image is null)
                {
                    return Fail("Task ảnh YEScale thành công nhưng không tìm thấy URL/base64 ảnh đầu ra.", requestJson, result.TerminalResponseJson, model, result.TaskId);
                }

                _logger.LogInformation("YESCALE_IMAGE_DONE model={Model} taskId={TaskId} durationMs={DurationMs}", model, result.TaskId, result.Duration.TotalMilliseconds);
                return new OpenRouterImageResponse
                {
                    Success = true,
                    ImageUrl = image.Url,
                    ImageBytes = image.Bytes,
                    MimeType = image.MimeType ?? "image/png",
                    ProviderCode = "yescale_task_image",
                    ModelName = model,
                    RawRequestJson = requestJson,
                    RawResponseJson = result.TerminalResponseJson,
                    UsageJson = JsonSerializer.Serialize(new
                    {
                        taskId = result.TaskId,
                        durationMs = result.Duration.TotalMilliseconds,
                        fallbackTrail
                    }, JsonOptions())
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (YEScaleTaskException ex) when (ShouldFallback(ex) && i < models.Length - 1)
            {
                fallbackTrail.Add(new { from = model, to = models[i + 1], taskId = ex.TaskId, errorCode = ex.ErrorCode, reason = ex.ErrorCode ?? ex.StatusCode?.ToString() ?? ex.GetType().Name });
                _logger.LogWarning("YESCALE_IMAGE_FALLBACK fromModel={FromModel} toModel={ToModel} statusCode={StatusCode} errorCode={ErrorCode} taskId={TaskId}",
                    model, models[i + 1], ex.StatusCode, ex.ErrorCode, ex.TaskId);
            }
            catch (Exception ex) when (IsFallbackCandidate(ex) && i < models.Length - 1)
            {
                fallbackTrail.Add(new { from = model, to = models[i + 1], taskId = (string?)null, errorCode = (string?)null, reason = ex.GetType().Name });
                _logger.LogWarning("YESCALE_IMAGE_FALLBACK fromModel={FromModel} toModel={ToModel} error={Error}",
                    model, models[i + 1], ex.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("YESCALE_IMAGE_FAILED model={Model} error={Error}", model, ex.GetType().Name);
                return Fail(ex.Message, requestJson, null, model, null);
            }
        }

        return Fail("YEScale image fallback chain exhausted.");
    }

    private static bool ShouldFallback(YEScaleTaskException ex)
        => ex.IsTransient
           || ex.StatusCode is 408 or 429
           || ex.StatusCode >= 500
           || string.Equals(ex.ErrorCode, "temporarily_unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackCandidate(Exception ex) => ex is HttpRequestException or IOException;

    private static bool ShouldFallback(TerminalError error, YEScaleImageRoutingConfig config)
    {
        if (string.IsNullOrWhiteSpace(error.Code)) return false;
        return config.TransientTerminalErrorCodes.Contains(error.Code.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static ExtractedImage? ExtractImage(YEScaleTaskStatusResponse response)
    {
        var roots = new List<JsonElement>();
        if (response.Extra is not null)
        {
            foreach (var key in new[] { "output", "result", "data", "images" })
            {
                if (response.Extra.TryGetValue(key, out var value))
                {
                    roots.Add(value);
                }
            }
        }

        foreach (var root in roots)
        {
            var found = FindImage(root);
            if (found is not null) return found;
        }

        return null;
    }

    private static ExtractedImage? FindImage(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var urlKey in new[] { "url", "image_url", "imageUrl", "output_url", "outputUrl" })
                {
                    if (element.TryGetProperty(urlKey, out var url) && url.ValueKind == JsonValueKind.String)
                    {
                        var value = url.GetString();
                        if (IsHttpUrl(value)) return new ExtractedImage { Url = value };
                    }
                }

                foreach (var b64Key in new[] { "b64_json", "base64", "base64_image", "image_base64" })
                {
                    if (element.TryGetProperty(b64Key, out var b64) && b64.ValueKind == JsonValueKind.String)
                    {
                        var bytes = TryDecodeImage(b64.GetString(), out var mimeType);
                        if (bytes is not null) return new ExtractedImage { Bytes = bytes, MimeType = mimeType };
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (IsNonOutputMetadataKey(property.Name))
                    {
                        continue;
                    }

                    var nested = FindImage(property.Value);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindImage(item);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (IsHttpImageUrl(text)) return new ExtractedImage { Url = text };
                break;
        }

        return null;
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsNonOutputMetadataKey(string key)
        => key.Contains("metadata", StringComparison.OrdinalIgnoreCase)
           || key.Contains("document", StringComparison.OrdinalIgnoreCase)
           || key.Contains("callback", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpImageUrl(string? value)
    {
        if (!IsHttpUrl(value)) return false;
        var path = new Uri(value!, UriKind.Absolute).AbsolutePath;
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? TryDecodeImage(string? value, out string? mimeType)
    {
        mimeType = null;
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            mimeType = text[5..comma].Split(';')[0];
            text = text[(comma + 1)..];
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
    }

    private static OpenRouterImageResponse Fail(string message, string? requestJson = null, string? responseJson = null, string? model = null, string? taskId = null)
        => new()
        {
            Success = false,
            ProviderCode = "yescale_task_image",
            ModelName = model,
            RawRequestJson = requestJson,
            RawResponseJson = responseJson,
            StatusCode = HttpStatusCode.BadGateway,
            ErrorMessage = string.IsNullOrWhiteSpace(taskId) ? message : $"{message} task_id={taskId}"
        };

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ExtractedImage
    {
        public string? Url { get; set; }
        public byte[]? Bytes { get; set; }
        public string? MimeType { get; set; }
    }

    private sealed class TerminalError
    {
        public string? Code { get; set; }
        public string? Type { get; set; }
        public string? SafeMessage { get; set; }
        public string? TaskId { get; set; }
        public string Reason => Code ?? Type ?? "terminal_failure";

        public static TerminalError From(YEScaleTaskStatusResponse response, string taskId)
        {
            var error = new TerminalError { TaskId = taskId, SafeMessage = "Task ảnh YEScale thất bại." };
            if (response.Error is JsonElement element)
            {
                Apply(element, error);
            }

            return error;
        }

        private static void Apply(JsonElement element, TerminalError error)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                error.SafeMessage = SafeText(element.GetString());
                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;
            error.Code = ReadString(element, "code") ?? ReadString(element, "error_code") ?? ReadString(element, "errorCode");
            error.Type = ReadString(element, "type") ?? ReadString(element, "error_type") ?? ReadString(element, "errorType");
            error.SafeMessage = SafeText(ReadString(element, "message") ?? ReadString(element, "error") ?? error.SafeMessage);
        }

        private static string? ReadString(JsonElement root, string name)
            => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())
                ? el.GetString()
                : null;

        private static string? SafeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var value = text.Trim();
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
