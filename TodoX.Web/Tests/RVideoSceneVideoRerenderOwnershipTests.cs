using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// Regression coverage for the scene-814 rerender defect: a scene that already holds V1..Vn could never
/// be claimed before its next version existed, so every rerender was quarantined with
/// RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED. Ownership is now decided per operation instead of per scene.
/// </summary>
public sealed class RVideoSceneVideoRerenderOwnershipTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Source normalised to LF so assertions do not depend on the checkout's line endings.</summary>
    private static string RenderJobServiceSource => File.ReadAllText(
            Path.Combine(RepoRoot, "TodoX.Web", "Services", "Render", "RenderJobService.cs"), System.Text.Encoding.UTF8)
        .Replace("\r\n", "\n");

    /// <summary>The whole allowPreDispatch branch body, isolated from the rest of the file.</summary>
    private static string PreDispatchBranch
    {
        get
        {
            var source = RenderJobServiceSource;
            var start = source.IndexOf("if (!owned && allowPreDispatch)", StringComparison.Ordinal);
            Assert.True(start >= 0, "The allowPreDispatch branch must exist.");
            var end = source.IndexOf("if (!owned)", start, StringComparison.Ordinal);
            Assert.True(end > start, "The ownership refusal must follow the branch.");
            return source[start..end];
        }
    }

    [Fact]
    public void PreDispatchOwnershipNoLongerRequiresSceneWithoutVersions()
    {
        var branch = PreDispatchBranch;

        // The defective predicate was a scene-scoped "no version exists at all" test: it closed the
        // scene id predicate immediately, without ever comparing a logical request id.
        Assert.DoesNotMatch(new Regex(@"AND v\.scene_id=@sceneId::bigint\s*\)"), branch);
        Assert.Contains("AND v.scene_id=@sceneId::bigint\n                                AND v.logical_request_id=j.input_json->>'logicalRequestId')",
            branch, StringComparison.Ordinal);

        // Ownership must be tied to this operation's own logical request id.
        Assert.Contains("v.logical_request_id=j.input_json->>'logicalRequestId'", branch, StringComparison.Ordinal);
        Assert.Contains("NULLIF(j.input_json->>'logicalRequestId', '') IS NOT NULL", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void PreDispatchOwnershipKeepsLeaseWorkerAndAttemptGuards()
    {
        var branch = PreDispatchBranch;

        Assert.Contains("j.status='preparing'", branch, StringComparison.Ordinal);
        Assert.Contains("j.worker_key=j.lock_owner", branch, StringComparison.Ordinal);
        Assert.Contains("j.lock_until > now()", branch, StringComparison.Ordinal);
        Assert.Contains("j.attempt_count > 0 AND j.attempt_count <= j.max_attempts", branch, StringComparison.Ordinal);
        Assert.Contains("j.provider_task_id IS NULL AND j.provider_video_id_base IS NULL", branch, StringComparison.Ordinal);
        Assert.Contains("j.tenant_id=@tenant", branch, StringComparison.Ordinal);
        Assert.Contains("j.input_json->>'projectId'=@projectId", branch, StringComparison.Ordinal);
        Assert.Contains("j.input_json->>'sceneId'=@sceneId", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void PreDispatchOwnershipRefusesAnotherHolderOfTheSameOperation()
    {
        var branch = PreDispatchBranch;

        // Another live lease, a job that may carry a live provider task, or an unresolved submit intent
        // must still block the claim.
        Assert.Contains("SELECT 1 FROM render.render_jobs other", branch, StringComparison.Ordinal);
        Assert.Contains("other.id <> j.id", branch, StringComparison.Ordinal);
        Assert.Contains("render.scene_video_submit_intents", branch, StringComparison.Ordinal);
        Assert.Contains("'SUBMIT_IN_FLIGHT','AMBIGUOUS_SUBMIT'", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void PreDispatchBranchStillThrowsOwnershipRequiredWhenRefused()
    {
        var collapsed = Regex.Replace(RenderJobServiceSource, @"\s+", " ");
        Assert.Contains(
            "if (!owned) throw new InvalidOperationException(\"RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED\");",
            collapsed,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdvisoryLockAndSceneRowLockAreStillTakenBeforeOwnership()
    {
        var source = RenderJobServiceSource;
        var start = source.IndexOf("private async Task<System.Data.IDbTransaction?> BeginSceneVideoWriterAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("if (!owned && allowPreDispatch)", start, StringComparison.Ordinal);
        Assert.True(end > start, "The ownership evaluation must follow the lock acquisition.");
        var body = source[start..end];

        Assert.Contains("pg_advisory_xact_lock(hashtextextended(@lockName, 0))", body, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", body, StringComparison.Ordinal);
        Assert.Contains("BuildSceneJobLockName", body, StringComparison.Ordinal);
    }
}