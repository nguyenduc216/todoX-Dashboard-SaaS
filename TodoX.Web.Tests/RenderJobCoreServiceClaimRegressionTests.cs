using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RenderJobCoreServiceClaimRegressionTests
{
    [Fact]
    public void ClaimNextExcludingJobTypesIncludesUnclaimedCoreServiceRenderingRows()
    {
        var source = ReadSource("TodoX.Web", "Services", "Render", "RenderJobService.cs");
        var claim = Extract(source, "private async Task<RenderJobDto?> ClaimNextInternal(", "internal static string ResolveClaimOrderSql");

        Assert.Contains("(status='queued' AND (retry_after IS NULL OR retry_after <= now()))", claim, StringComparison.Ordinal);
        Assert.Contains("OR (", claim, StringComparison.Ordinal);
        Assert.Contains("job_type='core_service'", claim, StringComparison.Ordinal);
        Assert.Contains("AND status='rendering'", claim, StringComparison.Ordinal);
        Assert.Contains("AND attempt_count=0", claim, StringComparison.Ordinal);
        Assert.Contains("AND worker_key IS NULL", claim, StringComparison.Ordinal);
        Assert.Contains("AND lock_owner IS NULL", claim, StringComparison.Ordinal);
        Assert.Contains("AND lock_until IS NULL", claim, StringComparison.Ordinal);
        Assert.Contains("AND started_at IS NULL", claim, StringComparison.Ordinal);
        Assert.Contains("sql += \" AND NOT (job_type = ANY(@excludedJobTypes))\"", claim, StringComparison.Ordinal);
        Assert.True(
            claim.IndexOf("job_type='core_service'", StringComparison.Ordinal) <
            claim.IndexOf("sql += \" AND NOT (job_type = ANY(@excludedJobTypes))\"", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreServiceRecoveryClaimTransitionsToPreparingAndSetsClaimMetadata()
    {
        var source = ReadSource("TodoX.Web", "Services", "Render", "RenderJobService.cs");
        var claim = Extract(source, "private async Task<RenderJobDto?> ClaimNextInternal(", "internal static string ResolveClaimOrderSql");

        Assert.Contains("SET status='preparing'", claim, StringComparison.Ordinal);
        Assert.Contains("worker_key=@workerKey", claim, StringComparison.Ordinal);
        Assert.Contains("lock_owner=@workerKey", claim, StringComparison.Ordinal);
        Assert.Contains("lock_until=now() + (@lockSeconds || ' seconds')::interval", claim, StringComparison.Ordinal);
        Assert.Contains("attempt_count=attempt_count + CASE", claim, StringComparison.Ordinal);
        Assert.Contains("ELSE 1", claim, StringComparison.Ordinal);
        Assert.Contains("started_at=COALESCE(started_at, now())", claim, StringComparison.Ordinal);
        Assert.Contains("RENDER_JOB_CLAIM_SELECTED", claim, StringComparison.Ordinal);
        Assert.Contains("RENDER_JOB_CLAIM_RESULT", claim, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralRenderJobWorkerStillExcludesSceneVideoJobs()
    {
        var worker = ReadSource("TodoX.Web", "Services", "Render", "RenderJobWorker.cs");
        var sceneWorker = ReadSource("TodoX.Web", "Services", "Render", "SceneVideoJobWorker.cs");
        var program = ReadSource("TodoX.Web", "Program.cs");

        Assert.Contains("ClaimNextExcludingJobTypesAsync(_workerKey, lockFor, new[] { RenderJobTypes.RenderSceneVideo }", worker, StringComparison.Ordinal);
        Assert.Contains("ClaimNextByJobTypeAsync(workerKey, lockFor, new[] { RenderJobTypes.RenderSceneVideo }", sceneWorker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<RenderJobWorker>()", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<TodoX.Web.Services.Render.SceneVideoJobWorker>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RVideoLifecycleDoesNotMoveDraftOrQueuedCoreJobsToRenderingBeforeWorkerClaim()
    {
        var source = ReadSource("TodoX.Web", "Services", "VideoRender", "RVideoJobService.cs");
        var sync = Extract(source, "public async Task SyncLifecycleAsync", "internal static (string Status, int ProgressPercent) ResolveCoreLifecycleState");

        Assert.Contains("SET status=@status", sync, StringComparison.Ordinal);
        Assert.Contains("AND status IN ('preparing','rendering','post_processing','pending_reconciliation')", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("AND status NOT IN ('completed','failed','cancelled')", sync, StringComparison.Ordinal);
    }

    private static string Extract(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string ReadSource(params string[] path)
        => File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. path]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find TodoX repository root.");
    }
}
