using System.Text.Json;

namespace TodoX.Web.Services.PromptAssistant;

public static class PromptAssistantVideoOutputValidator
{
    public static IReadOnlyList<ServicePromptValidationError> Validate(JsonElement root)
    {
        var errors = new List<ServicePromptValidationError>();
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetProperty(root, "scenes", out var scenes)
            || scenes.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new("$.scenes", "required_scenes", "A non-empty scenes array is required."));
            return errors;
        }

        if (scenes.GetArrayLength() == 0)
        {
            errors.Add(new("$.scenes", "required_scenes", "At least one scene is required."));
            return errors;
        }

        var index = 0;
        foreach (var scene in scenes.EnumerateArray())
        {
            var path = $"$.scenes[{index}]";
            if (scene.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(path, "wrong_type", "Each scene must be an object."));
                index++;
                continue;
            }

            if (!TryGetProperty(scene, "duration_seconds", out var duration)
                || duration.ValueKind != JsonValueKind.Number
                || !duration.TryGetInt32(out var seconds)
                || seconds is < 4 or > 8)
            {
                errors.Add(new($"{path}.duration_seconds", "duration_out_of_range", "Scene duration_seconds must be an integer from 4 through 8."));
            }

            RequireText(scene, path, "image_prompt", errors, "image_prompt");
            RequireText(scene, path, "motion_prompt", errors, "motion_prompt", "video_prompt");
            RequireText(scene, path, "voice", errors, "voice", "voice_text", "tts_text", "dialogue", "dialogue_text", "narration", "narration_text", "voice_over", "voiceover", "script");

            if (!TryGetProperty(scene, "tts_rate", out var speed)
                || speed.ValueKind != JsonValueKind.Number
                || !speed.TryGetDecimal(out var rate)
                || rate is < 1.0m or > 1.2m)
            {
                errors.Add(new($"{path}.tts_rate", "tts_rate_out_of_range", "tts_rate must be a number from 1.0 through 1.2."));
            }

            index++;
        }

        return errors;
    }

    private static void RequireText(
        JsonElement scene,
        string path,
        string errorField,
        ICollection<ServicePromptValidationError> errors,
        params string[] propertyNames)
    {
        if (propertyNames.Any(name => TryGetProperty(scene, name, out var value)
                                      && value.ValueKind == JsonValueKind.String
                                      && !string.IsNullOrWhiteSpace(value.GetString())))
        {
            return;
        }

        errors.Add(new($"{path}.{errorField}", "required_text", $"A non-empty {errorField} is required."));
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
