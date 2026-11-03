using Npgsql;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SceneVideoQueueBlockedTests
{
    [Theory]
    [InlineData("SUBMIT_INTENT_PERSISTED", false)]
    [InlineData("SUBMIT_IN_FLIGHT", false)]
    [InlineData("AMBIGUOUS_SUBMIT", false)]
    [InlineData("CONFIRMED_PROVIDER_TASK", true)]
    [InlineData("MANUAL_REVIEW_REQUIRED", false)]
    public void CurrentOperationSubmitIntentAlwaysPreventsResend(string state, bool hasTask)
        => Assert.True(SceneVideoSubmitIntentFact.BlocksSceneSubmit(new(state, hasTask,
            IsNewJob: true, IsPriorJobTerminal: false, IsPriorJobCompleted: false)));

    [Theory]
    [InlineData("55000", "VIDEO_OPERATION_SUPERSEDED: old operation cannot submit or mutate current output", true)]
    [InlineData("55000", "VIDEO_PROJECT_GENERATION_NOT_COMPLETED", false)]
    [InlineData("55000", "unrelated failure", false)]
    [InlineData("23505", "VIDEO_OPERATION_SUPERSEDED: duplicate", false)]
    public void OnlyExactOperationFenceIsHandled(string state, string message, bool expected)
        => Assert.Equal(expected, RenderJobService.IsSceneVideoQueueOwnershipFence(new PostgresException(message, "ERROR", "ERROR", state)));

    [Fact]
    public void ClaimBoundaryHasDurableQuarantineAndSavepointRecovery()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Services/Render/RenderJobService.cs"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services/Render/RenderJobService.cs"));
        Assert.Contains("SCENE_VIDEO_QUEUE_QUARANTINED", source);
        Assert.Contains("ROLLBACK TO SAVEPOINT scene_video_claim", source);
        Assert.Contains("ClaimNextCandidateAsync", source);
    }
}
