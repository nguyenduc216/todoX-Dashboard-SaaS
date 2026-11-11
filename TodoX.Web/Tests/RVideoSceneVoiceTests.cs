using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoSceneVoiceTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static SceneVideoVersionDto Video(Guid? audio = null, string? prompt = "push in") => new()
    { Status = "completed", VoiceAudioVersionId = audio, VideoPromptSnapshot = prompt, LogicalRequestId = "x" };

    private static SceneAudioVersionDto Audio(string status) => new() { Status = status, LogicalRequestId = "a" };

    [Fact]
    public void Library_voice_states()
    {
        var lib = RVideoVoiceModes.Library;
        Assert.Equal(RVideoSceneVoice.Voiced, RVideoSceneVoice.Resolve(lib, true, Video(Guid.NewGuid()), Audio("completed")));
        Assert.Equal(RVideoSceneVoice.Rendering, RVideoSceneVoice.Resolve(lib, true, Video(), Audio("processing")));
        // A new voice being made shows as rendering even while the old voice is still in the video.
        Assert.Equal(RVideoSceneVoice.Rendering, RVideoSceneVoice.Resolve(lib, true, Video(Guid.NewGuid()), Audio("queued")));
        Assert.Equal(RVideoSceneVoice.Failed, RVideoSceneVoice.Resolve(lib, true, Video(), Audio("failed")));
        Assert.Equal(RVideoSceneVoice.Waiting, RVideoSceneVoice.Resolve(lib, true, Video(), null));
        Assert.Equal(RVideoSceneVoice.None, RVideoSceneVoice.Resolve(lib, true, null, null));
    }

    [Fact]
    public void Native_and_silent_videos()
    {
        var native = Video(prompt: "walk\n\n[NATIVE SPEECH]\nThe on-screen character speaks");
        Assert.Equal(RVideoSceneVoice.Voiced, RVideoSceneVoice.Resolve(RVideoVoiceModes.Native, true, native, null));
        // Rendered before Native was switched on: no speech in it.
        Assert.Equal(RVideoSceneVoice.None, RVideoSceneVoice.Resolve(RVideoVoiceModes.Native, true, Video(), null));
        Assert.Equal(RVideoSceneVoice.None, RVideoSceneVoice.Resolve(RVideoVoiceModes.None, true, Video(), null));
    }

    [Fact]
    public void Scene_voice_overrides_the_job_voice()
    {
        var metadata = new ScenePromptMetadata();
        Assert.Null(RVideoSceneVoice.SceneVoiceCode(metadata));
        metadata.Extra[RVideoSceneVoice.VoiceCodeKey] = " vbee_a ";
        Assert.Equal("vbee_a", RVideoSceneVoice.SceneVoiceCode(metadata));
        Assert.Contains("ResolveVoiceAsync(settings, RVideoSceneVoice.SceneVoiceCode(metadata), ct)",
            Read("TodoX.Web", "Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs"));
    }

    [Fact]
    public void Regenerated_voice_is_a_new_attempt_muxed_again_and_re_merged()
    {
        var chain = Read("TodoX.Web", "Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs");
        Assert.Contains("logicalRequestId = $\"{logicalRequestId}:v{(latest?.VersionNumber ?? 0) + 1}\";", chain);
        Assert.Contains("if (!regenerate && selected is not null", chain);
        // Billed at the TodoX voice rate every time, recorded as a user re-render.
        Assert.Contains("var parentJobBilled = !regenerate &&", chain);
        Assert.Contains("billingIntent = regenerate ? PointBillingIntent.UserRerender : PointBillingIntent.InitialRender", chain);
        Assert.Contains("voices.FirstOrDefault(x => x.IsDefault)", chain);
        Assert.Contains(":{selectedAudio.Id:N}", Read("TodoX.Web", "Services", "VideoRender", "RVideoSceneMediaFinalizerService.cs"));
        Assert.Contains("TriggerSceneAudioRevoice", Read("TodoX.Web", "Services", "VideoRender", "SceneAudioMuxHandler.cs"));
        var finalization = Read("TodoX.Web", "Services", "VideoRender", "RVideoProjectFinalizationService.cs");
        Assert.Contains("if (triggerSource == RVideoProjectFinalizationContracts.TriggerSceneAudioRevoice) manual = true;", finalization);
        var merge = Read("TodoX.Web", "Services", "VideoRender", "VideoRenderMergeHandler.cs");
        Assert.Contains("manual = sourceName == RVideoProjectFinalizationContracts.TriggerManual || revoice;", merge);
        Assert.Contains("ResolveVersioningEnabledAsync(_versions, manual && !revoice, ct)", merge);
        var preview = Read("TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.razor");
        Assert.Contains("Tạo lại giọng đọc", preview);
        Assert.Contains("rv2-pv-voice--@thumbVoice", preview);
    }
}
