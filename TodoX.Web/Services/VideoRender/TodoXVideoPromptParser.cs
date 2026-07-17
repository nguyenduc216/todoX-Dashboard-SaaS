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
    public string? Voice { get; set; }
    public string? VoiceInstruction { get; set; }
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
    public bool HasDurationMismatch => !HasExplicitScenes && DeclaredDurationSeconds.HasValue && DeclaredDurationSeconds.Value != SceneDurationTotal;
    public bool HasExplicitScenes { get; set; }
    public string? DurationMismatchMessage { get; set; }
}

public sealed class TodoXVideoPromptParseResult
{
    public bool IsTodoXPrompt { get; set; }
    public bool IsJsonValid { get; set; }
    public bool HasInvalidAspectRatio { get; set; }
    public string? InvalidAspectRatio { get; set; }
    public bool HasInvalidResolution { get; set; }
    public string? InvalidResolution { get; set; }
    public bool HasScenes => Model.Scenes.Count > 0;
    public string? ErrorMessage { get; set; }
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
                result.ErrorMessage = "JSON không tạo được dữ liệu.";
                return result;
            }

            result.Model = Normalize(model);
            result.IsJsonValid = true;
            result.IsTodoXPrompt = HasTodoXMetadata(result.Model);
            result.Summary = BuildSummary(result.Model);
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
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
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
            }
        }

        return model;
    }

    private static TodoXVideoPromptModel? DeserializeModel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var model = new TodoXVideoPromptModel
        {
            RawAspectRatio = ReadString(root, "aspect_ratio", "aspectRatio", "video_aspect_ratio", "ratio"),
            RawResolution = ReadString(root, "resolution", "video_resolution", "output_resolution", "quality_resolution"),
            VideoTitle = ReadString(root, "video_title", "title"),
            VideoObjective = ReadString(root, "video_objective", "objective"),
            DurationSeconds = ParseDuration(ReadRaw(root, "duration")),
            Style = ReadString(root, "style"),
            Cta = ReadString(root, "cta"),
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
                    MotionPrompt = ReadString(item, "motion_prompt"),
                    Voice = ReadString(item, "voice"),
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
