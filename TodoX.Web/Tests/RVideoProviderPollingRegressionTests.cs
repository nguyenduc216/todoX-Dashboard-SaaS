using System.Reflection;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoProviderPollingRegressionTests
{
    [Fact]
    public void PendingPollSurvivesMoreThanThreeWorkerClaims()
    {
        var method = typeof(Services.VideoRender.SceneVideoWorkerHandler)
            .GetMethod("ResolveNextAttemptIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var versions = Enumerable.Range(0, 12)
            .Select(_ => new Services.VideoRender.SceneVideoVersionDto
            {
                LogicalRequestId = "scene-base",
                Status = "pending_reconciliation",
                ProviderTaskId = "a9896cf26fd2ff29"
            })
            .ToArray();

        for (var poll = 0; poll < 12; poll++)
        {
            Assert.Equal(0, method!.Invoke(null, new object[] { "scene-base", versions }));
            Assert.All(versions, version => Assert.Equal("a9896cf26fd2ff29", version.ProviderTaskId));
        }
    }

    [Fact]
    public void ProviderPollSchedulerDoesNotUseRetryBudget()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("Task<bool> ScheduleProviderPollAsync", source);
        Assert.Contains("SET status='queued'", source);
        Assert.Contains("lock_owner=NULL", source);
        Assert.Contains("lock_until=NULL", source);
        Assert.DoesNotContain("attempt_count < max_attempts", ProviderPollMethod(source));
        Assert.Contains("JOB_PROVIDER_POLL_SCHEDULED", ProviderPollMethod(source));
    }

    [Fact]
    public void MaxAttemptsDoesNotBlockProviderPollScheduling()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var providerPoll = ProviderPollMethod(source);

        Assert.DoesNotContain("max_attempts", providerPoll, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt_count", providerPoll, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingPollReusesSameProviderTaskIdWithoutSubmitOrReserve()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("GetReservationAsync(attemptLogicalRequestId", source);
        Assert.Contains("DeferProviderPollAsync(job, taskId!", source);
        Assert.Contains("if (string.IsNullOrWhiteSpace(taskId))", source);
        Assert.Contains("SubmitAsync", source);
        Assert.Contains("ReserveAsync", source);

        var existingTaskBlock = source[
            source.IndexOf("var existingTaskId = await _versions.GetSceneVideoProviderTaskIdAsync", StringComparison.Ordinal)..];
        var submitIndex = existingTaskBlock.IndexOf("SubmitAsync", StringComparison.Ordinal);
        var reserveIndex = existingTaskBlock.IndexOf("ReserveAsync", StringComparison.Ordinal);
        var reuseIndex = existingTaskBlock.IndexOf("GetReservationAsync", StringComparison.Ordinal);

        Assert.True(reuseIndex >= 0);
        Assert.True(submitIndex > reuseIndex);
        Assert.True(reserveIndex < reuseIndex);
    }

    [Fact]
    public void ProviderSuccessPathDownloadsAndCompletesSceneVideoVersion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("DownloadAndSaveBinaryAtObjectKeyAsync", source);
        Assert.Contains("CompleteSceneVideoVersionAsync", source);
        Assert.Contains("ProviderTaskId: taskId", source);
        Assert.Contains("ResultMediaId: saved.Id", source);
        Assert.Contains("saved.PublicUrl ?? saved.FileUrl", source);
    }

    [Fact]
    public void RetryUpdateZeroRowsDoesNotEmitFalseSuccessEvent()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var retry = source[
            source.IndexOf("public async Task ScheduleRetryAsync", StringComparison.Ordinal)..source.IndexOf("private const string SelectJobSql", StringComparison.Ordinal)];

        Assert.Contains("if (changed > 0)", retry);
        Assert.Contains("JOB_RETRY_NOT_SCHEDULED", retry);
        Assert.DoesNotContain("await AddEventAsync(jobId, \"JOB_RETRY_SCHEDULED\"", retry[..retry.IndexOf("if (changed > 0)", StringComparison.Ordinal)]);
    }

    private static string ProviderPollMethod(string source)
    {
        var start = source.IndexOf("public async Task<bool> ScheduleProviderPollAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private const string SelectJobSql", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
