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

        Assert.Contains("status='queued'", claim, StringComparison.Ordinal);
        Assert.Contains("retry_after IS NULL OR retry_after <= now()", claim, StringComparison.Ordinal);
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
    public void ClaimPredicateBlocksNormalQueuedJobsAtMaxAttemptsButPreservesProviderPollClaims()
    {
        var source = ReadSource("TodoX.Web", "Services", "Render", "RenderJobService.cs");
        var claim = Extract(source, "private async Task<RenderJobDto?> ClaimNextInternal(", "internal static string ResolveClaimOrderSql");

        Assert.Contains("AND attempt_count <= max_attempts", claim, StringComparison.Ordinal);
        Assert.Contains("COALESCE(input_json->>'providerPoll', 'false') = 'true'", claim, StringComparison.Ordinal);
        Assert.Contains("OR attempt_count < max_attempts", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("status='pending_reconciliation'", claim, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneVideoWorkerRejectsOverBudgetJobsBeforeDispatchingProviderWork()
    {
        var source = ReadSource("TodoX.Web", "Services", "Render", "SceneVideoJobWorker.cs");

        Assert.Contains("job.AttemptCount > job.MaxAttempts", source, StringComparison.Ordinal);
        Assert.Contains("Render job attempt budget exceeded", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("job.AttemptCount > job.MaxAttempts", StringComparison.Ordinal)
            < source.IndexOf("await dispatcher.DispatchAsync(job, stoppingToken)", StringComparison.Ordinal));
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

    [Fact]
    public void RVideoDraftCreationPersistsCoreJobAndProjectOwnershipAtomically()
    {
        var source = ReadSource("TodoX.Web", "Services", "VideoRender", "RVideoJobService.cs");
        var create = Extract(source, "public async Task<RVideoJobCreatedResult> CreateDraftAsync", "public async Task<RVideoJobView?> GetByJobIdAsync");

        Assert.Contains("using var tx = conn.BeginTransaction()", create, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO render.render_jobs", create, StringComparison.Ordinal);
        Assert.Contains("operation_type", create, StringComparison.Ordinal);
        Assert.Contains("operationType = service.ServiceType", create, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO video_render.video_projects", create, StringComparison.Ordinal);
        Assert.Contains("core_job_id", create, StringComparison.Ordinal);
        Assert.Contains("@jobId", create, StringComparison.Ordinal);
        Assert.Contains("tenant = _tenant.TenantId", create, StringComparison.Ordinal);
        Assert.Contains("customer = user.CustomerId", create, StringComparison.Ordinal);
        Assert.Contains("user = user.UserId", create, StringComparison.Ordinal);
        Assert.Contains("tx.Commit()", create, StringComparison.Ordinal);
    }

    [Fact]
    public void RVideoDraftCreationReusesLogicalRequestInsteadOfCreatingDuplicateCoreJob()
    {
        var source = ReadSource("TodoX.Web", "Services", "VideoRender", "RVideoJobService.cs");
        var create = Extract(source, "public async Task<RVideoJobCreatedResult> CreateDraftAsync", "public async Task<RVideoJobView?> GetByJobIdAsync");

        Assert.Contains("pg_advisory_xact_lock", create, StringComparison.Ordinal);
        Assert.Contains("j.logical_request_id=@logicalRequestId", create, StringComparison.Ordinal);
        Assert.Contains("JOIN video_render.video_projects p", create, StringComparison.Ordinal);
        Assert.Contains("p.core_job_id=j.id", create, StringComparison.Ordinal);
        Assert.Contains("return new(existing.JobId, existing.ProjectId", create, StringComparison.Ordinal);
        Assert.True(
            create.IndexOf("return new(existing.JobId, existing.ProjectId", StringComparison.Ordinal)
            < create.IndexOf("var jobId = Guid.NewGuid()", StringComparison.Ordinal));
    }

    [Fact]
    public void OrphanRecoveryLocksExistingProjectAndOnlyLinksCoreJob()
    {
        var source = ReadSource("TodoX.Web", "Services", "VideoRender", "RVideoJobService.cs");
        var recovery = Extract(source, "public async Task<RVideoJobCreatedResult> RecoverOrphanPromptWorkspaceAsync", "public async Task<RVideoJobCreatedResult> CreateDraftAsync");

        Assert.Contains("FOR UPDATE", recovery, StringComparison.Ordinal);
        Assert.Contains("RVIDEO_PROJECT_NOT_FOUND", recovery, StringComparison.Ordinal);
        Assert.Contains("RVIDEO_PROJECT_OWNERSHIP_MISMATCH", recovery, StringComparison.Ordinal);
        Assert.Contains("RVIDEO_PROJECT_IS_NOT_PROMPT_WORKSPACE", recovery, StringComparison.Ordinal);
        Assert.Contains("if (project.CoreJobId is Guid existingJobId)", recovery, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO render.render_jobs", recovery, StringComparison.Ordinal);
        Assert.Contains("operationType = service.ServiceType", recovery, StringComparison.Ordinal);
        Assert.Contains("status, current_step", recovery, StringComparison.Ordinal);
        Assert.Contains("'draft', 'info'", recovery, StringComparison.Ordinal);
        Assert.Contains("point_status", recovery, StringComparison.Ordinal);
        Assert.Contains("'not_required'", recovery, StringComparison.Ordinal);
        Assert.Contains("UPDATE video_render.video_projects SET core_job_id", recovery, StringComparison.Ordinal);
        Assert.Contains("core_job_id IS NULL", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("video_project_scenes", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("scene_image", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enqueue", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Billing", recovery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrphanRecoveryHasExplicitManualEndpointAndDoesNotRunAtStartup()
    {
        var endpoint = ReadSource("TodoX.Web", "Services", "VideoRender", "RVideoEndpoints.cs");
        var program = ReadSource("TodoX.Web", "Program.cs");

        Assert.Contains("recover-orphan", endpoint, StringComparison.Ordinal);
        Assert.Contains("RecoverOrphanPromptWorkspaceAsync", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoverOrphanPromptWorkspaceAsync", program, StringComparison.Ordinal);
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
