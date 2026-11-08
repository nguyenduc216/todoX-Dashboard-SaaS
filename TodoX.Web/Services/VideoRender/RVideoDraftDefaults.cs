using System.Text.Json.Nodes;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services.VideoRender;

// RVideo V2 is the only job UI: "/jobs/rvideo/new" creates an empty draft straight away with the service's
// job defaults (what the legacy creator pre-filled) and opens it in V2.
public static class RVideoDraftDefaults
{
    public static RVideoJobCreateRequest Build(
        Guid serviceId,
        string serviceCode,
        ServiceJobDefaults defaults,
        IReadOnlyList<AiStudioVoiceDto> voices,
        IReadOnlyList<AiStudioMusicDto> music,
        string logicalRequestId)
    {
        var aspect = Pick(defaults.AspectRatio, "9:16", "16:9", "9:16");
        var resolution = Pick(defaults.Resolution, "720p", "720p", "1080p", "4K");
        var total = defaults.TotalSeconds is > 0 ? defaults.TotalSeconds.Value : 16;
        var scene = defaults.SceneSeconds is > 0 ? defaults.SceneSeconds.Value : 8;
        var autoFinish = defaults.AutoFinish ?? true;

        var voiceMode = Pick(defaults.VoiceMode, RVideoVoiceModes.None, RVideoVoiceModes.None, RVideoVoiceModes.Native, RVideoVoiceModes.Library);
        var voice = voiceMode == RVideoVoiceModes.Library
            ? voices.FirstOrDefault(x => string.Equals(x.Code, defaults.VoiceCatalogCode, StringComparison.OrdinalIgnoreCase))
            : null;
        if (voiceMode == RVideoVoiceModes.Library && voice is null) voiceMode = RVideoVoiceModes.None; // default voice gone

        var musicItem = string.Equals(defaults.MusicMode, RVideoMusicModes.Library, StringComparison.OrdinalIgnoreCase)
            ? music.FirstOrDefault(x => string.Equals(x.Code, defaults.MusicCatalogCode, StringComparison.OrdinalIgnoreCase))
            : null;

        return new RVideoJobCreateRequest
        {
            LogicalRequestId = logicalRequestId,
            ServiceId = serviceId,
            ServiceCode = serviceCode,
            Title = "Video mới",
            Prompt = new JsonObject { ["aspect_ratio"] = aspect, ["resolution"] = resolution, ["auto_finish"] = autoFinish }.ToJsonString(),
            AspectRatio = aspect,
            Resolution = resolution,
            TotalSeconds = total,
            SceneSeconds = Math.Max(1, scene),
            ThinkScenes = false,
            Settings = new RVideoJobSettingsRequest
            {
                CreativeMode = false,
                ExecutionMode = Pick(defaults.ExecutionMode, RVideoExecutionModes.Manual, RVideoExecutionModes.Manual, RVideoExecutionModes.Auto),
                SkipCharacter = string.Equals(defaults.CharacterMode, RVideoCharacterModes.None, StringComparison.OrdinalIgnoreCase),
                UseReferenceImageForAllScenes = defaults.UseReferenceImageForAllScenes ?? false,
                CharacterMode = RVideoCharacterModes.None, // the reference image is picked in V2's Info tab
                VoiceMode = voiceMode,
                VoiceCatalogCode = voice?.Code,
                VoiceSnapshot = voice,
                DefaultTtsRate = defaults.DefaultTtsRate is > 0m ? defaults.DefaultTtsRate.Value : 1.0m,
                MusicCatalogCode = musicItem?.Code,
                MusicSnapshot = musicItem,
                MusicVolume = defaults.MusicVolume is >= 0m and <= 1m ? defaults.MusicVolume.Value : 0.8m
            }
        };
    }

    private static string Pick(string? value, string fallback, params string[] allowed)
        => allowed.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
}
