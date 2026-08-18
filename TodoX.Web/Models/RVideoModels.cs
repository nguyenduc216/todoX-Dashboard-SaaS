using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Models;

public static class RVideoExecutionModes
{
    public const string Manual = "MANUAL";
    public const string Auto = "AUTO";
}

public static class RVideoStages
{
    public const string Info = "INFO";
    public const string Scene = "SCENE";
    public const string Image = "IMAGE";
    public const string Video = "VIDEO";
    public const string Result = "RESULT";
}

public static class RVideoVoiceModes
{
    public const string None = "NONE";
    public const string Native = "NATIVE";
    public const string Library = "LIBRARY";
}

public static class RVideoCharacterModes
{
    public const string None = "NONE";
    public const string Upload = "UPLOAD";
    public const string Library = "LIBRARY";
}

public static class RVideoMusicModes
{
    public const string None = "NONE";
    public const string Library = "LIBRARY";
}

public sealed class RVideoJobSettingsDto
{
    public long ProjectId { get; set; }
    public string ExecutionMode { get; set; } = RVideoExecutionModes.Manual;
    public string CurrentStage { get; set; } = RVideoStages.Info;
    public bool SkipCharacter { get; set; }
    public string CharacterMode { get; set; } = "NONE";
    public long? SelectedCharacterId { get; set; }
    public string? CharacterSnapshotJson { get; set; }
    public string VoiceMode { get; set; } = RVideoVoiceModes.None;
    public string? VoiceCatalogCode { get; set; }
    public string? VoiceSnapshotJson { get; set; }
    public decimal DefaultTtsRate { get; set; } = 1.0m;
    public string? MusicCatalogCode { get; set; }
    public string? MusicSnapshotJson { get; set; }
    public decimal MusicVolume { get; set; } = 0.8m;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class RVideoJobSettingsRequest
{
    public string ExecutionMode { get; set; } = RVideoExecutionModes.Manual;
    public bool SkipCharacter { get; set; }
    public string CharacterMode { get; set; } = "NONE";
    public long? SelectedCharacterId { get; set; }
    public object? CharacterSnapshot { get; set; }
    public string VoiceMode { get; set; } = RVideoVoiceModes.None;
    public string? VoiceCatalogCode { get; set; }
    public object? VoiceSnapshot { get; set; }
    public decimal DefaultTtsRate { get; set; } = 1.0m;
    public string? MusicCatalogCode { get; set; }
    public object? MusicSnapshot { get; set; }
    public decimal MusicVolume { get; set; } = 0.8m;
}

public sealed class RVideoSceneImportDocument
{
    public string? VideoTitle { get; set; }
    public List<RVideoSceneImportItem> Scenes { get; set; } = new();
}

public sealed class RVideoSceneImportItem
{
    public int? Scene { get; set; }
    public int? SceneIndex { get; set; }
    public string? ScenePurpose { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ImagePrompt { get; set; }
    public string? MotionPrompt { get; set; }
    public string? VoiceInstruction { get; set; }
    public string? NegativePrompt { get; set; }
    public decimal? TtsRate { get; set; }
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed record RVideoSceneEditorItem(
    int SceneIndex,
    string? ScenePurpose,
    int DurationSeconds,
    string? ImagePrompt,
    string? MotionPrompt,
    string? DialogueText,
    string? VoiceInstruction,
    string? NegativePrompt,
    decimal? TtsRate);

public sealed record RVideoLifecycleDecision(string Stage, bool ShouldQueueVideo, bool ShouldFinalize, bool TerminalFailure);

public static class RVideoRules
{
    public static readonly int[] SupportedDurations = [4, 6, 8, 10];
    public static readonly string[] SupportedAspectRatios = ["16:9", "9:16"];
    public static readonly string[] SupportedResolutions = ["720p", "1080p", "4K"];

    public static string NormalizeExecutionMode(string? value)
        => string.Equals(value, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase)
            ? RVideoExecutionModes.Auto
            : RVideoExecutionModes.Manual;

    public static string NormalizeVoiceMode(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            RVideoVoiceModes.Native => RVideoVoiceModes.Native,
            RVideoVoiceModes.Library => RVideoVoiceModes.Library,
            _ => RVideoVoiceModes.None
        };

    public static string NormalizeCharacterMode(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            RVideoCharacterModes.Upload => RVideoCharacterModes.Upload,
            RVideoCharacterModes.Library => RVideoCharacterModes.Library,
            _ => RVideoCharacterModes.None
        };

    public static string NormalizeMusicMode(string? value, string? catalogCode)
        => string.IsNullOrWhiteSpace(catalogCode) ? RVideoMusicModes.None : RVideoMusicModes.Library;

    public static string? NormalizeAspectRatio(string? value)
        => SupportedAspectRatios.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? NormalizeResolution(string? value)
        => SupportedResolutions.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static (string AspectRatio, string Resolution) ResolveRenderSettings(string? prompt, string? fallbackAspectRatio = null, string? fallbackResolution = null)
    {
        var parsed = new TodoXVideoPromptParser().Parse(prompt);
        var aspect = NormalizeAspectRatio(parsed.Model.AspectRatio)
            ?? NormalizeAspectRatio(fallbackAspectRatio)
            ?? "16:9";
        var resolution = NormalizeResolution(parsed.Model.Resolution)
            ?? NormalizeResolution(fallbackResolution)
            ?? "720p";
        return (aspect, resolution);
    }

    public static void ValidateSettings(RVideoJobSettingsRequest request)
    {
        request.ExecutionMode = NormalizeExecutionMode(request.ExecutionMode);
        request.VoiceMode = NormalizeVoiceMode(request.VoiceMode);
        request.CharacterMode = NormalizeCharacterMode(request.CharacterMode);
        if (request.MusicVolume is < 0 or > 1) throw new InvalidOperationException("RVVIDEO_MUSIC_VOLUME_INVALID");
        if (request.DefaultTtsRate <= 0) throw new InvalidOperationException("RVVIDEO_TTS_RATE_INVALID");
        if (request.SkipCharacter)
        {
            request.CharacterMode = RVideoCharacterModes.None;
            request.SelectedCharacterId = null;
            request.CharacterSnapshot = null;
        }
        else if (request.CharacterMode == RVideoCharacterModes.Upload
                 && request.CharacterSnapshot is null)
        {
            throw new InvalidOperationException("RVVIDEO_UPLOADED_CHARACTER_REQUIRED");
        }
        else if (request.CharacterMode == RVideoCharacterModes.Library
                 && (request.SelectedCharacterId is null || request.CharacterSnapshot is null))
        {
            throw new InvalidOperationException("RVVIDEO_LIBRARY_CHARACTER_REQUIRED");
        }
        else if (request.CharacterMode == RVideoCharacterModes.None)
        {
            throw new InvalidOperationException("RVVIDEO_CHARACTER_MODE_REQUIRED");
        }

        if (request.VoiceMode == RVideoVoiceModes.Library
            && (string.IsNullOrWhiteSpace(request.VoiceCatalogCode) || request.VoiceSnapshot is null))
        {
            throw new InvalidOperationException("RVVIDEO_LIBRARY_VOICE_REQUIRED");
        }
        if (request.VoiceMode != RVideoVoiceModes.Library)
        {
            request.VoiceCatalogCode = null;
            request.VoiceSnapshot = null;
        }
        if (string.IsNullOrWhiteSpace(request.MusicCatalogCode))
        {
            request.MusicCatalogCode = null;
            request.MusicSnapshot = null;
        }
        else if (request.MusicSnapshot is null)
        {
            throw new InvalidOperationException("RVVIDEO_LIBRARY_MUSIC_REQUIRED");
        }
    }

    public static void ValidateActiveVoice(AiStudioVoiceDto? voice, RVideoJobSettingsRequest request)
    {
        if (request.VoiceMode != RVideoVoiceModes.Library) return;
        if (voice is null || !voice.IsActive || string.IsNullOrWhiteSpace(voice.ProviderCode))
            throw new InvalidOperationException("RVVIDEO_LIBRARY_VOICE_UNAVAILABLE");
    }

    public static void ValidateActiveMusic(AiStudioMusicDto? music, RVideoJobSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MusicCatalogCode)) return;
        if (music is null || !music.IsActive || !AiStudioCatalogRules.HasLocalMusicFile(music))
            throw new InvalidOperationException("RVVIDEO_LIBRARY_MUSIC_UNAVAILABLE");
    }

    public static void ValidateAutoProject(VideoProjectDto project, RVideoJobSettingsDto settings)
    {
        if (project.Scenes.Count == 0) throw new InvalidOperationException("RVVIDEO_SCENES_REQUIRED");
        var render = ResolveRenderSettings(project.OriginalPrompt);
        if (NormalizeAspectRatio(render.AspectRatio) is null || NormalizeResolution(render.Resolution) is null)
            throw new InvalidOperationException("RVVIDEO_RENDER_SETTINGS_INVALID");
        ValidateSettings(ToRequest(settings));
    }

    public static RVideoJobSettingsRequest ToRequest(RVideoJobSettingsDto settings)
        => new()
        {
            ExecutionMode = settings.ExecutionMode,
            SkipCharacter = settings.SkipCharacter,
            CharacterMode = settings.CharacterMode,
            SelectedCharacterId = settings.SelectedCharacterId,
            CharacterSnapshot = ParseSnapshot(settings.CharacterSnapshotJson),
            VoiceMode = settings.VoiceMode,
            VoiceCatalogCode = settings.VoiceCatalogCode,
            VoiceSnapshot = ParseSnapshot(settings.VoiceSnapshotJson),
            DefaultTtsRate = settings.DefaultTtsRate,
            MusicCatalogCode = settings.MusicCatalogCode,
            MusicSnapshot = ParseSnapshot(settings.MusicSnapshotJson),
            MusicVolume = settings.MusicVolume
        };

    private static object? ParseSnapshot(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) ?? new JsonObject();

    public static void ValidateScene(RVideoSceneEditorItem scene)
    {
        if (!SupportedDurations.Contains(scene.DurationSeconds)) throw new InvalidOperationException("RVVIDEO_DURATION_UNSUPPORTED");
        if (string.IsNullOrWhiteSpace(scene.ImagePrompt)) throw new InvalidOperationException("RVVIDEO_IMAGE_PROMPT_REQUIRED");
    }

    public static RVideoLifecycleDecision Evaluate(
        string executionMode,
        IReadOnlyCollection<string> sceneStatuses,
        bool hasFinalVideo)
    {
        var imageTerminal = sceneStatuses.Count > 0
            && sceneStatuses.All(x => x is VideoSceneStatuses.ImageReady or VideoSceneStatuses.VideoQueued or VideoSceneStatuses.VideoRendering or VideoSceneStatuses.VideoReady);
        var imageFailed = sceneStatuses.Count > 0 && sceneStatuses.All(x => x == VideoSceneStatuses.Failed);
        var videoTerminal = sceneStatuses.Count > 0
            && sceneStatuses.All(x => x is VideoSceneStatuses.VideoReady or VideoSceneStatuses.Failed);
        var anyVideoReady = sceneStatuses.Any(x => x == VideoSceneStatuses.VideoReady);
        if (hasFinalVideo) return new(RVideoStages.Result, false, false, false);
        if (imageFailed) return new(RVideoStages.Image, false, false, true);
        if (string.Equals(executionMode, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase)
            && imageTerminal && anyVideoReady == false)
        {
            return new(RVideoStages.Video, true, false, false);
        }
        if (string.Equals(executionMode, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase)
            && videoTerminal && sceneStatuses.All(x => x == VideoSceneStatuses.VideoReady))
        {
            return new(RVideoStages.Result, false, true, false);
        }
        return new(imageTerminal ? RVideoStages.Image : RVideoStages.Scene, false, false, false);
    }
}
