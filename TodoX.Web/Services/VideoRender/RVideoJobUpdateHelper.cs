using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public static class RVideoJobUpdateHelper
{
    public static RVideoJobUpdateRequest BuildSafeUpdate(
        RVideoJobView view,
        string? titleOverride = null,
        string? promptOverride = null,
        string? aspectRatioOverride = null,
        string? resolutionOverride = null,
        int? totalSecondsOverride = null,
        int? sceneSecondsOverride = null,
        bool? thinkScenesOverride = null,
        string? sourceImageUrlOverride = null,
        RVideoJobSettingsRequest? settingsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var aspect = aspectRatioOverride ?? ExtractAspectRatio(view) ?? "9:16";
        var resolution = resolutionOverride ?? ExtractResolution(view) ?? "720p";
        return new RVideoJobUpdateRequest
        {
            Title = titleOverride ?? view.Project.Title ?? "RVIDEO",
            Prompt = promptOverride ?? view.Project.OriginalPrompt ?? string.Empty,
            AspectRatio = aspect,
            Resolution = resolution,
            TotalSeconds = totalSecondsOverride ?? view.Project.TotalSeconds,
            SceneSeconds = sceneSecondsOverride ?? view.Project.SceneSeconds,
            ThinkScenes = thinkScenesOverride ?? view.Project.ThinkScenes,
            SourceImageUrl = sourceImageUrlOverride ?? view.Project.SourceImageUrl,
            Settings = settingsOverride ?? BuildSettingsFromView(view)
        };
    }

    private static string? ExtractAspectRatio(RVideoJobView view)
    {
        try
        {
            if (view.CoreJob.Input.ValueKind == JsonValueKind.Object)
            {
                if (view.CoreJob.Input.TryGetProperty("aspectRatio", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                }
                if (view.CoreJob.Input.TryGetProperty("aspect_ratio", out var v2) && v2.ValueKind == JsonValueKind.String)
                {
                    var s = v2.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                }
            }
        }
        catch { }
        try
        {
            if (string.IsNullOrWhiteSpace(view.Project.OriginalPrompt)) return null;
            var parser = new TodoXVideoPromptParser();
            return parser.Parse(view.Project.OriginalPrompt).Model.AspectRatio;
        }
        catch { return null; }
    }

    private static string? ExtractResolution(RVideoJobView view)
    {
        try
        {
            if (view.CoreJob.Input.ValueKind == JsonValueKind.Object)
            {
                if (view.CoreJob.Input.TryGetProperty("resolution", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
                }
            }
        }
        catch { }
        try
        {
            if (string.IsNullOrWhiteSpace(view.Project.OriginalPrompt)) return null;
            var parser = new TodoXVideoPromptParser();
            return parser.Parse(view.Project.OriginalPrompt).Model.Resolution;
        }
        catch { return null; }
    }

    private static RVideoJobSettingsRequest BuildSettingsFromView(RVideoJobView view)
    {
        var s = view.Settings;
        if (s is null) return new RVideoJobSettingsRequest();
        return new RVideoJobSettingsRequest
        {
            ExecutionMode = s.ExecutionMode,
            SkipCharacter = s.SkipCharacter,
            UseReferenceImageForAllScenes = s.UseReferenceImageForAllScenes,
            CharacterMode = s.CharacterMode,
            SelectedCharacterId = s.SelectedCharacterId,
            CharacterSnapshot = string.IsNullOrWhiteSpace(s.CharacterSnapshotJson) ? null : JsonSerializer.Deserialize<object>(s.CharacterSnapshotJson),
            VoiceMode = s.VoiceMode,
            VoiceCatalogCode = s.VoiceCatalogCode,
            VoiceSnapshot = string.IsNullOrWhiteSpace(s.VoiceSnapshotJson) ? null : JsonSerializer.Deserialize<object>(s.VoiceSnapshotJson),
            DefaultTtsRate = s.DefaultTtsRate,
            MusicCatalogCode = s.MusicCatalogCode,
            MusicSnapshot = string.IsNullOrWhiteSpace(s.MusicSnapshotJson) ? null : JsonSerializer.Deserialize<object>(s.MusicSnapshotJson),
            MusicVolume = s.MusicVolume
        };
    }
}
