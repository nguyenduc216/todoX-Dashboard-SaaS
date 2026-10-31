using TodoX.Web.Services.JobRepair;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class JobRepairPlaybookTests
{
    private static JobRepairSceneSnapshot Head(string status, string? versionStatus = null) => new()
    {
        ProjectId = 1,
        SceneId = 2,
        SceneIndex = 1,
        HeadJobId = Guid.NewGuid(),
        HeadJobStatus = status,
        HeadVersionStatus = versionStatus ?? status,
        AckFound = true,
        HeadIdleMinutes = 3000
    };

    // Rows below mirror job e2c3c1e8, the first case recorded in the playbook.
    [Fact]
    public void FailedOperationWithOrphanOffersNewOperationThatDoesNotChargeCustomer()
    {
        var snapshot = Head("failed");
        snapshot.HeadHasProviderTask = true;
        snapshot.OrphanJobCount = 1;
        var finding = JobRepairPlaybook.Classify(snapshot);
        Assert.Equal(JobRepairPlaybook.FailedOperation, finding.CaseCode);
        Assert.Equal(JobRepairAction.NewOperation, finding.Action);
        Assert.Contains("Không trừ điểm của khách", finding.PointNote);
        Assert.Contains("mồ côi", finding.Summary);
    }

    [Fact]
    public void StaleInFlightSubmitWarnsThatProviderMayHaveAccepted()
    {
        var snapshot = Head("rendering", "queued");
        snapshot.SubmitStates = "SUBMIT_IN_FLIGHT";
        var finding = JobRepairPlaybook.Classify(snapshot);
        Assert.Equal(JobRepairPlaybook.AmbiguousSubmit, finding.CaseCode);
        Assert.Equal(JobRepairAction.NewOperation, finding.Action);
        Assert.NotNull(finding.RiskNote);
    }

    [Fact]
    public void KnownTaskTimeoutOnlyRechecksAndStatesTheCharge()
    {
        var snapshot = Head("pending_reconciliation");
        snapshot.HeadHasProviderTask = true;
        snapshot.HeadHasProviderVideoId = true;
        snapshot.SubmitStates = "CONFIRMED_PROVIDER_TASK";
        snapshot.HeadErrorCode = "SCENE_VIDEO_PROVIDER_POLL_TIMEOUT";
        var finding = JobRepairPlaybook.Classify(snapshot);
        Assert.Equal(JobRepairPlaybook.KnownTaskTimedOut, finding.CaseCode);
        Assert.Equal(JobRepairAction.RecheckProvider, finding.Action);
        Assert.Contains("trừ đúng 1 lần", finding.PointNote);
    }

    [Theory]
    [InlineData(false, "SUBMIT_IN_FLIGHT")]
    [InlineData(true, "CONFIRMED_PROVIDER_TASK")]
    public void PendingWithoutFullProviderIdentityIsNeverRepaired(bool hasTask, string submit)
    {
        var snapshot = Head("pending_reconciliation");
        snapshot.HeadHasProviderTask = hasTask;
        snapshot.HeadHasProviderVideoId = false;
        snapshot.SubmitStates = submit;
        Assert.Equal(JobRepairAction.None, JobRepairPlaybook.Classify(snapshot).Action);
    }

    [Fact]
    public void AnotherLiveJobBlocksEveryRepair()
    {
        var snapshot = Head("failed");
        snapshot.CompetingJobCount = 1;
        var finding = JobRepairPlaybook.Classify(snapshot);
        Assert.Equal(JobRepairPlaybook.BlockedByOtherJob, finding.CaseCode);
        Assert.Equal(JobRepairAction.None, finding.Action);
    }

    [Fact]
    public void ManualReviewIsLeftAloneEvenWhenItLooksRecoverable()
    {
        var snapshot = Head("pending_reconciliation");
        snapshot.HeadHasProviderTask = true;
        snapshot.HeadHasProviderVideoId = true;
        snapshot.SubmitStates = "CONFIRMED_PROVIDER_TASK";
        snapshot.HeadErrorCode = "SCENE_VIDEO_MANUAL_REVIEW_REQUIRED";
        Assert.Equal(JobRepairPlaybook.ManualReview, JobRepairPlaybook.Classify(snapshot).CaseCode);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(3000, true)]
    public void RecentOrLockedSendCountsAsRunning(int idleMinutes, bool lockActive)
    {
        var snapshot = Head("rendering", "queued");
        snapshot.SubmitStates = "SUBMIT_IN_FLIGHT";
        snapshot.HeadIdleMinutes = idleMinutes;
        snapshot.HeadLockActive = lockActive;
        var finding = JobRepairPlaybook.Classify(snapshot);
        Assert.Equal(JobRepairPlaybook.Running, finding.CaseCode);
        Assert.Equal(JobRepairAction.None, finding.Action);
    }

    [Fact]
    public void UnrecognisedStatesAreReportedNotRepaired()
    {
        var prepared = Head("failed");
        prepared.PreparedCount = 1;
        var noAck = Head("failed");
        noAck.AckFound = false;
        var odd = Head("cancelled");
        var staleWithoutIntent = Head("rendering", "queued");
        foreach (var snapshot in new[] { prepared, noAck, odd, staleWithoutIntent })
        {
            var finding = JobRepairPlaybook.Classify(snapshot);
            Assert.Equal(JobRepairPlaybook.Unknown, finding.CaseCode);
            Assert.Equal(JobRepairAction.None, finding.Action);
        }
    }

    [Fact]
    public void NoHeadAndCompletedNeedNoAction()
    {
        Assert.Equal(JobRepairPlaybook.NotStarted, JobRepairPlaybook.Classify(new JobRepairSceneSnapshot()).CaseCode);
        Assert.Equal(JobRepairPlaybook.Healthy, JobRepairPlaybook.Classify(Head("completed")).CaseCode);
    }

    [Fact]
    public void ButtonLivesOnlyInJobMonitorBehindItsAdminCheck()
    {
        var web = FindWebRoot();
        var page = File.ReadAllText(Path.Combine(web, "Components", "Pages", "AdminJobMonitor.razor"));
        Assert.True(page.IndexOf("AdminEndpointAuthorization.IsAdmin", StringComparison.Ordinal) < page.IndexOf(">Sửa lỗi<", StringComparison.Ordinal));
        Assert.DoesNotContain("JobRepairDialog", File.ReadAllText(Path.Combine(web, "Components", "Pages", "RenderVideoJobs.razor")));

        var service = File.ReadAllText(Path.Combine(web, "Services", "JobRepair", "JobRepairService.cs"));
        Assert.Contains("AdminEndpointAuthorization.IsAdmin(user)", service);
        Assert.Contains("user.CustomerId is not null", service);
    }

    private static string FindWebRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Web", "TodoX.Web.csproj"))) return Path.Combine(dir.FullName, "TodoX.Web");
        }
        throw new DirectoryNotFoundException("TodoX.Web not found.");
    }
}
