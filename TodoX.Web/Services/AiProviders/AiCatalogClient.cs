using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiCatalogFetchResult
{
    public bool Configured { get; set; }
    public string? Message { get; set; }
    public string? ImageModelsPath { get; set; }
    public string? VideoModelsPath { get; set; }
    public string? ModelsPath { get; set; }
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
    public List<string> SupportedModes { get; set; } = new();
    public List<int> SupportedDurations { get; set; } = new();
    public List<string> SupportedResolutions { get; set; } = new();
    public List<string> SupportedRatios { get; set; } = new();
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
    private readonly IProviderCredentialResolver _credentialResolver;
    private readonly IProviderCredentialRepository _credentialRepository;

    public Ai79CatalogClient(
        HttpClient httpClient,
        IProviderCredentialResolver credentialResolver,
        IProviderCredentialRepository credentialRepository)
    {
        _httpClient = httpClient;
        _credentialResolver = credentialResolver;
        _credentialRepository = credentialRepository;
    }

    public async Task<AiCatalogFetchResult> FetchAsync(AiProviderDetailDto provider, CancellationToken ct = default)
    {
        var config = ParseConfig(provider.ConfigJson);
        if (Is79AiProvider(provider.ProviderCode))
        {
            var modelsPath = string.IsNullOrWhiteSpace(config.ModelsPath) ? "/models" : config.ModelsPath.Trim();
            if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            {
                return new AiCatalogFetchResult
                {
                    Configured = false,
                    Message = "Model catalog endpoint chưa được cấu hình.",
                    ModelsPath = modelsPath
                };
            }

            return await Fetch79AiAsync(provider, config, modelsPath, ct);
        }

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

    private async Task<AiCatalogFetchResult> Fetch79AiAsync(AiProviderDetailDto provider, AiCatalogPaths config, string modelsPath, CancellationToken ct)
    {
        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentialResolver.ResolveAsync("79ai", "access_token", ct);
        }
        catch
        {
            return new AiCatalogFetchResult
            {
                Configured = false,
                Message = "Không tìm thấy credential 79AI đang hoạt động.",
                ModelsPath = modelsPath
            };
        }

        var domain = await ResolveDomainAsync(credential.ProviderAccountId, provider.ConfigJson, config.Domain, ct);
        var uri = new Uri(new Uri(provider.BaseUrl!.TrimEnd('/') + "/"), modelsPath.TrimStart('/'));
        var models = new List<AiCatalogModelSnapshot>();
        models.AddRange(await Load79AiModelsAsync(uri, credential.Secret, domain, "image", ct));
        models.AddRange(await Load79AiModelsAsync(uri, credential.Secret, domain, "video", ct));

        return new AiCatalogFetchResult
        {
            Configured = true,
            ModelsPath = modelsPath,
            Models = models
        };
    }

    private async Task<IReadOnlyList<AiCatalogModelSnapshot>> Load79AiModelsAsync(Uri uri, string accessToken, string domain, string mediaType, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["access_token"] = accessToken,
            ["domain"] = domain,
            ["type"] = mediaType
        });

        using var response = await _httpClient.PostAsync(uri, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"79AI catalog trả HTTP {(int)response.StatusCode}.");
        }

        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return ParseModels(RemoveSecretEcho(document.RootElement, accessToken), mediaType);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("79AI catalog trả dữ liệu không hợp lệ.", ex);
        }
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
            ModelsPath = ReadString(catalog, "models_path") ?? ReadString(catalog, "modelsPath"),
            ImageModelsPath = ReadString(catalog, "image_models_path") ?? ReadString(catalog, "imageModelsPath"),
            VideoModelsPath = ReadString(catalog, "video_models_path") ?? ReadString(catalog, "videoModelsPath"),
            Domain = ReadString(catalog, "domain") ?? ReadString(root, "domain")
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
                                ?? ReadString(item, "model")
                                ?? ReadString(item, "code")
                                ?? ReadString(item, "id")
                                ?? string.Empty,
            ProviderModelIdBase = ReadString(item, "provider_model_id_base") ?? ReadString(item, "id_base") ?? ReadString(item, "base_id"),
            DisplayName = ReadString(item, "display_name") ?? ReadString(item, "name") ?? ReadString(item, "title") ?? string.Empty,
            MediaType = ReadString(item, "media_type") ?? ReadString(item, "type") ?? fallbackMediaType,
            ServerCode = ReadString(item, "server_code") ?? ReadString(item, "serverCode") ?? ReadString(item, "server"),
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

        model.SupportedModes = ReadStringList(item, "modes", "mode");
        model.SupportedDurations = ReadIntList(item, "durations", "duration", "duration_seconds");
        model.SupportedResolutions = ReadStringList(item, "resolutions", "resolution");
        model.SupportedRatios = ReadStringList(item, "ratios", "ratio");

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
                    RateType = ReadString(price, "rate_type") ?? model.RateType,
                    UnitType = ReadString(price, "unit_type") ?? "scene",
                    ProviderPrice = ReadDecimal(price, "provider_price") ?? ReadDecimal(price, "price"),
                    ProviderPriceDefault = ReadDecimal(price, "provider_price_default") ?? ReadDecimal(price, "price_default"),
                    ProviderPriceUnit = ReadString(price, "provider_price_unit") ?? ReadString(price, "price_unit") ?? model.ProviderPriceUnit ?? "79ai_credit",
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
        else if (model.BaseProviderPrice is decimal basePrice)
        {
            model.Prices.Add(new AiModelPriceDto
            {
                Mode = ReadString(item, "mode"),
                Resolution = ReadString(item, "resolution"),
                DurationSeconds = ReadInt(item, "duration_seconds") ?? ReadInt(item, "duration"),
                Ratio = ReadString(item, "ratio"),
                RateType = model.RateType,
                UnitType = "scene",
                ProviderPrice = basePrice,
                ProviderPriceDefault = ReadDecimal(item, "price_default") ?? ReadDecimal(item, "provider_price_default"),
                ProviderPriceUnit = model.ProviderPriceUnit ?? "79ai_credit",
                SellPriceMode = "AUTO",
                PriceSource = "catalog",
                Active = true
            });
        }

        ApplyVerifiedVeoOmniSeedPrices(model);

        var normalized = AiProviderModelOptionsNormalizer.Normalize(model.SupportedModes, model.SupportedDurations, model.SupportedResolutions, model.SupportedRatios, model.Prices, model.RawJson);
        model.SupportedModes = normalized.Modes;
        model.SupportedDurations = normalized.Durations;
        model.SupportedResolutions = normalized.Resolutions;
        model.SupportedRatios = normalized.Ratios;

        return model;
    }

    private static void ApplyVerifiedVeoOmniSeedPrices(AiCatalogModelSnapshot model)
    {
        var identity = $"{model.ProviderModelCode} {model.DisplayName}".ToLowerInvariant();
        if (!identity.Contains("veo") || !identity.Contains("omni"))
        {
            return;
        }

        AddVerifiedVeoOmniPrice(model, "flash", "720p", 4, 1260, 1400);
        AddVerifiedVeoOmniPrice(model, "flash", "720p", 6, 1800, 2000);
        AddVerifiedVeoOmniPrice(model, "flash", "720p", 8, 2160, 2400);
        AddVerifiedVeoOmniPrice(model, "flash", "720p", 10, 2700, 3000);
        AddVerifiedVeoOmniPrice(model, "flash", "1080p", 4, 1440, 1600);
        AddVerifiedVeoOmniPrice(model, "flash", "1080p", 6, 1980, 2200);
        AddVerifiedVeoOmniPrice(model, "flash", "1080p", 8, 2430, 2700);
        AddVerifiedVeoOmniPrice(model, "flash", "1080p", 10, 2880, 3200);
        AddVerifiedVeoOmniPrice(model, "flash", "4K", 4, 4500, 5000);
        AddVerifiedVeoOmniPrice(model, "flash", "4K", 6, 5400, 6000);
        AddVerifiedVeoOmniPrice(model, "flash", "4K", 8, 6300, 7000);
        AddVerifiedVeoOmniPrice(model, "flash", "4K", 10, 7200, 8000);
    }

    private static void AddVerifiedVeoOmniPrice(
        AiCatalogModelSnapshot model,
        string mode,
        string resolution,
        int durationSeconds,
        decimal providerPrice,
        decimal providerPriceDefault)
    {
        if (model.Prices.Any(x =>
                string.Equals(x.Mode, mode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Resolution, resolution, StringComparison.OrdinalIgnoreCase)
                && x.DurationSeconds == durationSeconds))
        {
            return;
        }

        model.Prices.Add(new AiModelPriceDto
        {
            Mode = mode,
            Resolution = resolution,
            DurationSeconds = durationSeconds,
            RateType = model.RateType,
            UnitType = "scene",
            ProviderPrice = providerPrice,
            ProviderPriceDefault = providerPriceDefault,
            ProviderPriceUnit = model.ProviderPriceUnit ?? "79ai_credit",
            SellPriceMode = "AUTO",
            PriceSource = "verified_seed",
            Active = true
        });
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

    private static List<string> ReadStringList(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Array => value.EnumerateArray()
                    .Select(ReadElementAsString)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? new List<string>() : new List<string> { value.GetString()! },
                JsonValueKind.Number => new List<string> { value.GetRawText() },
                _ => new List<string>()
            };
        }

        return new List<string>();
    }

    private static List<int> ReadIntList(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Array => value.EnumerateArray()
                    .Select(ReadElementAsInt)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                JsonValueKind.Number when value.TryGetInt32(out var number) => new List<int> { number },
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => new List<int> { parsed },
                _ => new List<int>()
            };
        }

        return new List<int>();
    }

    private static string? ReadElementAsString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

    private static int? ReadElementAsInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed)
            ? parsed
            : null;
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
        public string? ModelsPath { get; set; }
        public string? ImageModelsPath { get; set; }
        public string? VideoModelsPath { get; set; }
        public string? Domain { get; set; }
    }

    private static bool Is79AiProvider(string? providerCode)
        => string.Equals(providerCode?.Trim(), "79ai", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ResolveDomainAsync(Guid providerAccountId, string? providerConfigJson, string? configDomain, CancellationToken ct)
    {
        var account = await _credentialRepository.GetAccountByIdAsync(providerAccountId, ct);
        var accountDomain = ReadDomainFromJson(account?.ConfigJson);
        return FirstNonBlank(accountDomain, configDomain, ReadDomainFromJson(providerConfigJson), "79ai.net")!;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string? ReadDomainFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? ReadString(doc.RootElement, "domain")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement RemoveSecretEcho(JsonElement root, string accessToken)
    {
        var sanitized = SanitizeSecretEcho(root, accessToken);
        return JsonSerializer.SerializeToElement(sanitized);
    }

    private static object? SanitizeSecretEcho(JsonElement element, string accessToken)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                x => x.Name,
                x => IsSecretProperty(x.Name) ? "***" : SanitizeSecretEcho(x.Value, accessToken),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(x => SanitizeSecretEcho(x, accessToken)).ToList(),
            JsonValueKind.String => string.Equals(element.GetString(), accessToken, StringComparison.Ordinal) ? "***" : element.GetString(),
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool IsSecretProperty(string name)
        => name.Contains("access_token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("accessToken", StringComparison.OrdinalIgnoreCase)
           || name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase);
}
