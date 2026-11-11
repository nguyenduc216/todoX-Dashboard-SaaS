using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoX.Web.Services.PromptAssistant;

/// <summary>
/// Brings a prompt agent's video script into the TodoX scene contract before it is validated.
/// Some agents run on the provider side (e.g. 79AI "Người que 2d chibi") with their own schema that TodoX cannot
/// retrain: they write fractional scene lengths (speech + pause) and no motion_prompt. Here:
///  - a fractional duration_seconds is rounded, and a scene shorter than the minimum is lengthened to it
///    (the voice still fits; scenes longer than the maximum are left for the validator to reject);
///  - a missing motion_prompt is written from the scene's own description (visual_core, cast_interaction, fx);
///  - the root duration follows the scene durations.
/// A script that already follows the contract is returned unchanged.
/// </summary>
public static class PromptAssistantVideoOutputNormalizer
{
    private static readonly string[] MotionAliases = ["motion_prompt", "video_prompt"];

    public static string Normalize(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        if (root is not JsonObject obj || Find(obj, "scenes") is not JsonArray scenes || scenes.Count == 0) return json;

        var changed = false;
        var total = 0;
        var allDurations = true;
        foreach (var node in scenes)
        {
            if (node is not JsonObject scene) { allDurations = false; continue; }
            if (NormalizeDuration(scene, out var seconds)) changed = true;
            if (seconds is int value) total += value; else allDurations = false;
            if (FillMotionPrompt(scene)) changed = true;
        }

        if (changed && allDurations && FindKey(obj, "duration") is string durationKey
            && obj[durationKey] is JsonValue current && !(current.TryGetValue<int>(out var existing) && existing == total))
        {
            obj[durationKey] = total;
        }

        return changed ? obj.ToJsonString() : json;
    }

    /// <summary>Validation of the script as it will be saved (normalized first).</summary>
    public static IReadOnlyList<ServicePromptValidationError> ValidateNormalized(JsonElement root)
    {
        using var document = JsonDocument.Parse(Normalize(root.GetRawText()));
        return PromptAssistantVideoOutputValidator.Validate(document.RootElement);
    }

    private static bool NormalizeDuration(JsonObject scene, out int? seconds)
    {
        seconds = null;
        if (FindKey(scene, "duration_seconds") is not string key || scene[key] is not JsonValue value) return false;
        if (value.TryGetValue<int>(out var whole))
        {
            seconds = whole;
            if (whole >= 1 && whole < PromptAssistantVideoOutputValidator.MinSceneSeconds)
            {
                seconds = PromptAssistantVideoOutputValidator.MinSceneSeconds;
                scene[key] = seconds.Value;
                return true;
            }
            return false;
        }

        double raw;
        if (value.TryGetValue<double>(out var number)) raw = number;
        else if (value.TryGetValue<string>(out var text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) raw = parsed;
        else return false;
        if (!double.IsFinite(raw) || raw <= 0) return false;

        var rounded = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        seconds = Math.Max(rounded, PromptAssistantVideoOutputValidator.MinSceneSeconds);
        scene[key] = seconds.Value;
        return true;
    }

    private static bool FillMotionPrompt(JsonObject scene)
    {
        if (MotionAliases.Any(alias => Text(scene, alias) is not null)) return false;
        var parts = new[] { Text(scene, "visual_core"), Text(scene, "cast_interaction"), Text(scene, "action"), Text(scene, "scene_location") }
            .Where(x => x is not null)
            .Select(x => x!.TrimEnd('.', ' '))
            .ToList();
        var fx = new[] { Text(scene, "light_fx"), Text(scene, "object_fx") }.Where(x => x is not null).ToList();
        var description = parts.Count > 0 ? string.Join(". ", parts) + "." : "The scene of the image comes to life.";
        var effects = fx.Count > 0 ? $" Subtle effects: {string.Join(", ", fx)}." : string.Empty;
        scene["motion_prompt"] = $"{description}{effects} Characters move naturally with small expressive gestures; gentle slow camera push-in. "
            + "Keep the composition, art style, colours and every character's identity exactly as in the image.";
        return true;
    }

    private static string? Text(JsonObject obj, string name)
        => FindKey(obj, name) is string key && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static JsonNode? Find(JsonObject obj, string name) => FindKey(obj, name) is string key ? obj[key] : null;

    private static string? FindKey(JsonObject obj, string name)
        => obj.Select(x => x.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
}
