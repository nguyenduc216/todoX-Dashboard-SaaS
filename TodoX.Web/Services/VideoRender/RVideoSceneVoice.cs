using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Voice state of a scene's selected video, for the voice icon: "voiced" (the video carries a voice: Vbee muxed in
/// or AI Native speech rendered with it), "rendering" (a Vbee voice is being made), "failed", "waiting" (the video is
/// done, its Vbee voice not made yet) or "none" (no voice in the video).
/// </summary>
public static class RVideoSceneVoice
{
    public const string Voiced = "voiced";
    public const string Rendering = "rendering";
    public const string Failed = "failed";
    public const string Waiting = "waiting";
    public const string None = "none";

    /// <summary>Marker ComposeNativeVoicePrompt writes into the prompt of a video rendered with AI Native Voice.</summary>
    public const string NativeSpeechMarker = "[NATIVE SPEECH]";

    /// <summary>Scene metadata key of a per-scene library voice; empty = the job's voice.</summary>
    public const string VoiceCodeKey = "voice_catalog_code";

    public static string? SceneVoiceCode(ScenePromptMetadata metadata)
        => metadata.Extra.TryGetValue(VoiceCodeKey, out var code) && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;

    private static readonly string[] ActiveAudioStatuses = ["queued", "submitted", "processing", "rendering", "pending", "preparing", "post_processing"];

    public static string Resolve(string? voiceMode, bool hasVoiceText, SceneVideoVersionDto? selectedVideo, SceneAudioVersionDto? latestAudio)
    {
        var audioStatus = latestAudio?.Status?.Trim().ToLowerInvariant();
        var library = string.Equals(voiceMode, RVideoVoiceModes.Library, StringComparison.OrdinalIgnoreCase);
        if (library && audioStatus is not null && ActiveAudioStatuses.Contains(audioStatus)) return Rendering;

        var videoDone = selectedVideo is not null && string.Equals(selectedVideo.Status, "completed", StringComparison.OrdinalIgnoreCase);
        if (videoDone && selectedVideo!.VoiceAudioVersionId is not null) return Voiced;
        if (videoDone && selectedVideo!.VideoPromptSnapshot?.Contains(NativeSpeechMarker, StringComparison.Ordinal) == true) return Voiced;

        if (library && hasVoiceText && videoDone)
            return audioStatus is "failed" or "error" or "pending_reconciliation" ? Failed : Waiting;
        return None;
    }

    public static string Label(string state) => state switch
    {
        Voiced => "Có giọng",
        Rendering => "Đang tạo giọng",
        Failed => "Lỗi tạo giọng",
        Waiting => "Chờ tạo giọng",
        _ => "Không có giọng"
    };
}
