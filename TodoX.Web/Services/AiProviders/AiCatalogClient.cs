using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiCatalogFetchResult
{
    public bool Configured { get; set; }
    public string? Message { get; set; }
    public string? ImageModelsPath { get; set; }
    public string? VideoModelsPath { get; set; }
    public List<AiCatalogModelSnapshot> Models { get; set; } = new();
}

public sealed class AiCatalogModelSnapshot
{
    public string ProviderModelCode { get; set; } = string.Empty;
    public string? ProviderModelIdBase { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string? ServerCode { get; set; }
    public string? ProviderStatus { get; set; }
    public string? StatusMessage { get; set; }
    public string? RateType { get; set; }
    public decimal? BaseProviderPrice { get; set; }
    public string? ProviderPriceUnit { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowUserSelect { get; set; } = true;
    public bool IsDeprecated { get; set; }
    public string? Source { get; set; }
    public DateTime? LastProviderSyncAt { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public int FailureCount { get; set; }
    public string? RawJson { get; set; }
    public List<AiProviderModelCapabilityDto> Capabilities { get; set; } = new();
    public List<AiModelPriceDto> Prices { get; set; } = new();
    public List<AiPricingPolicyDto> Policies { get; set; } = new();
}

public interface IAi79CatalogClient
{
    Task<AiCatalogFetchResult> FetchAsync(AiProviderDetailDto provider, CancellationToken ct = default);
}

public sealed class Ai79CatalogClient : IAi79CatalogClient
{
    private readonly HttpClient _httpClient;

    public Ai79CatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AiCatalogFetchResult> FetchAsync(AiProviderDetailDto provider, CancellationToken ct = default)
    {
        var config = ParseConfig(provider.ConfigJson);
        var imagePath = config.ImageModelsPath?.Trim();
        var videoPath = config.VideoModelsPath?.Trim();

        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || (string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(videoPath)))
        {
            return new AiCatalogFetchResult
            {
                Configured = false,
                Message = "Model catalog endpoint chưa được cấu hình",
                ImageModelsPath = imagePath,
                VideoModelsPath = videoPath
            };
        }

        var models = new List<AiCatalogModelSnapshot>();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            models.AddRange(await LoadModelsAsync(provider.BaseUrl!, imagePath, "image", ct));
        }
        if (!string.IsNullOrWhiteSpace(videoPath))
        {
            models.AddRange(await LoadModelsAsync(provider.BaseUrl!, videoPath, "video", ct));
        }

        return new AiCatalogFetchResult
        {
            Configured = true,
            ImageModelsPath = imagePath,
            VideoModelsPath = videoPath,
            Models = models
        };
    }

    private async Task<IReadOnlyList<AiCatalogModelSnapshot>> LoadModelsAsync(string baseUrl, string path, string fallbackMediaType, CancellationToken ct)
    {
        var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
        using var response = await _httpClient.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Catalog fetch failed for {uri.AbsolutePath} with status {(int)response.StatusCode}.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseModels(document.RootElement, fallbackMediaType);
    }

    private static AiCatalogPaths ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AiCatalogPaths();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new AiCatalogPaths();
        }

        var root = doc.RootElement;
        var catalog = root.TryGetProperty("catalog", out var catalogElement) && catalogElement.ValueKind == JsonValueKind.Object
            ? catalogElement
            : root;
        return new AiCatalogPaths
        {
            ImageModelsPath = ReadString(catalog, "image_models_path") ?? ReadString(catalog, "imageModelsPath"),
            VideoModelsPath = ReadString(catalog, "video_models_path") ?? ReadString(catalog, "videoModelsPath")
        };
    }

    private static List<AiCatalogModelSnapshot> ParseModels(JsonElement root, string fallbackMediaType)
    {
        var array = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            JsonValueKind.Object when TryReadArray(root, "models", out var models) => models,
            JsonValueKind.Object when TryReadArray(root, "data", out var data) => data,
            JsonValueKind.Object when TryReadArray(root, "items", out var items) => items,
            _ => new List<JsonElement>()
        };

        var result = new List<AiCatalogModelSnapshot>();
        foreach (var item in array)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(ParseModel(item, fallbackMediaType));
        }

        return result;
    }

    private static bool TryReadArray(JsonElement root, string name, out List<JsonElement> items)
    {
        items = new List<JsonElement>();
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        items = element.EnumerateArray().ToList();
        return true;
    }

    private static AiCatalogModelSnapshot ParseModel(JsonElement item, string fallbackMediaType)
    {
        var rawJson = item.GetRawText();
        var model = new AiCatalogModelSnapshot
        {
            ProviderModelCode = ReadString(item, "provider_model_code")
                                ?? ReadString(item, "model_code")
                                ?? ReadString(item, "code")
                                ?? ReadString(item, "id")
                                ?? string.Empty,
            ProviderModelIdBase = ReadString(item, "provider_model_id_base") ?? ReadString(item, "base_id"),
            DisplayName = ReadString(item, "display_name") ?? ReadString(item, "name") ?? ReadString(item, "title") ?? string.Empty,
            MediaType = ReadString(item, "media_type") ?? ReadString(item, "type") ?? fallbackMediaType,
            ServerCode = ReadString(item, "server_code") ?? ReadString(item, "serverCode"),
            ProviderStatus = ReadString(item, "provider_status") ?? ReadString(item, "status"),
            StatusMessage = ReadString(item, "status_message") ?? ReadString(item, "message"),
            RateType = ReadString(item, "rate_type"),
            BaseProviderPrice = ReadDecimal(item, "base_provider_price") ?? ReadDecimal(item, "provider_price") ?? ReadDecimal(item, "price"),
            ProviderPriceUnit = ReadString(item, "provider_price_unit") ?? ReadString(item, "price_unit"),
            Description = ReadString(item, "description"),
            Enabled = ReadBool(item, "enabled") ?? true,
            AllowUserSelect = ReadBool(item, "allow_user_select") ?? true,
            IsDeprecated = ReadBool(item, "is_deprecated") ?? false,
            Source = ReadString(item, "source") ?? "catalog",
            LastProviderSyncAt = ReadDateTime(item, "last_provider_sync_at"),
            LastHealthCheckAt = ReadDateTime(item, "last_health_check_at"),
            LastSuccessAt = ReadDateTime(item, "last_success_at"),
            LastFailureAt = ReadDateTime(item, "last_failure_at"),
            FailureCount = ReadInt(item, "failure_count") ?? 0,
            RawJson = rawJson
        };

        if (item.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
        {
            foreach (var capability in capabilities.EnumerateArray())
            {
                var capabilityCode = capability.ValueKind == JsonValueKind.Object
                    ? ReadString(capability, "capability_code") ?? ReadString(capability, "code")
                    : capability.ValueKind == JsonValueKind.String ? capability.GetString() : null;
                if (!string.IsNullOrWhiteSpace(capabilityCode))
                {
                    model.Capabilities.Add(new AiProviderModelCapabilityDto
                    {
                        CapabilityCode = capabilityCode.Trim(),
                        Enabled = capability.ValueKind == JsonValueKind.Object ? (ReadBool(capability, "enabled") ?? true) : true,
                        Source = capability.ValueKind == JsonValueKind.Object ? ReadString(capability, "source") : "catalog",
                        ConfigJson = capability.ValueKind == JsonValueKind.Object ? capability.GetRawText() : null
                    });
                }
            }
        }

        if (item.TryGetProperty("prices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            foreach (var price in prices.EnumerateArray())
            {
                if (price.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                model.Prices.Add(new AiModelPriceDto
                {
                    Mode = ReadString(price, "mode"),
                    Resolution = ReadString(price, "resolution"),
                    DurationSeconds = ReadInt(price, "duration_seconds") ?? ReadInt(price, "duration"),
                    Ratio = ReadString(price, "ratio"),
                    RateType = ReadString(price, "rate_type"),
                    UnitType = ReadString(price, "unit_type"),
                    ProviderPrice = ReadDecimal(price, "provider_price") ?? ReadDecimal(price, "price"),
                    ProviderPriceDefault = ReadDecimal(price, "provider_price_default"),
                    ProviderPriceUnit = ReadString(price, "provider_price_unit"),
                    InternalCostPoints = ReadDecimal(price, "internal_cost_points"),
                    SellPoints = ReadDecimal(price, "sell_points"),
                    SellPriceMode = ReadString(price, "sell_price_mode") ?? "AUTO",
                    MarkupPercent = ReadDecimal(price, "markup_percent"),
                    MinimumPoints = ReadDecimal(price, "minimum_points"),
                    RoundingRule = ReadString(price, "rounding_rule"),
                    PriceSource = ReadString(price, "price_source") ?? "catalog",
                    Active = ReadBool(price, "active") ?? true
                });
            }
        }

        return model;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? ReadDateTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class AiCatalogPaths
    {
        public string? ImageModelsPath { get; set; }
        public string? VideoModelsPath { get; set; }
    }
}
