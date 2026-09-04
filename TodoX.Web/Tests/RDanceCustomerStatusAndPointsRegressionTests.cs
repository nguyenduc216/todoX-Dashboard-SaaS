using System.Text;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services;
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

    [Fact]
    public void RdancePointPricingUsesVideoSecondsAndMatchesBackendQueueContract()
    {
        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 0.0m, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 0.8m, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 0m, "per_render", "global");

        var fourteenSeconds = PointPricingCalculator.Estimate(0, imageRate, 14, videoRate, 0, voiceRate);
        var fifteenSeconds = PointPricingCalculator.Estimate(0, imageRate, 15, videoRate, 0, voiceRate);

        Assert.Equal(11.2m, fourteenSeconds.Video.Points);
        Assert.Equal(11.2m, fourteenSeconds.TotalPoints);
        Assert.Equal(12m, fifteenSeconds.Video.Points);
        Assert.Equal(12m, fifteenSeconds.TotalPoints);
    }

    [Fact]
    public void RdanceDetailUsesUnifiedPointPricingForEstimateAndConfirmation()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("@using TodoX.Web.Services.Platform", detail);
        Assert.Contains("@inject ICoreServiceCatalogService CoreCatalog", detail);
        Assert.Contains("@inject IPointPricingService PointPricing", detail);
        Assert.Contains("DanceSellMotionProviderContract.ResolveProviderMode(route, _job.Mode)", detail);
        Assert.Contains("CoreCatalog.GetByCodeAsync(FixedTodoXServiceCatalog.RDance", detail);
        Assert.Contains("FixedTodoXServiceCatalog.RDance", detail);
        Assert.Contains("ResolveMotionDurationSeconds(_job, route)", detail);
        Assert.Contains("PointPricing.EstimateAsync(new PointPricingEstimateRequest(", detail);
        Assert.Contains("StaticImageBillingPolicy.ResolveRdanceStaticInputCount(_job)", detail);
        Assert.Contains("StaticImageBillingPolicy.ResolveBillableStaticImageCount(staticImageCount, chargeStaticImagePoints)", detail);
        Assert.Contains("FormatPoints(_pointEstimate?.TotalPoints)", detail);
        Assert.Contains("var points = FormatPoints(_pointEstimate?.TotalPoints);", detail);
        Assert.Contains("ReferenceVersionStatusLabel(version?.Status)", detail);
        Assert.Contains("DanceSellCustomerStatusText.ProviderStatusLabel(x.Status)", detail);
    }

    [Fact]
    public void RdancePointDisplayPrefersChargedOperationPoints()
    {
        var job = new DanceSellJobDto
        {
            TotalTodoxPointsEstimated = 11.2m
        };
        var chargedOperation = new DanceSellProviderOperationDto
        {
            BillingStatus = DanceSellBillingStatuses.Charged,
            TodoxPointsCharged = 12m
        };

        Assert.Equal(12m, DanceSellPointDisplay.ResolveDisplayPoints(job, chargedOperation));
        Assert.Equal(11.2m, DanceSellPointDisplay.ResolveDisplayPoints(job, null));
    }

    [Fact]
    public void StaticImageBillingPolicyCountsConfiguredRdanceInputsAndCanDisableBilling()
    {
        var directReferenceJob = new DanceSellJobDto
        {
            ReferenceMode = DanceSellReferenceModes.DirectReference,
            DirectReferenceMediaId = Guid.NewGuid(),
            DirectReferenceUrl = "direct.png"
        };

        var job = new DanceSellJobDto
        {
            CharacterMediaId = Guid.NewGuid(),
            CharacterImageUrl = "character.png",
            ProductMediaId = Guid.NewGuid(),
            ProductImageUrl = "product.png"
        };

        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 0.5m, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 0.8m, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 0m, "per_render", "global");

        Assert.Equal(1, StaticImageBillingPolicy.ResolveRdanceStaticInputCount(directReferenceJob));
        Assert.Equal(2, StaticImageBillingPolicy.ResolveRdanceStaticInputCount(job));
        Assert.Equal(2, StaticImageBillingPolicy.ResolveBillableStaticImageCount(2, true));
        Assert.Equal(0, StaticImageBillingPolicy.ResolveBillableStaticImageCount(2, false));
        Assert.Equal(12.2m, PointPricingCalculator.Estimate(2, imageRate, 14, videoRate, 0, voiceRate).TotalPoints);
        Assert.Equal(11.2m, PointPricingCalculator.Estimate(0, imageRate, 14, videoRate, 0, voiceRate).TotalPoints);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()), Encoding.UTF8);
}
