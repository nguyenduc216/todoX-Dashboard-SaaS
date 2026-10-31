using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoBulkRerenderTests
{
    private static readonly SceneVideoUiState Idle = new(null, null, null, SceneVideoSubmitBlockReason.None, HasActiveJob: false);
    private static SceneVideoUiState Job(string status, bool active) =>
        Idle with { LatestJob = new RenderJobDto { Id = Guid.NewGuid(), Status = status }, HasActiveJob = active };

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. path]));

    [Theory]
    [InlineData(RVideoBulkRerenderMode.IncompleteScenes, true, RVideoBulkRerenderSceneDecision.SkipCompleted)]
    [InlineData(RVideoBulkRerenderMode.IncompleteScenes, false, RVideoBulkRerenderSceneDecision.Render)]
    [InlineData(RVideoBulkRerenderMode.AllScenes, true, RVideoBulkRerenderSceneDecision.Render)]
    public void ModeDecidesWhetherCompletedScenesAreIncluded(RVideoBulkRerenderMode mode, bool completed, RVideoBulkRerenderSceneDecision expected)
        => Assert.Equal(expected, RVideoBulkRerenderPolicy.Decide(mode, Idle, completed, hasImage: true));

    [Theory]
    [InlineData(RVideoBulkRerenderMode.AllScenes)]
    [InlineData(RVideoBulkRerenderMode.IncompleteScenes)]
    public void RunningAttemptIsNeverReplacedSoTheCustomerCannotPayTwice(RVideoBulkRerenderMode mode)
        => Assert.Equal(RVideoBulkRerenderSceneDecision.SkipInFlight,
            RVideoBulkRerenderPolicy.Decide(mode, Job("rendering", active: true), false, hasImage: true));

    [Fact]
    public void AttemptWaitingForReconciliationIsIncompleteEvenWithAnOlderVideo()
    {
        var state = Job("pending_reconciliation", active: true);
        Assert.Equal(RVideoBulkRerenderSceneDecision.Render,
            RVideoBulkRerenderPolicy.Decide(RVideoBulkRerenderMode.IncompleteScenes, state, hasCompletedVideo: true, hasImage: true));
        Assert.True(RVideoBulkRerenderPolicy.HasUnknownOutcome(state));
    }

    [Fact]
    public void SceneWithoutImageOrLoadedStateIsSkipped()
    {
        Assert.Equal(RVideoBulkRerenderSceneDecision.SkipMissingImage,
            RVideoBulkRerenderPolicy.Decide(RVideoBulkRerenderMode.AllScenes, Idle, false, hasImage: false));
        Assert.Equal(RVideoBulkRerenderSceneDecision.SkipStateUnknown,
            RVideoBulkRerenderPolicy.Decide(RVideoBulkRerenderMode.AllScenes, null, false, hasImage: true));
    }

    [Fact]
    public void SceneWithoutAnAttemptTakesTheFirstRenderPath()
    {
        Assert.False(RVideoBulkRerenderPolicy.UsesNewOperation(Idle));
        Assert.True(RVideoBulkRerenderPolicy.UsesNewOperation(Idle with { LatestJobVersion = new SceneVideoVersionDto() }));
    }

    [Theory]
    [InlineData(10, 20, true, 0)]
    [InlineData(10, 10, true, 0)]
    [InlineData(25, 10, false, 15)]
    public void QuoteSumsScenesAndBlocksWhenPointsAreShort(int points, int available, bool canConfirm, int missing)
    {
        var quote = new RVideoBulkRerenderQuote(RVideoBulkRerenderMode.AllScenes,
            [new(1, 1, points - 4m, false), new(2, 2, 4m, true)], [], available, false);
        Assert.Equal(points, quote.TotalPoints);
        Assert.Equal(missing, quote.MissingPoints);
        Assert.Equal(canConfirm, quote.CanConfirm);
        Assert.Equal(1, quote.UnknownOutcomeCount);
    }

    [Fact]
    public void QuoteWithNoSceneCannotBeConfirmed()
        => Assert.False(new RVideoBulkRerenderQuote(RVideoBulkRerenderMode.AllScenes, [], [], 100m, false).CanConfirm);

    [Fact]
    public void DialogDefaultsToIncompleteScenesAndConfirmRequiresAQuote()
    {
        var dialog = Source("Components", "Dialogs", "RVideoBulkRerenderDialog.razor");
        Assert.Contains("_mode = RVideoBulkRerenderMode.IncompleteScenes", dialog);
        Assert.Contains("Render lại tất cả các scene", dialog);
        Assert.Contains("Render lại các scene chưa hoàn thành", dialog);
        Assert.Contains("Disabled=\"@(_loading || _quote?.CanConfirm != true)\"", dialog);
    }

    [Fact]
    public void PageButtonSitsNextToAddSceneAndReusesTheCanonicalActions()
    {
        var page = Source("Components", "Pages", "RenderVideoJobs.razor");
        var add = page.IndexOf(">Thêm scene</MudButton>", StringComparison.Ordinal);
        var bulk = page.IndexOf(">Render lại các job</MudButton>", StringComparison.Ordinal);
        var create = page.IndexOf("OnClick=\"EnqueueRenderAllScenesAsync\"", StringComparison.Ordinal);
        Assert.True(add >= 0 && add < bulk && bulk < create);

        var flow = Source("Components", "Pages", "RenderVideoJobs.BulkRerender.cs");
        Assert.Contains("BulkVideoRender.SupersedeAsync(", flow);
        Assert.Contains("BulkVideoRender.EnqueueRerenderAsync(", flow);
        Assert.Contains("BulkVideoRender.EstimateAsync(", flow);
        Assert.DoesNotContain("ChargeAsync", flow);
        Assert.DoesNotContain("ReserveAsync", flow);
    }
}
