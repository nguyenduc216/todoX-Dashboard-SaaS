using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseUiRegressionTests
{
    [Fact]
    public void TimelapseReadinessUsesExistingRequestValidation()
    {
        var request = new TimelapseCreateRequest
        {
            ProfileCode = "construction",
            SceneCount = 3,
            VideoMode = TimelapseRequestRules.FastMode,
            Ratio = TimelapseRequestRules.LandscapeRatio
        };

        Assert.Empty(TimelapseRequestRules.Validate(request, hasOriginalImage: true));
        Assert.NotEmpty(TimelapseRequestRules.Validate(request, hasOriginalImage: false));

        request.ProfileCode = string.Empty;
        Assert.NotEmpty(TimelapseRequestRules.Validate(request, hasOriginalImage: true));
    }

    [Fact]
    public void TimelapseCreateAutosavesOnceThenUpdatesTheSameDraft()
    {
        var source = ReadRepoFile("Components", "Pages", "TimelapseJobCreate.razor");

        Assert.Contains("private Guid? _draftJobId", source);
        Assert.Contains("Jobs.CreateDraftAsync", source);
        Assert.Contains("Jobs.UpdateDraftAsync", source);
        Assert.Contains("if (_draftJobId is Guid draftId)", source);
        Assert.Contains("await Task.Delay(700, token)", source);
        Assert.Contains("_autoSaveGate", source);
    }

    [Fact]
    public void TimelapseMediaCardsExposeDirectViewAndPlayActions()
    {
        var frame = ReadRepoFile("Components", "Shared", "RenderMediaFrame.razor");
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");

        Assert.Contains("preload=\"metadata\"", frame);
        Assert.Contains("scene-media-play-overlay", frame);
        Assert.Contains("scene-media-view-overlay", frame);
        Assert.Contains("aria-label=\"@VideoAriaLabel\"", frame);
        Assert.Contains("OpenImagePreviewAsync(image)", page);
        Assert.Contains("OpenVideoPreviewAsync(clip)", page);
        Assert.Contains("ReferenceImageLightboxDialog", page);
        Assert.Contains("LandingIndustryVideoPreviewDialog", page);
        Assert.Contains("IsRenderingOperation(clip.Status)) ? clip.PublicUrl : null", page);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
