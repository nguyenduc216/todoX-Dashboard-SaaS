using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.VideoRender;
using Xunit;
namespace TodoX.Web.Tests;

// "/jobs/rvideo/new" creates the draft V2 opens; it must carry the service job defaults.
public sealed class RVideoBootstrapBehaviorTests
{
    [Fact]
    public void NewDraftUsesServiceJobDefaults()
    {
        var voice = new AiStudioVoiceDto { Code = "vn-female", Name = "Nữ" };
        var defaults = new ServiceJobDefaults
        {
            AspectRatio = "16:9", Resolution = "1080p", TotalSeconds = 30, SceneSeconds = 6,
            ExecutionMode = RVideoExecutionModes.Auto, VoiceMode = RVideoVoiceModes.Library, VoiceCatalogCode = "VN-FEMALE",
            UseReferenceImageForAllScenes = true, MusicVolume = 0.4m
        };
        var request = RVideoDraftDefaults.Build(Guid.NewGuid(), "rvideo", defaults, new[] { voice }, Array.Empty<AiStudioMusicDto>(), "req-1");

        Assert.Equal("16:9", request.AspectRatio);
        Assert.Equal("1080p", request.Resolution);
        Assert.Equal(30, request.TotalSeconds);
        Assert.Equal(6, request.SceneSeconds);
        Assert.Equal(RVideoExecutionModes.Auto, request.Settings.ExecutionMode);
        Assert.Equal(RVideoVoiceModes.Library, request.Settings.VoiceMode);
        Assert.Same(voice, request.Settings.VoiceSnapshot);
        Assert.True(request.Settings.UseReferenceImageForAllScenes);
        Assert.Equal(0.4m, request.Settings.MusicVolume);
        Assert.Contains("\"aspect_ratio\":\"16:9\"", request.Prompt);
        Assert.Equal("req-1", request.LogicalRequestId);
    }

    [Fact]
    public void MissingLibraryVoiceFallsBackToNoVoiceAndBadValuesUseSafeDefaults()
    {
        var defaults = new ServiceJobDefaults { AspectRatio = "4:3", VoiceMode = RVideoVoiceModes.Library, VoiceCatalogCode = "gone" };
        var request = RVideoDraftDefaults.Build(Guid.NewGuid(), "rvideo", defaults, Array.Empty<AiStudioVoiceDto>(), Array.Empty<AiStudioMusicDto>(), "req-2");

        Assert.Equal("9:16", request.AspectRatio);
        Assert.Equal("720p", request.Resolution);
        Assert.Equal(RVideoVoiceModes.None, request.Settings.VoiceMode);
        Assert.Null(request.Settings.VoiceSnapshot);
        Assert.Equal(RVideoExecutionModes.Manual, request.Settings.ExecutionMode);
    }
}
