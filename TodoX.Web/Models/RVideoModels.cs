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
    public bool UseReferenceImageForAllScenes { get; set; }
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
    public bool UseReferenceImageForAllScenes { get; set; }
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

public sealed record UploadedCharacterSnapshot(string Source, string FileName, string StorageKey, string FileUrl);

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

public sealed record RVideoSceneLifecycleState(
    long SceneId,
    int SceneIndex,
    int DurationSeconds,
    bool HasImage,
    bool UsesSharedReferenceImage,
    bool HasVideo,
    bool ImageAttemptActive,
    bool ImageFailedTerminal,
    bool VideoAttemptActive,
    bool VideoFailedTerminal,
    bool ImageRetryRequested = false)
{
    public bool IsImageReady
        => HasImage && !HasVideo && !VideoAttemptActive && !VideoFailedTerminal;

    public bool IsVideoTerminal
        => HasVideo || VideoFailedTerminal;
}

public static class RVideoSceneLifecycleClassifier
{
    public static RVideoSceneLifecycleState Classify(
        VideoProjectSceneDto scene,
        IReadOnlyCollection<VideoProjectEventDto>? events = null,
        bool imageAttemptActive = false,
        bool videoAttemptActive = false,
        bool imageRetryRequested = false,
        bool usesSharedReferenceImage = false)
    {
        var status = scene.Status?.Trim().ToLowerInvariant();
        var latestFailure = FindLatestFailure(scene.Id, events);
        var hasVideo = !string.IsNullOrWhiteSpace(scene.SceneVideoUrl)
                       || !string.IsNullOrWhiteSpace(scene.SceneVideoPath)
                       || status == VideoSceneStatuses.VideoReady;
        var hasImage = usesSharedReferenceImage
                       || !string.IsNullOrWhiteSpace(scene.StaticImageUrl)
                       || !string.IsNullOrWhiteSpace(scene.StaticImagePath)
                       || status is VideoSceneStatuses.ImageReady
                           or VideoSceneStatuses.VideoQueued
                           or VideoSceneStatuses.VideoRendering
                           or VideoSceneStatuses.VideoReady;
        var videoActive = videoAttemptActive
                          || status is VideoSceneStatuses.VideoQueued or VideoSceneStatuses.VideoRendering;
        var imageFailed = status == VideoSceneStatuses.Failed
                          && (latestFailure == "SCENE_IMAGE_RENDER_FAILED"
                              || (latestFailure is null && !hasImage && !hasVideo));
        var videoFailed = status == VideoSceneStatuses.Failed
                          && !imageFailed
                          && (latestFailure == "SCENE_VIDEO_RENDER_FAILED"
                              || (latestFailure is null && hasImage));

        return new(
            scene.Id,
            scene.SceneIndex,
            scene.DurationSeconds,
            hasImage,
            usesSharedReferenceImage,
            hasVideo,
            imageAttemptActive,
            imageFailed,
            videoActive,
            videoFailed,
            imageRetryRequested);
    }

    private static string? FindLatestFailure(long sceneId, IReadOnlyCollection<VideoProjectEventDto>? events)
    {
        if (events is null) return null;
        foreach (var projectEvent in events.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id))
        {
            if (projectEvent.EventType is not ("SCENE_IMAGE_RENDER_FAILED" or "SCENE_VIDEO_RENDER_FAILED")
                || string.IsNullOrWhiteSpace(projectEvent.DataJson))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(projectEvent.DataJson);
                if (document.RootElement.TryGetProperty("sceneId", out var value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt64(out var eventSceneId)
                    && eventSceneId == sceneId)
                {
                    return projectEvent.EventType;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed historical event payloads and use the legacy fallback.
            }
        }

        return null;
    }
}

public static class RVideoRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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

    public static string ResolveVoiceMode(RVideoJobSettingsDto? settings)
        => NormalizeVoiceMode(settings?.VoiceMode);

    public static bool HasSceneVoice(VideoProjectSceneDto scene)
        => !string.IsNullOrWhiteSpace(ResolveSceneVoiceText(scene));

    public static string? ResolveSceneVoiceText(VideoProjectSceneDto scene)
        => FirstNonBlank(scene.VoiceText, ScenePromptMetadata.FromScene(scene).Voice);

    public static string? ResolveSceneVoiceInstruction(VideoProjectSceneDto scene)
        => FirstNonBlank(scene.VoiceInstruction, ScenePromptMetadata.FromScene(scene).VoiceInstruction);

    public static string ComposeNativeVoicePrompt(string? visualPrompt, string? voiceText, string? voiceInstruction)
    {
        var visual = visualPrompt?.Trim() ?? string.Empty;
        var text = voiceText?.Trim();
        var instruction = voiceInstruction?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return visual;
        }

        if (visual.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return visual;
        }

        var builder = new System.Text.StringBuilder(visual);
        if (builder.Length > 0) builder.AppendLine().AppendLine();
        builder.AppendLine("[NATIVE SPEECH]");
        builder.Append("The on-screen character speaks naturally in Vietnamese: \"")
            .Append(text)
            .AppendLine("\"");
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.AppendLine().AppendLine("[VOICE / DELIVERY]").AppendLine(instruction);
        }
        builder.AppendLine().AppendLine("[LIP SYNC]")
            .AppendLine("Natural mouth movement must match the spoken Vietnamese dialogue.")
            .AppendLine("Speech must be generated natively as part of the video audio.")
            .AppendLine("Do not add subtitles unless explicitly requested.");
        return builder.ToString().Trim();
    }

    public static bool IsSceneFinalReady(
        VideoProjectSceneDto scene,
        RVideoJobSettingsDto settings,
        SceneVideoVersionDto? video,
        SceneAudioVersionDto? audio)
    {
        if (video is null || !string.Equals(video.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ResolveVoiceMode(settings) switch
        {
            RVideoVoiceModes.Library when HasSceneVoice(scene)
                => audio is not null
                   && string.Equals(audio.Status, "completed", StringComparison.OrdinalIgnoreCase)
                   && scene.SelectedAudioVersionId == audio.Id
                   && video.VoiceAudioVersionId == audio.Id,
            _ => true
        };
    }

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
                 && !HasUsableUploadedCharacterSnapshot(request.CharacterSnapshot))
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

    public static bool RequiresExternalVoice(VideoProjectSceneDto scene, RVideoJobSettingsDto? settings)
    {
        if (settings is null || ResolveVoiceMode(settings) != RVideoVoiceModes.Library)
        {
            return false;
        }

        return HasSceneVoice(scene);
    }

    public static bool NeedsImageWork(string sceneStatus)
        => string.Equals(sceneStatus, VideoSceneStatuses.Draft, StringComparison.OrdinalIgnoreCase);

    public static bool NeedsImageWork(RVideoSceneLifecycleState scene)
        => !scene.UsesSharedReferenceImage
           && !scene.HasImage
           && !scene.HasVideo
           && !scene.ImageAttemptActive
           && !scene.VideoAttemptActive
           && !scene.VideoFailedTerminal
           && (!scene.ImageFailedTerminal || scene.ImageRetryRequested);

    public static bool NeedsImageWork(IReadOnlyCollection<string> sceneStatuses)
        => sceneStatuses.Count > 0
            && sceneStatuses.All(x => x is not VideoSceneStatuses.VideoQueued
                                      and not VideoSceneStatuses.VideoRendering
                                      and not VideoSceneStatuses.VideoReady)
            && sceneStatuses.Any(NeedsImageWork);

    public static decimal CalculateMergedDuration(IEnumerable<VideoProjectSceneDto> scenes)
        => scenes.Sum(x => (decimal)x.DurationSeconds);

    public static void EnsureProjectOwnership(Guid? projectTenantId, Guid currentTenantId)
    {
        if (projectTenantId is null || projectTenantId.Value != currentTenantId)
            throw new InvalidOperationException("RVVIDEO_PROJECT_NOT_FOUND");
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
            UseReferenceImageForAllScenes = settings.UseReferenceImageForAllScenes,
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

    public static void PreserveValidUploadedCharacterSnapshot(
        RVideoJobSettingsRequest request,
        RVideoJobSettingsDto persistedSettings)
    {
        if (request.SkipCharacter
            || !string.Equals(request.CharacterMode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase)
            || HasUsableUploadedCharacterSnapshot(request.CharacterSnapshot)
            || persistedSettings.SkipCharacter
            || !string.Equals(persistedSettings.CharacterMode, RVideoCharacterModes.Upload, StringComparison.OrdinalIgnoreCase)
            || !HasUsableUploadedCharacterSnapshot(persistedSettings.CharacterSnapshotJson))
        {
            return;
        }

        request.CharacterSnapshot = JsonSerializer.Deserialize<UploadedCharacterSnapshot>(
            persistedSettings.CharacterSnapshotJson!,
            JsonOptions);
    }

    public static bool HasUsableUploadedCharacterSnapshot(object? snapshot)
        => snapshot is not null
           && HasUsableUploadedCharacterSnapshot(JsonSerializer.Serialize(snapshot, JsonOptions));

    public static bool HasUsableUploadedCharacterSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return HasValue(root, "fileUrl") || HasValue(root, "storageKey");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasValue(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString());

    private static object? ParseSnapshot(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) ?? new JsonObject();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
        var videoActive = sceneStatuses.Any(x => x is VideoSceneStatuses.VideoQueued or VideoSceneStatuses.VideoRendering);
        var videoTerminal = sceneStatuses.Count > 0
            && sceneStatuses.All(x => x is VideoSceneStatuses.VideoReady or VideoSceneStatuses.Failed);
        var anyVideoReady = sceneStatuses.Any(x => x == VideoSceneStatuses.VideoReady);
        if (hasFinalVideo) return new(RVideoStages.Result, false, false, false);
        if (videoTerminal)
        {
            return anyVideoReady
                ? new(RVideoStages.Result, false, true, false)
                : new(RVideoStages.Video, false, false, true);
        }
        if (string.Equals(executionMode, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase)
            && !videoActive
            && imageTerminal
            && sceneStatuses.Any(x => x == VideoSceneStatuses.ImageReady))
        {
            return new(RVideoStages.Video, true, false, false);
        }
        if (videoActive) return new(RVideoStages.Video, false, false, false);
        if (sceneStatuses.Count > 0 && sceneStatuses.All(x => x == VideoSceneStatuses.Failed))
            return new(RVideoStages.Video, false, false, true);
        return new(imageTerminal ? RVideoStages.Image : RVideoStages.Scene, false, false, false);
    }

    public static RVideoLifecycleDecision Evaluate(
        string executionMode,
        IReadOnlyCollection<RVideoSceneLifecycleState> scenes,
        bool hasFinalVideo)
    {
        if (hasFinalVideo) return new(RVideoStages.Result, false, false, false);
        if (scenes.Count == 0) return new(RVideoStages.Scene, false, false, false);

        var imageFailed = scenes.Any(x => x.ImageFailedTerminal && !x.ImageRetryRequested);
        var imagePending = scenes.Any(x => !x.HasImage
                                           && !x.HasVideo
                                           && !x.VideoFailedTerminal
                                           && (!x.ImageFailedTerminal || x.ImageRetryRequested));
        var imageActive = scenes.Any(x => x.ImageAttemptActive);
        var videoActive = scenes.Any(x => x.VideoAttemptActive);
        var allVideoTerminal = scenes.All(x => x.IsVideoTerminal);
        var anyVideoReady = scenes.Any(x => x.HasVideo);
        var videoPending = scenes.Any(x => x.IsImageReady);

        if (imageFailed || imagePending || imageActive)
            return new(RVideoStages.Image, false, false, false);
        if (videoActive)
            return new(RVideoStages.Video, false, false, false);
        if (allVideoTerminal)
        {
            return anyVideoReady
                ? new(RVideoStages.Result, false, true, false)
                : new(RVideoStages.Video, false, false, true);
        }

        if (string.Equals(executionMode, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase)
            && videoPending)
        {
            return new(RVideoStages.Video, true, false, false);
        }

        return new(RVideoStages.Video, false, false, false);
    }
}
