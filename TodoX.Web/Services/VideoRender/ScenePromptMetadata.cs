using System.Text;
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
        "voice_instruction"
    };

    public string? ScenePurpose { get; set; }
    public string? ImagePrompt { get; set; }
    public string? MotionPrompt { get; set; }
    public string? Voice { get; set; }
    public string? VoiceInstruction { get; set; }
    public Dictionary<string, string> Extra { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ScenePromptMetadata FromScene(VideoProjectSceneDto scene)
    {
        var metadata = Parse(scene.ScenePrompt);
        metadata.ImagePrompt = FirstNonBlank(scene.ImagePrompt, metadata.ImagePrompt);
        metadata.MotionPrompt = FirstNonBlank(scene.VideoPrompt, metadata.MotionPrompt);
        return metadata;
    }

    public static ScenePromptMetadata Parse(string? source)
    {
        var metadata = new ScenePromptMetadata();
        if (string.IsNullOrWhiteSpace(source))
        {
            return metadata;
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
        var parts = new List<string>();
        Add(parts, "scene_purpose", ScenePurpose);
        Add(parts, "image_prompt", ImagePrompt);
        Add(parts, "motion_prompt", MotionPrompt);
        Add(parts, "voice", Voice);
        Add(parts, "voice_instruction", VoiceInstruction);

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
            VoiceInstruction = VoiceInstruction
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
            case "motion_prompt":
            case "video_prompt":
                MotionPrompt = value;
                break;
            case "voice":
                Voice = value;
                break;
            case "voice_instruction":
                VoiceInstruction = value;
                break;
            default:
                Extra[key.Trim()] = value;
                break;
        }
    }

    private static string NormalizeKey(string key)
        => key.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

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
