using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed record AiProviderModelOptions(
    List<string> Modes,
    List<int> Durations,
    List<string> Resolutions,
    List<string> Ratios);

public static class AiProviderModelOptionsNormalizer
{
    public static AiProviderModelOptions Normalize(
        IEnumerable<string>? explicitModes,
        IEnumerable<int>? explicitDurations,
        IEnumerable<string>? explicitResolutions,
        IEnumerable<string>? explicitRatios,
        IEnumerable<AiModelPriceDto>? prices,
        string? rawJson = null,
        IEnumerable<string?>? capabilityConfigJsons = null)
    {
        var modes = new HashSet<string>(CleanStrings(explicitModes), StringComparer.OrdinalIgnoreCase);
        var durations = new SortedSet<int>((explicitDurations ?? Array.Empty<int>()).Where(x => x > 0));
        var resolutions = new HashSet<string>(CleanStrings(explicitResolutions), StringComparer.OrdinalIgnoreCase);
        var ratios = new HashSet<string>(CleanStrings(explicitRatios), StringComparer.OrdinalIgnoreCase);

        foreach (var price in prices ?? Array.Empty<AiModelPriceDto>())
        {
            Add(modes, price.Mode);
            if (price.DurationSeconds is > 0)
            {
                durations.Add(price.DurationSeconds.Value);
            }
            Add(resolutions, price.Resolution);
            Add(ratios, price.Ratio);
        }

        AddFromRaw(rawJson, modes, durations, resolutions, ratios);
        foreach (var capabilityConfigJson in capabilityConfigJsons ?? Array.Empty<string?>())
        {
            AddFromRaw(capabilityConfigJson, modes, durations, resolutions, ratios);
        }

        return new AiProviderModelOptions(
            modes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            durations.ToList(),
            resolutions.OrderBy(x => ResolutionRank(x)).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ratios.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static string ToConfigJson(AiProviderModelOptions options)
        => JsonSerializer.Serialize(new
        {
            supported_modes = options.Modes,
            supported_durations = options.Durations,
            supported_resolutions = options.Resolutions,
            supported_ratios = options.Ratios
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static IEnumerable<string> CleanStrings(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim());

    private static void Add(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set.Add(value.Trim());
        }
    }

    private static void AddFromRaw(
        string? rawJson,
        HashSet<string> modes,
        SortedSet<int> durations,
        HashSet<string> resolutions,
        HashSet<string> ratios)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            AddStrings(doc.RootElement, modes, "modes", "mode", "supported_modes");
            AddInts(doc.RootElement, durations, "durations", "duration", "duration_seconds", "supported_durations", "duration_options");
            AddStrings(doc.RootElement, resolutions, "resolutions", "resolution", "supported_resolutions", "size", "sizes");
            AddStrings(doc.RootElement, ratios, "ratios", "ratio", "aspect_ratio", "aspect_ratios", "supported_ratios");

            foreach (var arrayName in new[] { "variants", "options", "variant_options", "price_options" })
            {
                if (!doc.RootElement.TryGetProperty(arrayName, out var variants)
                    || variants.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var variant in variants.EnumerateArray())
                {
                    if (variant.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    AddStrings(variant, modes, "modes", "mode", "supported_modes");
                    AddInts(variant, durations, "durations", "duration", "duration_seconds", "supported_durations", "duration_options");
                    AddStrings(variant, resolutions, "resolutions", "resolution", "supported_resolutions", "size", "sizes");
                    AddStrings(variant, ratios, "ratios", "ratio", "aspect_ratio", "aspect_ratios", "supported_ratios");
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void AddStrings(JsonElement root, HashSet<string> target, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    Add(target, ElementToString(item));
                }
            }
            else
            {
                Add(target, ElementToString(value));
            }
        }
    }

    private static void AddInts(JsonElement root, SortedSet<int> target, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    AddInt(target, item);
                }
            }
            else
            {
                AddInt(target, value);
            }
        }
    }

    private static void AddInt(SortedSet<int> target, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            target.Add(number);
        }
        else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed) && parsed > 0)
        {
            target.Add(parsed);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "type", "value", "seconds", "duration", "duration_seconds" })
            {
                if (value.TryGetProperty(name, out var child))
                {
                    AddInt(target, child);
                }
            }
        }
    }

    private static string? ElementToString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

    private static int ResolutionRank(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "480p" => 480,
            "720p" => 720,
            "1080p" => 1080,
            "2k" => 2000,
            "4k" => 4000,
            _ => int.MaxValue
        };
}
