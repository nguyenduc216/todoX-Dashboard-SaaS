using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.Web.Services.AiProviders.SeventyNineAI;

public static class SeventyNineAiConstants
{
    public const string ProviderCode = "79ai";
    public const string DefaultBaseUrl = "https://api.gommo.net";
    public const string DefaultDomain = "79ai.net";
    public const string AccessTokenRole = "access_token";
    public const string CreditCurrency = "79AI_CREDIT";
}

public sealed class SeventyNineAiProviderConfig
{
    public string Domain { get; set; } = SeventyNineAiConstants.DefaultDomain;
    public string ProjectId { get; set; } = "default";
    public string Privacy { get; set; } = "PRIVATE";
    public bool TranslateToEnglish { get; set; } = true;
    public int ImagePollIntervalSeconds { get; set; } = 5;
    public int VideoPollIntervalSeconds { get; set; } = 8;
    public int SuccessWithoutUrlMaxPolls { get; set; } = 10;
    public string BillingMode { get; set; } = "disabled";

    public static SeventyNineAiProviderConfig Parse(string? providerConfigJson, string? capabilityConfigJson = null)
    {
        var config = new SeventyNineAiProviderConfig();
        Apply(providerConfigJson, config);
        Apply(capabilityConfigJson, config);
        return config;
    }

    private static void Apply(string? json, SeventyNineAiProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            config.Domain = ReadString(root, "domain") ?? config.Domain;
            config.ProjectId = ReadString(root, "projectId") ?? ReadString(root, "project_id") ?? config.ProjectId;
            config.Privacy = ReadString(root, "privacy") ?? config.Privacy;
            config.TranslateToEnglish = ReadBool(root, "translateToEnglish") ?? ReadBool(root, "translate_to_en") ?? config.TranslateToEnglish;
            config.ImagePollIntervalSeconds = ReadInt(root, "imagePollIntervalSeconds") ?? config.ImagePollIntervalSeconds;
            config.VideoPollIntervalSeconds = ReadInt(root, "videoPollIntervalSeconds") ?? config.VideoPollIntervalSeconds;
            config.SuccessWithoutUrlMaxPolls = ReadInt(root, "successWithoutUrlMaxPolls") ?? config.SuccessWithoutUrlMaxPolls;
            config.BillingMode = ReadString(root, "billingMode") ?? config.BillingMode;
        }
        catch (JsonException)
        {
            return;
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed class SeventyNineAiExecutionContext
{
    public string BaseUrl { get; set; } = SeventyNineAiConstants.DefaultBaseUrl;
    public string Domain { get; set; } = SeventyNineAiConstants.DefaultDomain;
    public string ProjectId { get; set; } = "default";
    public string AccessToken { get; set; } = string.Empty;
}

public sealed class SeventyNineAiModelInfo
{
    [JsonPropertyName("id_base")]
    public string? IdBase { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Server { get; set; }
    public string? Model { get; set; }
    public string? Status { get; set; }
    public JsonElement? Ratios { get; set; }
    public JsonElement? Resolutions { get; set; }
    public JsonElement? Durations { get; set; }
    public JsonElement? Prices { get; set; }
    public decimal? Price { get; set; }
    public bool? StartText { get; set; }
    public bool? StartImage { get; set; }
    public bool? StartImageAndEnd { get; set; }
    public bool? WithReference { get; set; }
    public bool? ExtendVideo { get; set; }
    public bool? WithLipsync { get; set; }
    public bool? WithMotion { get; set; }
    public JsonElement? Mode { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SeventyNineAiModelsResponse
{
    public List<SeventyNineAiModelInfo> Models { get; set; } = new();
    public string RawJson { get; set; } = "{}";
}

public sealed class SeventyNineAiAccountInfoResponse
{
    [JsonPropertyName("credits_ai")]
    public decimal? CreditsAi { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public string RawJson { get; set; } = "{}";
}

public sealed class SeventyNineAiCreateImageRequest
{
    public string Model { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool EditImage { get; set; }
    public string? Base64Image { get; set; }
    public object? Subjects { get; set; }
    public string Ratio { get; set; } = "1_1";
}

public sealed class SeventyNineAiUploadImageRequest
{
    public string Base64Data { get; set; } = string.Empty;
    public string FileName { get; set; } = "input.png";
    public long Size { get; set; }
}

public sealed class SeventyNineAiCreateVideoRequest
{
    public string Model { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Ratio { get; set; } = "9_16";
    public string Resolution { get; set; } = "720p";
    public int Duration { get; set; }
    public string? Mode { get; set; }
    public IReadOnlyList<string> Images { get; set; } = Array.Empty<string>();
}

public class SeventyNineAiImageSubmitResponse
{
    public SeventyNineAiImageInfo? ImageInfo { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class SeventyNineAiImageStatusResponse : SeventyNineAiImageSubmitResponse
{
}

public sealed class SeventyNineAiUploadImageResponse : SeventyNineAiImageSubmitResponse
{
}

public sealed class SeventyNineAiImageInfo
{
    [JsonPropertyName("id_base")]
    public string? IdBase { get; set; }
    public string? Status { get; set; }
    public string? Url { get; set; }
}

public class SeventyNineAiVideoSubmitResponse
{
    public SeventyNineAiVideoInfo? VideoInfo { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class SeventyNineAiVideoStatusResponse : SeventyNineAiVideoSubmitResponse
{
}

public sealed class SeventyNineAiVideoInfo
{
    [JsonPropertyName("id_base")]
    public string? IdBase { get; set; }

    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    public string? Status { get; set; }

    [JsonPropertyName("credit_fee")]
    public decimal? CreditFee { get; set; }

    public string? Prompt { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    public string? Url { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
