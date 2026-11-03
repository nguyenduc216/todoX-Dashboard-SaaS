using System.Reflection;
using System.Runtime.CompilerServices;
using TodoX.Web.Models;
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

    [Theory]
    [InlineData(RVideoBulkRerenderMode.AllScenes)]
    [InlineData(RVideoBulkRerenderMode.IncompleteScenes)]
    public void TaskTheProviderHoldsIsNotReplacedSoNobodyPaysTwice(RVideoBulkRerenderMode mode)
    {
        var state = Idle with { LatestJob = new RenderJobDto { Id = Guid.NewGuid(), Status = "pending_reconciliation", ProviderTaskId = "task" }, HasActiveJob = true };
        Assert.Equal(RVideoBulkRerenderSceneDecision.SkipProviderPending,
            RVideoBulkRerenderPolicy.Decide(mode, state, false, hasImage: true));
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
        var bulk = page.IndexOf(">Render job</MudButton>", StringComparison.Ordinal);
        var create = page.IndexOf("OnClick=\"EnqueueRenderAllScenesAsync\"", StringComparison.Ordinal);
        Assert.True(add >= 0 && add < bulk && bulk < create);
        Assert.DoesNotContain("Render lại các job", page);

        var flow = Source("Components", "Pages", "RenderVideoJobs.BulkRerender.cs");
        Assert.Contains("RVideoBulkRerenderPolicy.SubmitAsync(BulkVideoRender,", flow);
        Assert.Contains("BulkVideoRender.EstimateAsync(", flow);
        Assert.DoesNotContain("ChargeAsync", flow);
        Assert.DoesNotContain("ReserveAsync", flow);
    }

    [Fact]
    public void V2ButtonSitsNextToAddSceneAndReusesTheSameRules()
    {
        var page = Source("Components", "Pages", "RVideo", "RVideoV2Preview.razor");
        var add = page.IndexOf(">Thêm scene</MudButton>", StringComparison.Ordinal);
        var bulk = page.IndexOf(">Render job</MudButton>", StringComparison.Ordinal);
        Assert.True(add >= 0 && add < bulk);

        var flow = Source("Components", "Pages", "RVideo", "RVideoV2Preview.BulkRerender.cs");
        Assert.Contains("RVideoBulkRerenderPolicy.Decide(", flow);
        Assert.Contains("RVideoBulkRerenderPolicy.CanQuote(", flow);
        Assert.Contains("RVideoBulkRerenderPolicy.SubmitAsync(SceneVideoRender,", flow);
        Assert.DoesNotContain("ChargeAsync", flow);
        Assert.DoesNotContain("ReserveAsync", flow);
    }

    [Fact]
    public void V2SavesTheJobRatioBeforeQuotingANewOperation()
    {
        var flow = Source("Components", "Pages", "RVideo", "RVideoV2Preview.BulkRerender.cs");
        var save = flow.IndexOf("await SaveBulkRerenderSettingsAsync(scene, user)", StringComparison.Ordinal);
        var quote = flow.IndexOf("SceneVideoRender.EstimateAsync(", StringComparison.Ordinal);
        Assert.True(save >= 0 && save < quote);
    }

    [Theory]
    [InlineData(10, true, 0, false, true)]
    [InlineData(10, false, 5, false, true)]
    [InlineData(0, false, 0, false, false)]
    [InlineData(10, false, 0, false, false)]
    [InlineData(10, true, 0, true, false)]
    public void OnlyARealPriceCanBeQuoted(int points, bool canStart, int missing, bool forSupersede, bool expected)
        => Assert.Equal(expected, RVideoBulkRerenderPolicy.CanQuote(
            new RVideoSceneVideoRenderEstimate(1m, points, 100m, 0m, missing, canStart), forSupersede));

    [Theory]
    [InlineData(true, "SupersedeAsync")]
    [InlineData(false, "EnqueueRerenderAsync")]
    public async Task SubmitPicksOneRenderPathAndAcknowledgesTheRisk(bool forSupersede, string expected)
    {
        var action = DispatchProxy.Create<IRVideoSceneVideoRenderAction, RecordingAction>();
        var request = new RVideoSceneVideoRenderRequest(1, new VideoProjectSceneDto { Id = 2 }, new ScenePromptMetadata()) { ForSupersede = forSupersede };

        var result = await RVideoBulkRerenderPolicy.SubmitAsync(action, request, SceneVideoOperationDraftTests.Actor());

        Assert.True(result.Success);
        var recording = (RecordingAction)(object)action;
        Assert.Equal(expected, recording.Submitted.Single());
        if (forSupersede) Assert.True(recording.Acknowledgement!.RiskAcknowledged);
    }

    public class RecordingAction : DispatchProxy
    {
        public readonly List<string> Submitted = new();
        public SceneVideoSupersedeAcknowledgement? Acknowledgement;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IRVideoSceneVideoRenderAction.GetSupersedeAcknowledgementAsync))
                return Task.FromResult((SceneVideoSupersedeAcknowledgement)RuntimeHelpers.GetUninitializedObject(typeof(SceneVideoSupersedeAcknowledgement)));
            if (method.Name == nameof(IRVideoSceneVideoRenderAction.SupersedeAsync)) Acknowledgement = (SceneVideoSupersedeAcknowledgement)args![1]!;
            Submitted.Add(method.Name);
            return Task.FromResult(RVideoSceneVideoRenderResult.Ok(Guid.NewGuid()));
        }
    }
}
