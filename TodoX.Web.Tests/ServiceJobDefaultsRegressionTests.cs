using TodoX.Web.Models.Catalog;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ServiceJobDefaultsRegressionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = Path.Combine(RepoRoot, "TodoX.Web");

    [Fact]
    public void ServiceJobDefaults_Codec_RoundTripsCoreFields()
    {
        var original = new ServiceJobDefaults
        {
            Version = 2,
            AspectRatio = "9:16",
            Resolution = "1080p",
            TotalSeconds = 24,
            SceneSeconds = 8,
            ExecutionMode = "AUTO",
            UseReferenceImageForAllScenes = true,
            VoiceMode = "LIBRARY",
            VoiceCatalogCode = "VOICE_A",
            VoiceVolume = 0.75m,
            DefaultTtsRate = 1.2m,
            MusicMode = "LIBRARY",
            MusicCatalogCode = "MUSIC_A",
            MusicVolume = 0.25m,
            ProfileCode = "TIMELAPSE_CONSTRUCTION",
            SceneCount = 5,
            VideoMode = "professional",
            Ratio = "16_9",
            AutoFinish = true,
            ModelMode = "fit",
            CharacterProductMode = "wear"
        };

        var json = ServiceJobDefaultsCodec.ToJson(original);
        var parsed = ServiceJobDefaultsCodec.FromJson(json);

        Assert.Equal(2, parsed.Version);
        Assert.Equal("service_job_defaults", parsed.Type);
        Assert.Equal("9:16", parsed.AspectRatio);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal(24, parsed.TotalSeconds);
        Assert.Equal(8, parsed.SceneSeconds);
        Assert.Equal("AUTO", parsed.ExecutionMode);
        Assert.True(parsed.UseReferenceImageForAllScenes);
        Assert.Equal("LIBRARY", parsed.VoiceMode);
        Assert.Equal("VOICE_A", parsed.VoiceCatalogCode);
        Assert.Equal(0.75m, parsed.VoiceVolume);
        Assert.Equal(1.2m, parsed.DefaultTtsRate);
        Assert.Equal("LIBRARY", parsed.MusicMode);
        Assert.Equal("MUSIC_A", parsed.MusicCatalogCode);
        Assert.Equal(0.25m, parsed.MusicVolume);
        Assert.Equal("TIMELAPSE_CONSTRUCTION", parsed.ProfileCode);
        Assert.Equal(5, parsed.SceneCount);
        Assert.Equal("professional", parsed.VideoMode);
        Assert.Equal("16_9", parsed.Ratio);
        Assert.True(parsed.AutoFinish);
        Assert.Equal("fit", parsed.ModelMode);
        Assert.Equal("wear", parsed.CharacterProductMode);
    }

    [Fact]
    public void ServiceDefaults_ReachAllCreateScreensAndCatalogQuery()
    {
        var serviceDialog = Read("Components", "Dialogs", "ServiceDialog.razor");
        var renderVideo = Read("Components", "Pages", "RenderVideoJobs.razor");
        var timelapse = Read("Components", "Pages", "TimelapseJobCreate.razor");
        var rdance = Read("Components", "Pages", "RDanceJobCreate.razor");
        var catalogRepo = Read("Services", "CatalogRepository.cs");

        Assert.Contains("JobDefaultsJson", serviceDialog, StringComparison.Ordinal);
        Assert.Contains("NormalizeDefaults()", serviceDialog, StringComparison.Ordinal);
        Assert.Contains("ApplyServiceJobDefaults()", renderVideo, StringComparison.Ordinal);
        Assert.Contains("ServiceJobDefaultsCodec.FromJson(service.JobDefaults.ToString())", renderVideo, StringComparison.Ordinal);
        Assert.Contains("ApplyServiceDefaults()", timelapse, StringComparison.Ordinal);
        Assert.Contains("ServiceJobDefaultsCodec.FromJson(_selectedService.JobDefaultsJson)", timelapse, StringComparison.Ordinal);
        Assert.Contains("LoadServiceDefaultsAsync()", rdance, StringComparison.Ordinal);
        Assert.Contains("FixedTodoXServiceCatalog.RDance", rdance, StringComparison.Ordinal);
        Assert.Contains("JobDefaultsJson", catalogRepo, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { WebRoot }.Concat(parts).ToArray()));
}
