using System.Text.Json;

namespace TodoX.Web.Services.VideoRender;

public sealed class TodoXVideoPromptModel
{
    public string? RawAspectRatio { get; set; }
    public string? AspectRatio { get; set; }
    public string? RawResolution { get; set; }
    public string? Resolution { get; set; }
    public string? VideoTitle { get; set; }
    public string? VideoObjective { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Style { get; set; }
    public string? Cta { get; set; }
    public string? CharacterImageNote { get; set; }
    public List<TodoXVideoScenePromptModel> Scenes { get; set; } = new();
}

public sealed class TodoXVideoScenePromptModel
{
    public int? Scene { get; set; }
    public string? ScenePurpose { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ImagePrompt { get; set; }
    public string? MotionPrompt { get; set; }
    public string? VideoPrompt { get; set; }
    public string? Voice { get; set; }
    public string? VoiceText { get; set; }
    public string? TtsText { get; set; }
    public string? VoiceInstruction { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class TodoXVideoPromptSummary
{
    public string? AspectRatio { get; set; }
    public string? Resolution { get; set; }
    public string? VideoTitle { get; set; }
    public string? VideoObjective { get; set; }
    public string? Style { get; set; }
    public string? Cta { get; set; }
    public int? DeclaredDurationSeconds { get; set; }
    public int SceneDurationTotal { get; set; }
    public int SceneCount { get; set; }
    public string? SceneDurationValidationMessage { get; set; }
    public bool HasDurationMismatch => !HasExplicitScenes && DeclaredDurationSeconds.HasValue && DeclaredDurationSeconds.Value != SceneDurationTotal;
    public bool HasExplicitScenes { get; set; }
    public string? DurationMismatchMessage { get; set; }
}

public sealed class TodoXVideoPromptParseResult
{
    public bool IsTodoXPrompt { get; set; }
    public bool IsTodoXSchemaValid { get; set; }
    public bool IsJsonValid { get; set; }
    public bool HasInvalidAspectRatio { get; set; }
    public string? InvalidAspectRatio { get; set; }
    public bool HasInvalidResolution { get; set; }
    public string? InvalidResolution { get; set; }
    public bool HasScenes => Model.Scenes.Count > 0;
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public TodoXVideoPromptModel Model { get; set; } = new();
    public TodoXVideoPromptSummary Summary { get; set; } = new();
    public string RawText { get; set; } = string.Empty;
}

public interface ITodoXVideoPromptParser
{
    TodoXVideoPromptParseResult Parse(string? input);
}

public sealed class TodoXVideoPromptParser : ITodoXVideoPromptParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public TodoXVideoPromptParseResult Parse(string? input)
    {
        var result = new TodoXVideoPromptParseResult { RawText = input ?? string.Empty };
        if (string.IsNullOrWhiteSpace(input))
        {
            result.ErrorMessage = "Prompt rỗng.";
            return result;
        }

        if (!TryExtractJson(input, out var json))
        {
            result.ErrorMessage = "Không tìm thấy JSON hợp lệ.";
            return result;
        }

        try
        {
            var model = DeserializeModel(json);
            if (model is null)
            {
                result.IsJsonValid = true;
                result.ErrorMessage = "JSON hợp lệ nhưng phải có object gốc để dùng làm TodoX prompt.";
                return result;
            }

            result.Model = Normalize(model);
            result.IsJsonValid = true;
            result.IsTodoXPrompt = HasTodoXMetadata(result.Model);
            result.IsTodoXSchemaValid = HasTodoXSchema(json, result.Model, result.Warnings);
            result.Summary = BuildSummary(result.Model);
            result.Warnings.AddRange(result.Model.Scenes.SelectMany(scene => scene.Warnings));
            AddMetadataWarnings(result.Model, result.Warnings);
            var rawAspectRatio = model.RawAspectRatio;
            if (!string.IsNullOrWhiteSpace(rawAspectRatio) && string.IsNullOrWhiteSpace(result.Model.AspectRatio))
            {
                result.HasInvalidAspectRatio = true;
                result.InvalidAspectRatio = rawAspectRatio;
                result.ErrorMessage = AppendError(result.ErrorMessage, "Render Video Job hiện chỉ hỗ trợ 16:9 hoặc 9:16.");
            }
            var rawResolution = model.RawResolution;
            if (!string.IsNullOrWhiteSpace(rawResolution) && string.IsNullOrWhiteSpace(result.Model.Resolution))
            {
                result.HasInvalidResolution = true;
                result.InvalidResolution = rawResolution;
                result.ErrorMessage = AppendError(result.ErrorMessage, "Độ phân giải không hợp lệ.");
            }
            return result;
        }
        catch (JsonException)
        {
            result.ErrorMessage = "JSON syntax không hợp lệ hoặc chưa parse được.";
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Không thể đọc JSON: {ex.Message}";
            return result;
        }
    }

    private static TodoXVideoPromptModel Normalize(TodoXVideoPromptModel model)
    {
        model.AspectRatio = NormalizeAspectRatio(model.RawAspectRatio ?? model.AspectRatio);
        model.Resolution = NormalizeResolution(model.RawResolution ?? model.Resolution);
        model.DurationSeconds = ParseDuration(model.DurationSeconds?.ToString());
        if (model.Scenes is not null)
        {
            foreach (var scene in model.Scenes)
            {
                scene.DurationSeconds = ParseDuration(scene.DurationSeconds?.ToString());
                scene.MotionPrompt = FirstNonBlank(scene.MotionPrompt, scene.VideoPrompt);
                scene.Voice = FirstNonBlank(scene.Voice, scene.VoiceText, scene.TtsText);
                AddPlaceholderWarning(scene);
            }
        }

        return model;
    }

    private static TodoXVideoPromptModel? DeserializeModel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
            ? metaElement
            : default;
        var model = new TodoXVideoPromptModel
        {
            RawAspectRatio = ReadString(root, "aspect_ratio", "aspectRatio", "video_aspect_ratio", "ratio"),
            RawResolution = ReadString(root, "resolution", "video_resolution", "output_resolution", "quality_resolution"),
            VideoTitle = ReadString(root, "video_title", "title") ?? ReadString(meta, "product_name", "video_title"),
            VideoObjective = ReadString(root, "video_objective", "objective") ?? ReadString(meta, "kieu_kich_ban", "video_objective"),
            DurationSeconds = ParseDuration(ReadRaw(root, "duration_seconds", "duration"))
                ?? ParseDuration(ReadRaw(meta, "total_duration_seconds")),
            Style = ReadString(root, "style") ?? ReadString(meta, "style"),
            Cta = ReadString(root, "cta") ?? ReadString(meta, "cta"),
            CharacterImageNote = ReadString(root, "character_image_note")
        };

        if (root.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in scenes.EnumerateArray())
            {
                model.Scenes.Add(new TodoXVideoScenePromptModel
                {
                    Scene = TryGetInt(item, "scene"),
                    ScenePurpose = ReadString(item, "scene_purpose", "purpose"),
                    DurationSeconds = ParseDuration(ReadRaw(item, "duration_seconds", "duration")),
                    ImagePrompt = ReadString(item, "image_prompt"),
                    MotionPrompt = ReadString(item, "motion_prompt", "video_prompt"),
                    VideoPrompt = ReadString(item, "video_prompt"),
                    Voice = ReadString(item, "voice"),
                    VoiceText = ReadString(item, "voice_text"),
                    TtsText = ReadString(item, "tts_text"),
                    VoiceInstruction = ReadString(item, "voice_instruction")
                });
            }
        }

        return model;
    }

    private static TodoXVideoPromptSummary BuildSummary(TodoXVideoPromptModel model)
    {
        var summary = new TodoXVideoPromptSummary
        {
            AspectRatio = model.AspectRatio,
            Resolution = model.Resolution,
            VideoTitle = model.VideoTitle,
            VideoObjective = model.VideoObjective,
            Style = model.Style,
            Cta = model.Cta,
            DeclaredDurationSeconds = model.DurationSeconds,
            SceneDurationTotal = model.Scenes.Sum(x => x.DurationSeconds ?? 0),
            SceneCount = model.Scenes.Count,
            HasExplicitScenes = model.Scenes.Count > 0
        };

        var sceneLabels = model.Scenes
            .Select((scene, index) => scene.Scene ?? index + 1)
            .ToArray();
        if (sceneLabels.Length > 0 && model.Scenes.All(scene => scene.DurationSeconds is > 0))
        {
            summary.SceneDurationValidationMessage = $"Scene {string.Join("/", sceneLabels)} đủ duration.";
        }

        if (summary.HasDurationMismatch)
        {
            summary.DurationMismatchMessage = $"Tổng thời lượng khai báo là {summary.DeclaredDurationSeconds} giây, nhưng tổng thời lượng của {summary.SceneCount} scene là {summary.SceneDurationTotal} giây.";
        }

        return summary;
    }

    private static bool TryExtractJson(string input, out string json)
    {
        var start = input.IndexOf('{');
        var end = input.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            json = string.Empty;
            return false;
        }

        json = input[start..(end + 1)];
        return true;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var child))
            {
                if (child.ValueKind == JsonValueKind.String) return child.GetString();
                return child.ToString();
            }
        }

        return null;
    }

    private static string? ReadRaw(JsonElement element, params string[] names)
        => ReadString(element, names);

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        var raw = ReadRaw(element, names);
        return ParseDuration(raw);
    }

    private static bool HasTodoXMetadata(TodoXVideoPromptModel model)
        => !string.IsNullOrWhiteSpace(model.AspectRatio)
           || !string.IsNullOrWhiteSpace(model.VideoTitle)
           || !string.IsNullOrWhiteSpace(model.Resolution)
           || !string.IsNullOrWhiteSpace(model.VideoObjective)
           || !string.IsNullOrWhiteSpace(model.Cta)
           || model.Scenes.Count > 0;

    private static bool HasTodoXSchema(string json, TodoXVideoPromptModel model, List<string> warnings)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array)
        {
            warnings.Add("Thiếu trường scenes dạng mảng theo schema TodoX.");
            return false;
        }

        if (model.Scenes.Count == 0)
        {
            warnings.Add("Trường scenes phải có ít nhất một scene.");
            return false;
        }

        var valid = true;
        for (var index = 0; index < model.Scenes.Count; index++)
        {
            var scene = model.Scenes[index];
            if (scene.DurationSeconds is null or <= 0)
            {
                warnings.Add($"Scene {scene.Scene ?? index + 1}: thiếu duration_seconds hợp lệ.");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(scene.ImagePrompt))
            {
                warnings.Add($"Scene {scene.Scene ?? index + 1}: thiếu image_prompt.");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(scene.MotionPrompt))
            {
                warnings.Add($"Scene {scene.Scene ?? index + 1}: thiếu motion_prompt/video_prompt.");
                valid = false;
            }
        }

        return valid;
    }

    private static void AddMetadataWarnings(TodoXVideoPromptModel model, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(model.VideoTitle))
        {
            warnings.Add("Metadata thiếu video title/product name.");
        }

        if (string.IsNullOrWhiteSpace(model.VideoObjective))
        {
            warnings.Add("Metadata thiếu video objective/kieu_kich_ban.");
        }

        if (string.IsNullOrWhiteSpace(model.Style))
        {
            warnings.Add("Metadata thiếu style.");
        }

        if (string.IsNullOrWhiteSpace(model.Cta))
        {
            warnings.Add("Metadata thiếu cta.");
        }
    }

    private static void AddPlaceholderWarning(TodoXVideoScenePromptModel scene)
    {
        if (string.IsNullOrWhiteSpace(scene.ImagePrompt))
        {
            return;
        }

        var text = scene.ImagePrompt.Trim();
        if (text.Contains("[[", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            || text.Contains("THAY BẰNG", StringComparison.OrdinalIgnoreCase)
            || text.Contains("THAY BANG", StringComparison.OrdinalIgnoreCase))
        {
            scene.Warnings.Add($"Scene {scene.Scene ?? 0}: image_prompt đang là placeholder, cần thay bằng prompt/ảnh thực tế trước khi sinh ảnh.");
        }
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().ToLowerInvariant();
        if (int.TryParse(text, out var direct)) return direct;

        text = text.Replace("giây", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace("seconds", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace("second", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace("s", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace(" ", string.Empty);

        if (text.Contains(':'))
        {
            var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var minutes) && int.TryParse(parts[1], out var seconds))
            {
                return minutes * 60 + seconds;
            }
        }

        return int.TryParse(text, out direct) ? direct : null;
    }

    private static string? NormalizeAspectRatio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text switch
        {
            "16:9" => "16:9",
            "9:16" => "9:16",
            _ => null
        };
    }

    private static string? NormalizeResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.ToLowerInvariant() switch
        {
            "720p" => "720p",
            "1080p" => "1080p",
            "4k" => "4K",
            _ => null
        };
    }

    private static string AppendError(string? current, string next)
        => string.IsNullOrWhiteSpace(current) ? next : $"{current} {next}";
}
