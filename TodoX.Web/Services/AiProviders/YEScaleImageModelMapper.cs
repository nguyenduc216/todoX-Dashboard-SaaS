using System.Text.Json;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public sealed class YEScaleImageRoutingConfig
{
    public string? RoutingRole { get; set; }
    public string? AdapterProfile { get; set; }
    public Dictionary<string, string> ModelProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModelSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] FallbackModels { get; set; } = Array.Empty<string>();
    public string[] TransientTerminalErrorCodes { get; set; } = Array.Empty<string>();
    public string? Size { get; set; }
    public string? Quality { get; set; }
    public string? Background { get; set; }
    public string? GoogleSearch { get; set; }
    public string? Thinking { get; set; }
}

public static class YEScaleImageModelMapper
{
    private const string NanoBananaProfile = "nano_banana_2";
    private const string GptImageProfile = "gpt_image";
    private const string SeedreamProfile = "seedream_5";

    private static readonly HashSet<string> NanoBananaSizes = new(StringComparer.OrdinalIgnoreCase) { "0.5K", "1K", "2K", "4K" };
    private static readonly HashSet<string> GptImageSizes = new(StringComparer.OrdinalIgnoreCase) { "1024x1024", "1024x1536", "1536x1024" };
    private static readonly HashSet<string> SeedreamSizes = new(StringComparer.OrdinalIgnoreCase) { "2K" };
    private static readonly HashSet<string> GptImageQualities = new(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high" };
    private static readonly HashSet<string> GptImageBackgrounds = new(StringComparer.OrdinalIgnoreCase) { "transparent", "opaque", "auto" };

    public static YEScaleTaskSubmitRequest BuildSubmitRequest(OpenRouterImageRequest request, string model, YEScaleImageRoutingConfig config)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Chưa cấu hình model ảnh YEScale.");
        }

        var normalizedModel = model.Trim();
        var images = request.ReferenceImageUrls
            .Where(IsRenderableReferenceUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var profile = NormalizeProfile(config.AdapterProfile);
        return profile switch
        {
            NanoBananaProfile => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = request.Prompt,
                Config = new YEScaleImageTaskConfig
                {
                    Images = images.Length == 0 ? null : images,
                    AspectRatio = NormalizeAspectRatio(request.AspectRatio, allowMatchInput: images.Length > 0),
                    Size = ValidateOrDefault(config.Size ?? request.Resolution, NanoBananaSizes, "1K", normalizedModel, "size"),
                    GoogleSearch = NormalizeOptional(config.GoogleSearch, "disable"),
                    Thinking = NormalizeOptional(config.Thinking, "minimal")
                }
            },
            GptImageProfile => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = request.Prompt,
                Config = new YEScaleImageTaskConfig
                {
                    Images = images.Length == 0 ? null : images,
                    Size = ValidateOrDefault(ToGptImageSize(config.Size ?? request.Resolution, request.AspectRatio), GptImageSizes, "1024x1024", normalizedModel, "size"),
                    Quality = ValidateOrDefault(config.Quality ?? request.Quality, GptImageQualities, "high", normalizedModel, "quality"),
                    Background = ValidateOrDefault(config.Background, GptImageBackgrounds, null, normalizedModel, "background")
                }
            },
            SeedreamProfile => new YEScaleTaskSubmitRequest
            {
                Model = normalizedModel,
                Prompt = request.Prompt,
                Config = new YEScaleImageTaskConfig
                {
                    Images = images.Length == 0 ? null : images,
                    Size = ValidateOrDefault(config.Size ?? request.Resolution, SeedreamSizes, "2K", normalizedModel, "size")
                }
            },
            _ => throw new InvalidOperationException("Chưa cấu hình adapter_profile ảnh YEScale cho model này.")
        };
    }

    public static YEScaleImageRoutingConfig ParseConfig(string? providerConfigJson, string? capabilityConfigJson)
    {
        var config = new YEScaleImageRoutingConfig();
        ApplyConfig(providerConfigJson, config);
        ApplyConfig(capabilityConfigJson, config);
        return config;
    }

    public static string[] BuildAttemptChain(string model, YEScaleImageRoutingConfig config)
    {
        var chain = new List<string>();
        if (!string.IsNullOrWhiteSpace(model)) chain.Add(model.Trim());
        foreach (var fallback in config.FallbackModels.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!chain.Contains(fallback.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                chain.Add(fallback.Trim());
            }
        }

        return chain.ToArray();
    }

    public static YEScaleImageRoutingConfig ForModel(YEScaleImageRoutingConfig config, string model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            var modelKey = model.Trim();
            config.ModelProfiles.TryGetValue(modelKey, out var profile);
            config.ModelSizes.TryGetValue(modelKey, out var size);
            return new YEScaleImageRoutingConfig
            {
                RoutingRole = config.RoutingRole,
                AdapterProfile = string.IsNullOrWhiteSpace(profile) ? config.AdapterProfile : profile,
                ModelProfiles = config.ModelProfiles,
                ModelSizes = config.ModelSizes,
                FallbackModels = config.FallbackModels,
                TransientTerminalErrorCodes = config.TransientTerminalErrorCodes,
                Size = string.IsNullOrWhiteSpace(size) ? config.Size : size,
                Quality = config.Quality,
                Background = config.Background,
                GoogleSearch = config.GoogleSearch,
                Thinking = config.Thinking
            };
        }

        return config;
    }


    private static void ApplyConfig(string? json, YEScaleImageRoutingConfig config)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;
        config.RoutingRole = ReadString(root, "routing_role") ?? ReadString(root, "routingRole") ?? config.RoutingRole;
        config.AdapterProfile = ReadString(root, "adapter_profile") ?? ReadString(root, "adapterProfile") ?? config.AdapterProfile;
        config.Size = ReadString(root, "size") ?? ReadString(root, "resolution") ?? config.Size;
        config.Quality = ReadString(root, "quality") ?? config.Quality;
        config.Background = ReadString(root, "background") ?? config.Background;
        config.GoogleSearch = ReadString(root, "google_search") ?? ReadString(root, "googleSearch") ?? config.GoogleSearch;
        config.Thinking = ReadString(root, "thinking") ?? config.Thinking;
        config.ModelProfiles = ReadStringMap(root, "model_profiles")
            ?? ReadStringMap(root, "modelProfiles")
            ?? config.ModelProfiles;
        config.ModelSizes = ReadStringMap(root, "model_sizes")
            ?? ReadStringMap(root, "modelSizes")
            ?? config.ModelSizes;
        config.FallbackModels = ReadStringArray(root, "fallback_models")
            ?? ReadStringArray(root, "fallbackModels")
            ?? config.FallbackModels;
        config.TransientTerminalErrorCodes = ReadStringArray(root, "transient_terminal_error_codes")
            ?? ReadStringArray(root, "transientTerminalErrorCodes")
            ?? config.TransientTerminalErrorCodes;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()
            : null;

    private static string[]? ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
            .Select(x => x.GetString()!.Trim())
            .ToArray();
    }

    private static Dictionary<string, string>? ReadStringMap(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in el.EnumerateObject())
        {
            if (!string.IsNullOrWhiteSpace(property.Name) &&
                property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                values[property.Name.Trim()] = property.Value.GetString()!.Trim();
            }
        }

        return values;
    }

    private static string? ValidateOrDefault(string? value, HashSet<string> allowed, string? fallback, string model, string field)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (allowed.Contains(candidate)) return candidate;
        throw new InvalidOperationException($"{field} '{candidate}' không được YEScale model '{model}' hỗ trợ.");
    }

    private static string NormalizeOptional(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeProfile(string? profile)
        => (profile ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');

    private static string NormalizeAspectRatio(string? aspectRatio, bool allowMatchInput)
    {
        var value = string.IsNullOrWhiteSpace(aspectRatio) ? "1:1" : aspectRatio.Trim();
        if (allowMatchInput && value.Equals("match_input_image", StringComparison.OrdinalIgnoreCase))
        {
            return "match_input_image";
        }

        return value;
    }

    private static string ToGptImageSize(string? sizeOrResolution, string? aspectRatio)
    {
        var value = string.IsNullOrWhiteSpace(sizeOrResolution) ? string.Empty : sizeOrResolution.Trim();
        if (GptImageSizes.Contains(value)) return value;

        return (aspectRatio ?? string.Empty).Trim() switch
        {
            "9:16" or "2:3" or "3:4" or "4:5" => "1024x1536",
            "16:9" or "3:2" or "4:3" or "5:4" => "1536x1024",
            _ => "1024x1024"
        };
    }

    private static bool IsRenderableReferenceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
               && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps);
    }
}
