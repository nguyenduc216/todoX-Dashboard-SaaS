using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed class RVideoSceneJsonService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public IReadOnlyList<RVideoSceneEditorItem> Import(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var scenesElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("scenes", out var scenes) ? scenes : throw new InvalidOperationException("RVVIDEO_SCENES_REQUIRED");
        var result = new List<RVideoSceneEditorItem>();
        var index = 1;
        foreach (var item in scenesElement.EnumerateArray())
        {
            var scene = JsonSerializer.Deserialize<RVideoSceneImportItem>(item.GetRawText(), Options) ?? new();
            var purpose = GetFirst(item, "scene_purpose", "scenePurpose", "purpose") ?? scene.ScenePurpose;
            var imagePrompt = GetFirst(item, "image_prompt", "imagePrompt") ?? scene.ImagePrompt;
            var motionPrompt = GetFirst(item, "motion_prompt", "motionPrompt", "video_prompt", "videoPrompt") ?? scene.MotionPrompt;
            var voiceInstruction = GetFirst(item, "voice_instruction", "voiceInstruction") ?? scene.VoiceInstruction;
            var negativePrompt = GetFirst(item, "negative_prompt", "negativePrompt") ?? scene.NegativePrompt;
            var dialogue = GetFirst(item, "voice", "dialogue", "dialogue_text", "tts_text", "narration", "narration_text", "voice_over", "voiceover", "script");
            var duration = GetInt(item, "duration_seconds", "durationSeconds") ?? scene.DurationSeconds ?? 0;
            var rate = GetDecimal(item, "tts_rate", "ttsRate") ?? scene.TtsRate;
            RVideoRules.ValidateScene(new RVideoSceneEditorItem(index, purpose, duration, imagePrompt, motionPrompt, dialogue, voiceInstruction, negativePrompt, rate));
            result.Add(new(index++, purpose, duration, imagePrompt, motionPrompt, dialogue, voiceInstruction, negativePrompt, rate));
        }
        if (result.Count == 0) throw new InvalidOperationException("RVVIDEO_SCENES_REQUIRED");
        return result;
    }

    public string Export(string? title, IEnumerable<RVideoSceneEditorItem> scenes)
        => JsonSerializer.Serialize(new
        {
            video_title = title,
            scenes = scenes.OrderBy(x => x.SceneIndex).Select(x => new
            {
                scene = x.SceneIndex,
                scene_purpose = x.ScenePurpose,
                duration_seconds = x.DurationSeconds,
                image_prompt = x.ImagePrompt,
                motion_prompt = x.MotionPrompt,
                dialogue_text = x.DialogueText,
                voice_instruction = x.VoiceInstruction,
                negative_prompt = x.NegativePrompt,
                tts_rate = x.TtsRate
            })
        }, Options);

    private static string? GetFirst(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }
        return null;
    }

    private static int? GetInt(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!item.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        }
        return null;
    }

    private static decimal? GetDecimal(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!item.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number)) return number;
        }
        return null;
    }
}
