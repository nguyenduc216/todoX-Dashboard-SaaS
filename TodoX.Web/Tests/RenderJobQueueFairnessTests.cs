using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RenderJobQueueFairnessTests
{
    private static readonly DateTime BaseTime = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SceneVideoFairClaimPrefersFreshJobOverAncientDueProviderPollJob()
    {
        var fresh = Job("fresh", queuedAtHours: 6.41, retryAfterHours: null);
        var oldDue = Job("old", queuedAtHours: 1.09, retryAfterHours: 8.27);

        var ordered = RenderJobService.OrderForSceneVideoClaimFairness(new[] { oldDue, fresh });

        Assert.Equal(["fresh", "old"], ordered.Select(x => x.JobType));
    }

    [Fact]
    public void ProviderPollJobNotDueIsNotEligibleForClaim()
    {
        var now = BaseTime.AddHours(8.20);
        var notDue = Job("poll", queuedAtHours: 1.09, retryAfterHours: 8.27);

        Assert.False(IsClaimable(notDue, now));
    }

    [Fact]
    public void DueProviderPollJobWithoutFreshCompetitorIsClaimable()
    {
        var now = BaseTime.AddHours(8.28);
        var due = Job("poll", queuedAtHours: 1.09, retryAfterHours: 8.27);

        Assert.True(IsClaimable(due, now));
        Assert.Equal(due.RetryAfter, RenderJobService.ResolveEffectiveClaimReadyTime(due));
    }

    [Fact]
    public void SceneVideoFairClaimKeepsFIFOAmongFreshJobs()
    {
        var a = Job("A", queuedAtHours: 6.40, retryAfterHours: null);
        var b = Job("B", queuedAtHours: 6.41, retryAfterHours: null);

        var ordered = RenderJobService.OrderForSceneVideoClaimFairness(new[] { b, a });

        Assert.Equal(["A", "B"], ordered.Select(x => x.JobType));
    }

    [Fact]
    public void SceneVideoFairClaimPreservesPriorityBeforeReadyTime()
    {
        var highPriorityOld = Job("high", priority: 0, queuedAtHours: 1.09, retryAfterHours: 8.27);
        var lowPriorityFresh = Job("low", priority: 100, queuedAtHours: 6.41, retryAfterHours: null);

        var ordered = RenderJobService.OrderForSceneVideoClaimFairness(new[] { lowPriorityFresh, highPriorityOld });

        Assert.Equal(["high", "low"], ordered.Select(x => x.JobType));
    }

    [Fact]
    public void ProviderPollClaimStillDoesNotConsumeRetryBudget()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("input_json=input_json - 'providerPoll'", source);
        Assert.Contains("attempt_count=attempt_count + CASE", source);
        Assert.Contains("WHEN COALESCE(input_json->>'providerPoll', 'false') = 'true' THEN 0", source);
        Assert.Contains("JOB_PROVIDER_POLL_SCHEDULED", source);
    }

    [Fact]
    public void SceneVideoOnlyClaimUsesFairOrderingWhileOtherQueuesKeepLegacyOrder()
    {
        var fairOrder = RenderJobService.ResolveClaimOrderSql(new[] { RenderJobTypes.RenderSceneVideo });
        var legacyOrder = RenderJobService.ResolveClaimOrderSql(new[] { RenderJobTypes.RenderSceneAudio });

        Assert.Contains("GREATEST(queued_at, retry_after)", fairOrder);
        Assert.Contains("ORDER BY priority ASC, queued_at ASC", legacyOrder);
        Assert.DoesNotContain("GREATEST(queued_at, retry_after)", legacyOrder);
    }

    [Fact]
    public void SkipLockedClaimPrimitiveRemainsIntact()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("FOR UPDATE SKIP LOCKED", source);
    }

    private static bool IsClaimable(RenderJobDto job, DateTime now)
        => job.Status == RenderJobStatuses.Queued
           && (job.RetryAfter is null || job.RetryAfter <= now);

    private static RenderJobDto Job(string jobType, double queuedAtHours, double? retryAfterHours, int priority = 100)
        => new()
        {
            JobType = jobType,
            Status = RenderJobStatuses.Queued,
            Priority = priority,
            QueuedAt = BaseTime.AddHours(queuedAtHours),
            RetryAfter = retryAfterHours is null ? null : BaseTime.AddHours(retryAfterHours.Value)
        };

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot, "TodoX.Web" }.Concat(parts).ToArray()));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
