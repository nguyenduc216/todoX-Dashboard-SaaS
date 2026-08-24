using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseWorkerClaimRegressionTests
{
    [Fact]
    public void RenderingImageWithoutProviderTaskIsWaitingForWorkerAndBecomesStuckAfterThreshold()
    {
        var image = new TimelapseStageImage
        {
            Status = TimelapseOperationStatuses.Rendering,
            StartedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        Assert.Equal(TimelapseImageExecutionPhase.QueuedForWorker, TimelapseImageExecutionPhase.Resolve(image));
        Assert.True(TimelapseImageExecutionPhase.IsWaitingForWorker(image));
        Assert.True(TimelapseImageExecutionPhase.IsStuckWaitingForWorker(image, DateTime.UtcNow, TimeSpan.FromMinutes(2)));

        image.ProviderTaskId = "provider-task";
        Assert.Equal(TimelapseImageExecutionPhase.Submitted, TimelapseImageExecutionPhase.Resolve(image));
        Assert.False(TimelapseImageExecutionPhase.IsWaitingForWorker(image));
    }

    [Fact]
    public void ImageClaimPreservesDependencyAndExpiredClaimRulesForRestartRecovery()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var claim = Between(source, "public async Task<TimelapseImageWorkItem?> ClaimImageAsync", "public async Task<TimelapseImageClaimDiagnostic?> DiagnoseImageClaimAsync");

        Assert.Contains("s.status='RENDERING'", claim);
        Assert.Contains("v.status='RENDERING'", claim);
        Assert.Contains("d.status='COMPLETED'", claim);
        Assert.Contains("d.result_media_id IS NOT NULL", claim);
        Assert.Contains("NULLIF(d.public_url,'') IS NOT NULL OR NULLIF(d.object_key,'') IS NOT NULL", claim);
        Assert.Contains("COALESCE((v.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) <= now()", claim);
        Assert.Contains("FOR UPDATE SKIP LOCKED", claim);
        Assert.DoesNotContain("active_attempt=active_attempt+1", claim);
    }

    [Fact]
    public void ImageClaimDiagnosticMatchesEligibilityRules()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("Task<TimelapseImageClaimDiagnostic?> DiagnoseImageClaimAsync", source);
        Assert.Contains("HasDependencyMedia", source);
        Assert.Contains("HasDependencyReference", source);
        Assert.Contains("ClaimExpired", source);
        Assert.Contains("TenantMatches", source);
        Assert.Contains("Eligible", source);
    }

    [Fact]
    public void WorkerStartupAndClaimLoopEmitThrottledOperationalDiagnostics()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseProviderWorkers.cs");

        Assert.Contains("TIMELAPSE_WORKER_START", source);
        Assert.Contains("TIMELAPSE_WORKER_DISABLED", source);
        Assert.Contains("TIMELAPSE_WORKER_PARALLELISM_NORMALIZED", source);
        Assert.Contains("TIMELAPSE_WORKER_HEARTBEAT", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_BEGIN", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_RETURNED", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_NULL", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIMED", source);
        Assert.Contains("TIMELAPSE_WORKER_ERROR", source);
        Assert.Contains("ShouldLogNullClaim", source);
        Assert.Contains("Math.Max(1, parallelism)", source);
        Assert.Contains("TimelapseWorkerIterationResult", source);
    }

    [Fact]
    public void CustomerUiDistinguishesWorkerWaitFromProviderSubmission()
    {
        var workflow = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");

        Assert.Contains("Đang chờ xử lý", workflow);
        Assert.Contains("Đang chờ hệ thống xử lý lâu hơn bình thường", workflow);
        Assert.Contains("TimelapseImageExecutionPhase.IsWaitingForWorker(image)", page);
        Assert.Contains("Đang chờ xử lý...", page);
        Assert.Contains("Đang chờ hệ thống xử lý lâu hơn bình thường.", page);
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
