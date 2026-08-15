using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoX.Web.Services.Timelapse;

public static class TimelapsePromptSnapshot
{
    public const string CustomerOverrideProperty = "customer_prompt_override";
    public const int MaxPromptLength = 16000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string NormalizePrompt(string? prompt)
    {
        var normalized = prompt?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Prompt không được để trống.");
        }

        if (normalized.Length > MaxPromptLength)
        {
            throw new InvalidOperationException($"Prompt không được vượt quá {MaxPromptLength:N0} ký tự.");
        }

        return normalized;
    }

    public static string WithCustomerOverride(string? promptSnapshotJson, string prompt)
    {
        var root = ParseObjectPreservingOriginal(promptSnapshotJson);
        root[CustomerOverrideProperty] = NormalizePrompt(prompt);
        root["customer_prompt_updated_at_utc"] = DateTimeOffset.UtcNow;
        return root.ToJsonString(JsonOptions);
    }

    public static string? GetCustomerOverride(string? promptSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(promptSnapshotJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(promptSnapshotJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(CustomerOverrideProperty, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static bool CanEdit(string? status)
        => !string.Equals(status, Models.Timelapse.TimelapseOperationStatuses.Rendering, StringComparison.OrdinalIgnoreCase);

    private static JsonObject ParseObjectPreservingOriginal(string? promptSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(promptSnapshotJson))
        {
            return new JsonObject();
        }

        try
        {
            var parsed = JsonNode.Parse(promptSnapshotJson);
            if (parsed is JsonObject obj)
            {
                return obj;
            }

            return new JsonObject
            {
                ["original_profile_snapshot"] = parsed?.DeepClone()
            };
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["original_profile_snapshot_text"] = promptSnapshotJson
            };
        }
    }
}
