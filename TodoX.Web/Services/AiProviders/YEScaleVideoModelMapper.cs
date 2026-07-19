using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

public sealed class YEScaleVideoRoutingConfig
{
    public string? Mode { get; set; }
    public string? Size { get; set; }
    public int? Duration { get; set; }
    public string? AspectRatio { get; set; }
}

public static class YEScaleVideoModelMapper
{
    private static readonly HashSet<string> GrokVideoAspectRatios = new(StringComparer.OrdinalIgnoreCase) { "2:3", "3:2", "16:9", "9:16", "1:1" };
    private static readonly HashSet<string> GrokVideo15AspectRatios = new(StringComparer.OrdinalIgnoreCase) { "16:9", "9:16" };
    private static readonly HashSet<string> OmniFlashAspectRatios = new(StringComparer.OrdinalIgnoreCase) { "16:9", "9:16" };
    private static readonly HashSet<int> CommonDurations = new() { 4, 6, 8, 10, 12, 15 };
    private static readonly HashSet<string> GrokVideoSizes = new(StringComparer.OrdinalIgnoreCase) { "720P", "1080P" };
    private static readonly HashSet<string> GrokVideo15Sizes = new(StringComparer.OrdinalIgnoreCase) { "720P" };
    private static readonly HashSet<string> OmniFlashModes = new(StringComparer.OrdinalIgnoreCase) { "t2v", "i2v(img_ref)", "i2v(first_last_frame)", "v2v" };

    public static YEScaleTaskSubmitRequest BuildSubmitRequest(
        string model,
        string prompt,
        string imageUrl,
        string aspectRatio,
        string resolution,
        int durationSeconds,
        string? providerConfigJson,
        string? capabilityConfigJson)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Chưa cấu hình model video YEScale.");
        if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("Thiếu prompt video YEScale.");
        if (string.IsNullOrWhiteSpace(imageUrl)) throw new InvalidOperationException("Thiếu ảnh đầu vào cho YEScale video.");

        var normalizedModel = model.Trim();
        var normalizedPrompt = prompt.Trim();
        var config = ParseConfig(providerConfigJson, capabilityConfigJson);
        var normalizedAspectRatio = NormalizeAspectRatio(aspectRatio);
        var normalizedSize = NormalizeResolution(resolution);
        var normalizedDuration = NormalizeDuration(durationSeconds);

        return normalizedModel switch
        {
            "grok-video" => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = normalizedPrompt,
                Config = new YEScaleTaskConfig
                {
                    Images = new[] { imageUrl },
                    AspectRatio = ValidateAspectRatio(normalizedAspectRatio, GrokVideoAspectRatios, normalizedModel),
                    Duration = ValidateDuration(normalizedDuration, normalizedModel),
                    Size = ValidateOptionalValue(normalizedSize, GrokVideoSizes, normalizedModel, "size")
                }
            },
            "grok-video-1.5" => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = normalizedPrompt,
                Config = new YEScaleTaskConfig
                {
                    Images = new[] { imageUrl },
                    AspectRatio = ValidateAspectRatio(normalizedAspectRatio, GrokVideo15AspectRatios, normalizedModel),
                    Duration = ValidateDuration(normalizedDuration, normalizedModel),
                    Size = ValidateRequiredValue(config.Size ?? normalizedSize, GrokVideo15Sizes, normalizedModel, "size")
                }
            },
            "omni-flash" => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = normalizedPrompt,
                Config = new YEScaleTaskConfig
                {
                    Images = new[] { imageUrl },
                    AspectRatio = ValidateAspectRatio(normalizedAspectRatio, OmniFlashAspectRatios, normalizedModel),
                    Mode = ValidateRequiredValue(config.Mode ?? "i2v(img_ref)", OmniFlashModes, normalizedModel, "mode")
                }
            },
            _ => throw new InvalidOperationException($"Model video YEScale '{normalizedModel}' chưa được hỗ trợ trong runtime TodoX.")
        };
    }

    public static YEScaleVideoRoutingConfig ParseConfig(string? providerConfigJson, string? capabilityConfigJson)
    {
        var config = new YEScaleVideoRoutingConfig();
        ApplyConfig(providerConfigJson, config);
        ApplyConfig(capabilityConfigJson, config);
        return config;
    }

    private static void ApplyConfig(string? json, YEScaleVideoRoutingConfig config)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var root = doc.RootElement;
        config.Mode = ReadString(root, "mode") ?? ReadString(root, "video_mode") ?? ReadString(root, "videoMode") ?? config.Mode;
        config.Size = ReadString(root, "size") ?? ReadString(root, "resolution") ?? config.Size;
        config.AspectRatio = ReadString(root, "aspect_ratio") ?? ReadString(root, "aspectRatio") ?? config.AspectRatio;
        config.Duration ??= ReadInt(root, "duration") ?? ReadInt(root, "duration_seconds") ?? ReadInt(root, "durationSeconds");
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()!.Trim()
            : null;

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var number))
        {
            return number;
        }

        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static string NormalizeAspectRatio(string aspectRatio)
    {
        var value = aspectRatio.Trim();
        return value switch
        {
            "16:9" => "16:9",
            "9:16" => "9:16",
            "1:1" => "1:1",
            "3:2" => "3:2",
            "2:3" => "2:3",
            _ => throw new InvalidOperationException("Tỷ lệ video phải là 16:9 hoặc 9:16 cho YEScale video.")
        };
    }

    private static string NormalizeResolution(string resolution)
    {
        var value = resolution.Trim().ToUpperInvariant();
        return value switch
        {
            "720P" => "720P",
            "1080P" => "1080P",
            _ => throw new InvalidOperationException("Độ phân giải video YEScale phải là 720P hoặc 1080P.")
        };
    }

    private static int NormalizeDuration(int durationSeconds)
    {
        if (!CommonDurations.Contains(durationSeconds))
        {
            throw new InvalidOperationException("Thời lượng video YEScale chỉ hỗ trợ 4, 6, 8, 10, 12 hoặc 15 giây.");
        }

        return durationSeconds;
    }

    private static int ValidateDuration(int value, string model)
    {
        if (!CommonDurations.Contains(value))
        {
            throw new InvalidOperationException($"duration '{value}' không được YEScale model '{model}' hỗ trợ.");
        }

        return value;
    }

    private static string ValidateAspectRatio(string value, HashSet<string> allowed, string model)
    {
        if (!allowed.Contains(value))
        {
            throw new InvalidOperationException($"aspect_ratio '{value}' không được YEScale model '{model}' hỗ trợ.");
        }

        return value;
    }

    private static string? ValidateOptionalValue(string? value, HashSet<string> allowed, string model, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ValidateRequiredValue(value, allowed, model, field);
    }

    private static string ValidateRequiredValue(string? value, HashSet<string> allowed, string model, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Thiếu {field} cho YEScale model '{model}'.");
        }

        var normalized = value.Trim();
        if (!allowed.Contains(normalized))
        {
            throw new InvalidOperationException($"{field} '{normalized}' không được YEScale model '{model}' hỗ trợ.");
        }

        return normalized;
    }
}
