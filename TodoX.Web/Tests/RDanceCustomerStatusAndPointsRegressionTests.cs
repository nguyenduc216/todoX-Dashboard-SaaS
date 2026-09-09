using System.Text;
using Microsoft.Extensions.Configuration;
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
        Assert.Contains("var imageCount = string.Equals(_job.ReferenceMode, DanceSellReferenceModes.DirectReference, StringComparison.OrdinalIgnoreCase)", detail);
        Assert.Contains("FormatPoints(_pointEstimate?.TotalPoints)", detail);
        Assert.Contains("var points = FormatPoints(_pointEstimate?.TotalPoints);", detail);
        Assert.Contains("ReferenceVersionStatusLabel(version?.Status)", detail);
        Assert.Contains("DanceSellCustomerStatusText.ProviderStatusLabel(x.Status)", detail);
    }

    [Fact]
    public void RdanceDetailGuardsRenderBeforeJobLoads()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("@if (_job == null)", detail);
        Assert.Contains("Loading RDANCE job...", detail);
        Assert.DoesNotContain("@DisplayStatusLabel(_job!)", detail);
        Assert.Contains("private string DisplayStatusLabel(DanceSellJobDto? job)", detail);
    }

    [Fact]
    public void RdanceDetailOnePageUiMatchesMockupStructure()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var tiktokDialog = ReadRepoFile("Components", "Dialogs", "RDanceTikTokUrlDialog.razor");

        Assert.Contains("rdance-workflow-grid", detail);
        Assert.Contains("rdance-step-card", detail);
        Assert.Contains("rdance-media-frame", detail);
        Assert.Contains("rdance-overlay-button", detail);
        Assert.Contains("rdance-thumbnail", detail);
        Assert.Contains("rdance-status-item", detail);
        Assert.Contains("Xem Browser", detail);
        Assert.Contains("Lưu thay đổi", detail);
        Assert.Contains("Tạo video", detail);
        Assert.Contains("OnClick=\"OpenBrowserAsync\"", detail);
        Assert.Contains("OnClick=\"SaveChangesAsync\"", detail);
        Assert.Contains("OnClick=\"ConfirmAndQueueAsync\"", detail);
        Assert.Contains("OnClick=\"OpenTikTokEditorAsync\"", detail);
        Assert.Contains("ShowAsync<RDanceTikTokUrlDialog>", detail);
        Assert.Contains("OnChange=\"OnCharacterSelected\"", detail);
        Assert.Contains("OnChange=\"OnProductSelected\"", detail);
        Assert.Contains("OnClick=\"GenerateReferenceAsync\"", detail);
        Assert.Contains("OnClick=\"DownloadResultAsync\"", detail);
        Assert.DoesNotContain("rdance-video-source", detail);
        Assert.Contains("Current TikTok URL", tiktokDialog);
        Assert.Contains("Change TikTok URL", tiktokDialog);
        Assert.Contains("Confirm / Load video", tiktokDialog);
    }

    [Fact]
    public void RdanceNewUiFeatureFlagIsSharedAndSupportsLegacyFallback()
    {
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableRDanceNewUI"] = "true",
                ["Features:RdnOnePageUiEnabled"] = "false"
            })
            .Build();
        var disabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableRDanceNewUI"] = "false",
                ["Features:RdnOnePageUiEnabled"] = "true"
            })
            .Build();
        var legacy = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:RdnOnePageUiEnabled"] = "true"
            })
            .Build();

        Assert.True(new RDanceFeatureService(enabled).IsNewUiEnabled);
        Assert.False(new RDanceFeatureService(disabled).IsNewUiEnabled);
        Assert.True(new RDanceFeatureService(legacy).IsNewUiEnabled);
    }

    [Fact]
    public void RdanceCreateUsesSharedUiFlagAndExistingUploadAndTikTokServices()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");

        Assert.Contains("@inject IRDanceFeatureService RDanceFeatures", create);
        Assert.Contains("@if (RDanceFeatures.IsNewUiEnabled)", create);
        Assert.Contains("OnChange=\"OnCharacterSelected\"", create);
        Assert.Contains("OnChange=\"OnProductSelected\"", create);
        Assert.Contains("DanceSell.UploadCharacterAsync", create);
        Assert.Contains("DanceSell.UploadProductAsync", create);
        Assert.Contains("References.AutoPrepareAsync", create);
        Assert.Contains("ShowAsync<RDanceTikTokUrlDialog>", create);
        Assert.Contains("DanceSell.StageTikTokAsync", create);
        Assert.Contains("controls muted playsinline", create);
    }

    [Fact]
    public void RdanceCreateReloadsAfterMotionUploadAndLeavesQueueingToDetailAutoFinish()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");
        var motionStart = create.IndexOf("private async Task OnMotionSelected", StringComparison.Ordinal);
        var motionEnd = create.IndexOf("private static bool HasAutoFinishPrerequisites", motionStart, StringComparison.Ordinal);
        var motionFlow = create[motionStart..motionEnd];

        Assert.Contains("DanceSell.UploadMotionAsync", motionFlow);
        Assert.Contains("DanceSell.GetAsync(job.Id, AuthState.CurrentUser!)", motionFlow);
        Assert.Contains("HasMotionReady(_job)", motionFlow);
        Assert.Contains("ResolveReferenceAfterMotionAsync(_job)", motionFlow);
        Assert.Contains("Navigation.NavigateTo($\"/jobs/rdance/{job.Id}\")", motionFlow);
        Assert.DoesNotContain("QueueRenderAsync", motionFlow);

        var readinessStart = create.IndexOf("private static bool HasMotionReady", StringComparison.Ordinal);
        var readinessEnd = create.IndexOf("private static bool HasAutoFinishPrerequisites", readinessStart, StringComparison.Ordinal);
        var readiness = create[readinessStart..readinessEnd];
        Assert.Contains("MotionVideoMediaId is not null", readiness);
        Assert.Contains("SourceStageStatus == DanceSellSourceStageStatuses.Ready", readiness);
        var autoFinish = create[readinessEnd..create.IndexOf("private async Task<DanceSellJobDto> EnsureDraftAsync", readinessEnd, StringComparison.Ordinal)];
        Assert.Contains("PreparedReferenceStatus == DanceSellReferenceStatuses.Approved", autoFinish);
        Assert.Contains("PreparedReferenceUrl", autoFinish);
        Assert.Contains("private async Task ResolveReferenceAfterMotionAsync", create);
        Assert.Contains("References.ApproveCharacterAsync", create);
        Assert.Contains("References.AutoPrepareAsync", create);
        Assert.Contains("private static bool HasMotionReady", create);
        Assert.Contains("DanceSell.StageTikTokAsync", create);

        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        Assert.Contains("await ContinueAutoFinishAsync();", detail);
        Assert.Contains("MotionStepStatusKey", detail);
        Assert.Contains("ReferenceStepStatusKey", detail);
        Assert.Contains("await AutoPrepareReferenceAsync();", detail);
        Assert.Contains("References.ApproveCharacterAsync(_job.Id", detail);
        Assert.Contains("DanceSell.QueueRenderAsync(_job.Id", detail);
    }

    [Fact]
    public void RdanceDetailAutoprepareBranchesByReferenceModeAndGuardsDuplicateWork()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("private async Task AutoPrepareReferenceAsync", StringComparison.Ordinal);
        var end = detail.IndexOf("private string? ValidateMotionVideo", start, StringComparison.Ordinal);
        var method = detail[start..end];

        Assert.Contains("job.PreparedReferenceStatus is DanceSellReferenceStatuses.Approved or DanceSellReferenceStatuses.Ready", method);
        Assert.Contains("if (IsReferenceGenerating || job.ReferenceMode == DanceSellReferenceModes.DirectReference)", method);
        Assert.Contains("var hasProduct = job.ProductMediaId is not null", method);
        Assert.Contains("if (hasCharacter && !hasProduct)", method);
        Assert.Contains("References.ApproveCharacterAsync(job.Id", method);
        Assert.Contains("else if (hasCharacter && hasProduct)", method);
        Assert.Contains("References.AutoPrepareAsync(job.Id", method);
        Assert.Contains("await ContinueAutoFinishAsync();", method);

        var characterBranch = method.IndexOf("if (hasCharacter && !hasProduct)", StringComparison.Ordinal);
        var productBranch = method.IndexOf("else if (hasCharacter && hasProduct)", StringComparison.Ordinal);
        var approveCall = method.IndexOf("References.ApproveCharacterAsync(job.Id", StringComparison.Ordinal);
        var autoPrepareCall = method.IndexOf("References.AutoPrepareAsync(job.Id", StringComparison.Ordinal);
        Assert.True(characterBranch >= 0 && productBranch > characterBranch);
        Assert.True(approveCall > characterBranch && approveCall < productBranch);
        Assert.True(autoPrepareCall > productBranch);
        Assert.DoesNotContain("References.AutoPrepareAsync(job.Id", method[..productBranch]);
    }

    [Fact]
    public void RdanceTitleIsEditableInHeaderAndAbsentFromStatusPanel()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("Label=\"Tên video\"", create);
        Assert.Contains("Class=\"rdance-header-title-input\"", create);
        Assert.Contains("@bind-Value=\"_title\"", create);
        Assert.Contains("private const string DefaultTitle = \"Video nhảy quảng cáo thời trang\";", create);
        Assert.Contains("Title = _title", create);
        Assert.True(create.IndexOf("Label=\"Tên video\"", StringComparison.Ordinal)
            < create.IndexOf("<div class=\"rdance-workflow-grid\"", StringComparison.Ordinal));

        var createStatusStart = create.IndexOf("<MudText Typo=\"Typo.h6\">Trạng thái", StringComparison.Ordinal);
        var createStatusEnd = create.IndexOf("</MudPaper>", createStatusStart, StringComparison.Ordinal);
        Assert.True(createStatusStart >= 0 && createStatusEnd > createStatusStart);
        Assert.DoesNotContain("Tên video", create[createStatusStart..createStatusEnd]);

        Assert.Contains("Class=\"rdance-header-title-input\"", detail);
        Assert.Contains("@bind-Value=\"_title\"", detail);
        Assert.Contains("_title = string.IsNullOrWhiteSpace(_job.Title) ? DefaultTitle : _job.Title.Trim();", detail);
        Assert.Contains("Title = _title", detail);
        Assert.True(detail.IndexOf("Label=\"Tên video\"", StringComparison.Ordinal)
            < detail.IndexOf("<div class=\"rdance-workflow-grid\"", StringComparison.Ordinal));
        Assert.DoesNotContain("Tên video", detail[detail.IndexOf("<MudText Typo=\"Typo.h6\">Trạng thái job", StringComparison.Ordinal)..detail.IndexOf("</MudPaper>", detail.IndexOf("<MudText Typo=\"Typo.h6\">Trạng thái job", StringComparison.Ordinal), StringComparison.Ordinal)]);
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
