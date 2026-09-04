using System.Text;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceCustomerStatusAndPointsRegressionTests
{
    [Fact]
    public void CustomerFacingRdanceStatusLabelsAreTranslated()
    {
        Assert.Equal("Đang chờ tạo video", DanceSellCustomerStatusText.StageLabel("motion_queued"));
        Assert.Equal("Hoàn thành", DanceSellCustomerStatusText.JobStatusLabel("completed"));
        Assert.Equal("Đã trừ điểm", DanceSellCustomerStatusText.BillingStatusLabel("charged"));
        Assert.Equal("Đang xử lý", DanceSellCustomerStatusText.ProviderStatusLabel("rendering"));
        Assert.Equal("Đang xử lý điểm", DanceSellCustomerStatusText.PointStatusLabel("pending"));
    }

    [Fact]
    public void MyJobsPageUsesLatestChargedRdancePointsAndSharedLabels()
    {
        var source = ReadRepoFile("Components", "Pages", "MyJobs.razor");

        Assert.Contains("BuildDanceRowsAsync", source);
        Assert.Contains("ResolveDancePointsLabelAsync", source);
        Assert.Contains("DanceOperations.GetLatestOperationAsync(job.Id, DanceSellOperationTypes.MotionVideo", source);
        Assert.Contains("DanceSellCustomerStatusText.JobStatusLabel(job.Status)", source);
        Assert.Contains("DanceSellCustomerStatusText.StageLabel(job.CurrentStage)", source);
    }

    [Fact]
    public void RdanceBackendSyncsBillingAndCompletedPointStatus()
    {
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellRepository.cs");
        var render = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var dashboard = ReadRepoFile("Services", "CustomerDashboardService.cs");

        Assert.Contains("billing_status = CASE", repository);
        Assert.Contains("WHEN COALESCE(r.point_cost_estimate, 0) > 0 THEN 'charged'", repository);
        Assert.Contains("job_type='dance_sell'", render);
        Assert.Contains("AND @status='completed'", render);
        Assert.Contains("point_status='pending' THEN 'charged'", render);
        Assert.Contains("DanceSellCustomerStatusText.JobStatusLabel(job.Status)", detail);
        Assert.Contains("DanceSellCustomerStatusText.JobStatusLabel(row.Status)", dashboard);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()), Encoding.UTF8);
}
