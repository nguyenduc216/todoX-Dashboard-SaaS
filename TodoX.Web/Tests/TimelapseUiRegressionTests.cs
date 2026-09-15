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
    public void TimelapseRequestRulesDisableEveryInvalidRequiredField()
    {
        var valid = new TimelapseCreateRequest
        {
            ProfileCode = "construction",
            SceneCount = 3,
            VideoMode = TimelapseRequestRules.FastMode,
            Ratio = TimelapseRequestRules.LandscapeRatio
        };

        Assert.Empty(TimelapseRequestRules.Validate(valid, hasOriginalImage: true));

        foreach (var invalid in new[]
        {
            CreateRequest(profileCode: string.Empty),
            CreateRequest(sceneCount: 2),
            CreateRequest(videoMode: "unsupported"),
            CreateRequest(ratio: "1_1")
        })
        {
            Assert.NotEmpty(TimelapseRequestRules.Validate(invalid, hasOriginalImage: true));
        }

        Assert.NotEmpty(TimelapseRequestRules.Validate(valid, hasOriginalImage: false));
    }

    [Fact]
    public void TimelapseCreateUsesCentralReadinessForActionAndAutosave()
    {
        var source = ReadRepoFile("Components", "Pages", "TimelapseJobCreate.razor");

        Assert.Contains("private bool CanStartWorkflow", source);
        Assert.Contains("TimelapseRequestRules.Validate(_request, HasValidImage).Count == 0", source);
        Assert.Contains("private bool SubmitDisabled => _busy || !CanStartWorkflow", source);
        Assert.Contains("&& CanStartWorkflow", source);
        Assert.DoesNotContain("_profiles.Count == 0 || !ServiceReady || !HasValidImage || !HasValidPrice", source);
        Assert.DoesNotContain("_draftJobId.HasValue", source);
        Assert.DoesNotContain("userClickedSave", source);
        Assert.DoesNotContain("manualSaveCompleted", source);
    }

    [Fact]
    public void TimelapseClickSavesBeforeStartingAndReusesDraftGate()
    {
        var source = ReadRepoFile("Components", "Pages", "TimelapseJobCreate.razor");
        var saveIndex = source.IndexOf("var job = await EnsureDraftSavedAsync", StringComparison.Ordinal);
        var startIndex = source.IndexOf("job = await Jobs.StartOrResumeAsync", saveIndex, StringComparison.Ordinal);

        Assert.True(saveIndex >= 0);
        Assert.True(startIndex > saveIndex);
        Assert.Contains("await _autoSaveGate.WaitAsync(ct)", source);
        Assert.Contains("if (_draftJobId is Guid draftId)", source);
        Assert.Contains("Jobs.UpdateDraftAsync", source);
        Assert.Contains("Jobs.CreateDraftAsync", source);
        Assert.Equal(1, CountOccurrences(source, "Jobs.StartOrResumeAsync(job.Id"));
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

    private static TimelapseCreateRequest CreateRequest(
        string profileCode = "construction",
        int sceneCount = 3,
        string videoMode = TimelapseRequestRules.FastMode,
        string ratio = TimelapseRequestRules.LandscapeRatio)
        => new()
        {
            ProfileCode = profileCode,
            SceneCount = sceneCount,
            VideoMode = videoMode,
            Ratio = ratio
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
