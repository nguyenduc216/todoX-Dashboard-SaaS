using System.Text;
using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed class ScenePromptMetadata
{
    private static readonly string[] KnownKeys =
    {
        "scene_purpose",
        "image_prompt",
        "motion_prompt",
        "voice",
        "voice_text",
        "dialogue",
        "dialogue_text",
        "narration",
        "tts_text",
        "voice_instruction",
        "tts_rate",
        "raw_scene_json",
        "effective_image_prompt"
    };

    public string? ScenePurpose { get; set; }
    public string? ImagePrompt { get; set; }
    public string? MotionPrompt { get; set; }
    public string? Voice { get; set; }
    public string? VoiceInstruction { get; set; }
    public decimal? TtsRate { get; set; }
    public string? RawSceneJson { get; set; }
    public string? EffectiveImagePrompt { get; set; }
    public Dictionary<string, string> Extra { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ScenePromptMetadata FromScene(VideoProjectSceneDto scene)
    {
        var metadata = Parse(scene.ScenePrompt);
        metadata.ImagePrompt = FirstNonBlank(scene.ImagePrompt, metadata.ImagePrompt);
        metadata.MotionPrompt = FirstNonBlank(scene.VideoPrompt, metadata.MotionPrompt);
        metadata.EffectiveImagePrompt = NormalizeEffectiveImagePrompt(metadata.ImagePrompt, metadata.EffectiveImagePrompt);
        return metadata;
    }

    public static string? NormalizeEffectiveImagePrompt(string? imagePrompt, string? fallback)
    {
        var image = TrimOrNull(imagePrompt);
        var usableFallback = IsUsableImagePrompt(fallback) ? fallback!.Trim() : null;
        return IsPlaceholder(image) ? usableFallback : image;
    }

    public static string? NormalizeEditedEffectiveImagePrompt(string? imagePrompt, string? previousImagePrompt, string? previousEffectiveImagePrompt)
    {
        var fallback = IsPlaceholder(previousImagePrompt) ? previousEffectiveImagePrompt : null;
        return NormalizeEffectiveImagePrompt(imagePrompt, fallback);
    }

    public static bool IsUsableImagePrompt(string? value)
        => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value);

    public static ScenePromptMetadata Parse(string? source)
    {
        var metadata = new ScenePromptMetadata();
        if (string.IsNullOrWhiteSpace(source))
        {
            return metadata;
        }

        if (source.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(source);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    metadata.ScenePurpose = ReadJsonString(root, "scene_purpose", "scenePurpose", "purpose");
                    metadata.ImagePrompt = ReadJsonString(root, "image_prompt", "imagePrompt");
                    metadata.MotionPrompt = ReadJsonString(root, "motion_prompt", "motionPrompt", "video_prompt", "videoPrompt");
                    metadata.Voice = ReadJsonString(root, "voice", "voice_text", "dialogue", "dialogue_text", "narration", "tts_text");
                    metadata.VoiceInstruction = ReadJsonString(root, "voice_instruction", "voiceInstruction");
                    metadata.TtsRate = ReadJsonDecimal(root, "tts_rate", "ttsRate", "speech_rate");
                    metadata.RawSceneJson = ReadJsonString(root, "raw_scene_json", "rawSceneJson");
                    metadata.EffectiveImagePrompt = ReadJsonString(root, "effective_image_prompt", "effectiveImagePrompt");
                    foreach (var property in root.EnumerateObject())
                    {
                        if (IsKnownJsonKey(property.Name))
                        {
                            continue;
                        }

                        metadata.Extra[property.Name] = ReadJsonValue(property.Value);
                    }

                    if (root.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in extra.EnumerateObject())
                        {
                            metadata.Extra[property.Name] = ReadJsonValue(property.Value);
                        }
                    }

                    return metadata;
                }
            }
            catch (JsonException)
            {
                // Fall back to the legacy pipe-delimited format below.
            }
        }

        var foundKey = false;
        foreach (var segment in source.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foundKey = true;
            metadata.Set(key, value);
        }

        if (!foundKey)
        {
            metadata.ScenePurpose = source.Trim();
        }

        return metadata;
    }

    public string Serialize()
    {
        if (!string.IsNullOrWhiteSpace(RawSceneJson))
        {
            return JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                scene_purpose = ScenePurpose,
                image_prompt = ImagePrompt,
                effective_image_prompt = EffectiveImagePrompt,
                motion_prompt = MotionPrompt,
                voice = Voice,
                voice_instruction = VoiceInstruction,
                tts_rate = TtsRate,
                raw_scene_json = RawSceneJson,
                extra = Extra
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var parts = new List<string>();
        Add(parts, "scene_purpose", ScenePurpose);
        Add(parts, "image_prompt", ImagePrompt);
        Add(parts, "effective_image_prompt", EffectiveImagePrompt);
        Add(parts, "motion_prompt", MotionPrompt);
        Add(parts, "voice", Voice);
        Add(parts, "voice_instruction", VoiceInstruction);
        if (TtsRate is decimal rate && rate > 0)
        {
            Add(parts, "tts_rate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var item in Extra.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (KnownKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Add(parts, item.Key, item.Value);
        }

        return string.Join(" | ", parts);
    }

    public TodoXVideoScenePromptModel ToPromptModel(int? scene = null, int? durationSeconds = null)
        => new()
        {
            Scene = scene,
            DurationSeconds = durationSeconds,
            ScenePurpose = ScenePurpose,
            ImagePrompt = ImagePrompt,
            MotionPrompt = MotionPrompt,
            Voice = Voice,
            VoiceInstruction = VoiceInstruction,
            RawJson = RawSceneJson,
            EffectiveImagePrompt = EffectiveImagePrompt
        };

    private void Set(string key, string value)
    {
        switch (NormalizeKey(key))
        {
            case "scene_purpose":
            case "purpose":
                ScenePurpose = value;
                break;
            case "image_prompt":
                ImagePrompt = value;
                break;
            case "effective_image_prompt":
                EffectiveImagePrompt = value;
                break;
            case "motion_prompt":
            case "video_prompt":
                MotionPrompt = value;
                break;
            case "voice":
            case "voice_text":
            case "dialogue":
            case "dialogue_text":
            case "narration":
            case "tts_text":
                Voice = value;
                break;
            case "voice_instruction":
                VoiceInstruction = value;
                break;
            case "tts_rate":
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate > 0)
                {
                    TtsRate = rate;
                }
                break;
            default:
                Extra[key.Trim()] = value;
                break;
        }
    }

    private static string NormalizeKey(string key)
        => key.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private static bool IsKnownJsonKey(string key)
        => NormalizeJsonKey(key) is "scenepurpose"
            or "purpose"
            or "imageprompt"
            or "motionprompt"
            or "videoprompt"
            or "voice"
            or "voicetext"
            or "dialogue"
            or "dialoguetext"
            or "narration"
            or "ttstext"
            or "voiceinstruction"
            or "ttsrate"
            or "rawscenejson"
            or "effectiveimageprompt"
            or "schemaversion"
            or "extra";

    private static string NormalizeJsonKey(string key)
        => key.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return text.Contains("[[", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            || text.Contains("THAY BẰNG", StringComparison.OrdinalIgnoreCase)
            || text.Contains("THAY BANG", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var child))
            {
                continue;
            }

            if (child.ValueKind == JsonValueKind.String)
            {
                return child.GetString();
            }

            return child.GetRawText();
        }

        return null;
    }

    private static decimal? ReadJsonDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var child))
            {
                continue;
            }

            if (child.ValueKind == JsonValueKind.Number && child.TryGetDecimal(out var number))
            {
                return number;
            }

            if (child.ValueKind == JsonValueKind.String
                && decimal.TryParse(child.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static string ReadJsonValue(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();

    private static void Add(List<string> parts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parts.Add($"{key}: {NormalizeValue(value)}");
    }

    private static string NormalizeValue(string value)
    {
        var builder = new StringBuilder(value.Trim());
        builder.Replace("\r\n", "\n");
        builder.Replace('\r', '\n');
        builder.Replace('|', '/');
        return builder.ToString().Replace('\n', ' ');
    }
}
