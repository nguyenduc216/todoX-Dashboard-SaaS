using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

public static class AiJsonPersistence
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? NormalizeJsonText(string? value, string? emptyFallback = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return emptyFallback;
        }

        var trimmed = value.Trim();
        if (TryNormalizeJson(trimmed, out var normalized))
        {
            return normalized;
        }

        return JsonSerializer.Serialize(trimmed, Options);
    }

    public static string NormalizeObjectJson(string? value, string emptyFallback = "{}")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return emptyFallback;
        }

        var trimmed = value.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return emptyFallback;
        }
    }

    public static string NormalizeJsonPayload(object? value, string emptyFallback = "{}")
    {
        if (value is null)
        {
            return emptyFallback;
        }

        return value switch
        {
            string text => NormalizeJsonText(text, emptyFallback) ?? emptyFallback,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => JsonSerializer.Serialize(value, Options)
        };
    }

    private static bool TryNormalizeJson(string value, out string normalized)
    {
        normalized = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(value);
            normalized = document.RootElement.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
