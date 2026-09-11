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
        Assert.Contains("@inject IDanceSellCustomerPricing CustomerPricing", detail);
        Assert.Contains("DanceSellMotionProviderContract.ResolveProviderMode(route, _job.Mode)", detail);
        Assert.Contains("CustomerPricing.EstimateAsync(_job, durationSeconds.Value, quality, imageCount)", detail);
        Assert.Contains("ResolveMotionDurationSeconds(_job)", detail);
        Assert.DoesNotContain("PointPricing.EstimateAsync(", detail);
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
    public void RdanceDetailClassifiesPrimaryJobLoadErrorsOnlyAsNotFoundOrUnauthorized()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var classifierStart = detail.IndexOf("private static string ClassifyPrimaryJobLoadError", StringComparison.Ordinal);
        Assert.True(classifierStart >= 0);
        var classifierEnd = detail.IndexOf("\n    private ", classifierStart + 1, StringComparison.Ordinal);
        var classifier = detail[classifierStart..classifierEnd];

        Assert.Contains("\"DANCE_SELL_NOT_FOUND\" => LoadNotFoundMessage", classifier);
        Assert.Contains("\"DANCE_SELL_UNAUTHORIZED\" => LoadUnauthorizedMessage", classifier);
        Assert.Contains("_ => DetailRefreshErrorMessage", classifier);
        Assert.Contains("private const string LoadNotFoundMessage", detail);
        Assert.Contains("private const string LoadUnauthorizedMessage", detail);
    }

    [Fact]
    public void RdanceDetailKeepsLoadedJobWhenPostLoadRefreshFails()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var reloadStart = detail.IndexOf("private async Task ReloadAsync", StringComparison.Ordinal);
        var reloadEnd = detail.IndexOf("private static string ClassifyPrimaryJobLoadError", reloadStart, StringComparison.Ordinal);
        var reload = detail[reloadStart..reloadEnd];

        var loadCall = reload.IndexOf("_job = await DanceSell.GetAsync(JobId, AuthState.CurrentUser);", StringComparison.Ordinal);
        var postLoadCall = reload.IndexOf("RenderJobs.GetAsync(renderJobId)", StringComparison.Ordinal);
        var postLoadCatch = reload.IndexOf("_loadError = DetailRefreshErrorMessage;", StringComparison.Ordinal);
        Assert.True(loadCall >= 0);
        Assert.True(postLoadCall > loadCall);
        Assert.True(postLoadCatch > postLoadCall);

        var postLoadCatchBlock = reload[postLoadCatch..];
        Assert.DoesNotContain("_job = null", postLoadCatchBlock);
        Assert.DoesNotContain("LoadNotFoundMessage", postLoadCatchBlock);
        Assert.Contains("Logger.LogWarning(ex, \"RDance detail refresh failed after primary job load", postLoadCatchBlock);
    }

    [Fact]
    public void RdanceDetailClearsJobOnlyWhenPrimaryJobLoadFails()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var reloadStart = detail.IndexOf("private async Task ReloadAsync", StringComparison.Ordinal);
        var reloadEnd = detail.IndexOf("private static string ClassifyPrimaryJobLoadError", reloadStart, StringComparison.Ordinal);
        var reload = detail[reloadStart..reloadEnd];

        var primaryCatch = reload.IndexOf("_loadError = ClassifyPrimaryJobLoadError(ex);", StringComparison.Ordinal);
        var postLoadCatch = reload.IndexOf("_loadError = DetailRefreshErrorMessage;", StringComparison.Ordinal);

        Assert.True(primaryCatch >= 0);
        Assert.True(postLoadCatch > primaryCatch);
        Assert.Contains("_job = null", reload[..primaryCatch]);
        Assert.DoesNotContain("_job = null", reload[postLoadCatch..]);
        Assert.Contains("await InvokeAsync(StateHasChanged);", reload[..postLoadCatch]);
        Assert.Contains("return;", reload[..postLoadCatch]);
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
        Assert.Contains("_latestMotionOperation", detail);
        Assert.Contains("ReadPositiveInt(_latestMotionOperation?.RequestJson", detail);
        Assert.DoesNotContain(".rdance-overlay-actions { position: absolute;", detail);
        Assert.True(detail.IndexOf("class=\"rdance-overlay-actions\"", StringComparison.Ordinal)
            < detail.IndexOf("class=\"rdance-media-frame rdance-video-frame\"", StringComparison.Ordinal));
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
    public void RdanceCreateReloadsAfterMotionUploadAndLeavesQueueingToExplicitDetailAction()
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
        Assert.Contains("private async Task QueueRenderFromUserActionAsync()", detail);
        Assert.Contains("DanceSell.QueueRenderAsync(job.Id", detail);
    }

    [Fact]
    public void RdanceUploadReferenceAndPollingPathsCannotQueueRender()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        foreach (var methodName in new[]
        {
            "private async Task StageTikTokAsync",
            "private async Task OnMotionSelected",
            "private async Task AutoPrepareReferenceAsync",
            "private async Task ContinueAutoFinishAsync",
            "private async Task ReloadAsync",
            "private async Task PollLoopAsync"
        })
        {
            var start = detail.IndexOf(methodName, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing method: {methodName}");
            var next = detail.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
            var method = next < 0 ? detail[start..] : detail[start..next];
            Assert.DoesNotContain("QueueRenderAsync", method);
        }

        var explicitStart = detail.IndexOf("private async Task QueueRenderFromUserActionAsync", StringComparison.Ordinal);
        Assert.True(explicitStart >= 0);
        Assert.Contains("DanceSell.QueueRenderAsync(job.Id", detail[explicitStart..]);
    }

    [Fact]
    public void RdanceRenderRequiresReadyMotionAndPositiveDuration()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("private bool HasValidMotionDuration", StringComparison.Ordinal);
        var end = detail.IndexOf("private bool IsActive", start, StringComparison.Ordinal);
        var gate = detail[start..end];

        Assert.Contains("MotionDurationSeconds is > 0", gate);
        Assert.Contains("MotionVideoMediaId is not null", gate);
        Assert.Contains("!string.IsNullOrWhiteSpace(_job.MotionVideoUrl)", gate);
        Assert.Contains("SourceStageStatus == DanceSellSourceStageStatuses.Ready", gate);
        Assert.Contains("PreparedReferenceStatus == DanceSellReferenceStatuses.Approved", gate);
        Assert.Contains("HasMotionVideoWithoutValidDuration", detail);
        Assert.Contains("DANCE_SELL_VIDEO_DURATION_REQUIRED", detail);
        Assert.Contains("Chưa xác định được thời lượng video. Vui lòng tải lại video.", detail);
    }

    [Fact]
    public void RdanceAutoFinishTextDoesNotPromiseAutomaticRender()
    {
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("@AutoFinishHelpText", create);
        Assert.Contains("@AutoFinishHelpText", detail);
        Assert.Contains("Bạn vẫn cần bấm \\\"Tạo video\\\" để bắt đầu render.", create);
        Assert.Contains("\\u1ea1o video", detail);
        Assert.DoesNotContain("tự động chuẩn bị ảnh, duyệt ảnh và tạo video khi dữ liệu đã đầy đủ", create);
        Assert.DoesNotContain("tự động chuẩn bị ảnh, duyệt ảnh và tạo video khi dữ liệu đã đầy đủ", detail);
    }

    [Fact]
    public void RdanceMotionDurationIsPersistedAndCopiedIntoRenderOperation()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var repository = ReadRepoFile("Services", "DanceSell", "DanceSellRepository.cs");

        Assert.Contains("DanceSellMotionDuration.TryGetBillableSeconds(content)", service);
        Assert.Contains("UpdateMotionUploadAsync(job.Id", service);
        Assert.Contains("UpdateMotionTikTokAsync(job.Id", service);
        Assert.Contains("durationSeconds }", service);
        Assert.Contains("request_json=jsonb_set", repository);
        Assert.Contains("'{durationSeconds}'", repository);
        Assert.Contains("to_jsonb(@durationSeconds)", repository);
        var resolverStart = service.IndexOf("private async Task<int> ResolveMotionDurationSecondsAsync", StringComparison.Ordinal);
        var resolver = service[resolverStart..];
        var jobDuration = resolver.IndexOf("ReadInt(job.RequestJson", StringComparison.Ordinal);
        var operationDuration = resolver.IndexOf("latestMotionOperation?.RequestJson", StringComparison.Ordinal);
        Assert.True(jobDuration >= 0 && operationDuration > jobDuration);
        Assert.Contains("GetLatestOperationAsync", resolver);
        Assert.Contains("PersistMotionDurationAsync(job.Id, persistedOperationDuration.Value", resolver);
        Assert.DoesNotContain("route.ConfigJson", resolver);
        Assert.DoesNotContain("DanceSellCostEstimate estimate", resolver);
        Assert.DoesNotContain("DanceSellProviderRouteDto route", resolver);
    }

    [Fact]
    public void RdanceDurationGateRejectsInvalidPersistedValuesAndRouteDefaults()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var resolverStart = service.IndexOf("private async Task<int> ResolveMotionDurationSecondsAsync", StringComparison.Ordinal);
        var resolver = service[resolverStart..];

        Assert.Contains("persistedJobDuration is > 0", resolver);
        Assert.Contains("persistedOperationDuration is > 0", resolver);
        Assert.Contains("derived is > 0", resolver);
        Assert.DoesNotContain("ReadInt(route.ConfigJson", resolver);
        Assert.DoesNotContain("ReadInt(route?.ConfigJson", detail);
        Assert.Contains("ReadPositiveInt(job.RequestJson", detail);
        Assert.Contains("ReadPositiveInt(_latestMotionOperation?.RequestJson", detail);
        Assert.Contains("return value is > 0 ? value : null", detail);
        Assert.Contains("DANCE_SELL_VIDEO_DURATION_REQUIRED", resolver);
    }

    [Fact]
    public void RdanceCustomerPricingUsesPersistedServiceIdentityAndKeepsLegacyFallback()
    {
        var pricing = ReadRepoFile("Services", "DanceSell", "DanceSellCustomerPricing.cs");
        var phase2 = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var create = ReadRepoFile("Components", "Pages", "RDanceJobCreate.razor");

        Assert.Contains("ReadGuid(job.RequestJson, \"serviceId\", \"service_id\")", pricing);
        Assert.Contains("await _catalog.GetByIdAsync(id, ct)", pricing);
        Assert.Contains("await _sellPrices.EstimateAsync", pricing);
        Assert.Contains("FixedTodoXServiceCatalog.RDance", pricing);
        Assert.Contains("ServiceId = ServiceId", create);
        Assert.Contains("ServiceCode = ServiceCode", create);
        Assert.Contains("ServiceId = service?.Id", phase2);
        Assert.Contains("ServiceCode = service?.ServiceCode", phase2);
        Assert.Contains("DANCE_SELL_SERVICE_ID_MISMATCH", phase2);
    }

    [Fact]
    public void RdanceDetailAutoprepareBranchesByReferenceModeAndGuardsDuplicateWork()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("private async Task AutoPrepareReferenceAsync", StringComparison.Ordinal);
        var end = detail.IndexOf("private string? ValidateMotionVideo", start, StringComparison.Ordinal);
        var method = detail[start..end];

        Assert.Contains("job.PreparedReferenceStatus is DanceSellReferenceStatuses.Approved or DanceSellReferenceStatuses.Ready", method);
        Assert.Contains("if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)", method);
        Assert.Contains("var isReferenceGenerating = IsReferenceGenerating;", method);
        Assert.Contains("if (isReferenceGenerating)", method);
        Assert.Contains("var hasProduct = job.ProductMediaId is not null", method);
        Assert.Contains("if (!isReferenceGenerating && hasCharacter && !hasProduct)", method);
        Assert.Contains("References.ApproveCharacterAsync(job.Id", method);
        Assert.Contains("else if (!isReferenceGenerating && hasCharacter && hasProduct)", method);
        Assert.Contains("References.AutoPrepareAsync(job.Id", method);
        Assert.Contains("await ContinueAutoFinishAsync();", method);

        var characterBranch = method.IndexOf("if (!isReferenceGenerating && hasCharacter && !hasProduct)", StringComparison.Ordinal);
        var productBranch = method.IndexOf("else if (!isReferenceGenerating && hasCharacter && hasProduct)", StringComparison.Ordinal);
        var approveCall = method.IndexOf("References.ApproveCharacterAsync(job.Id", StringComparison.Ordinal);
        var generatingAutoPrepareCall = method.IndexOf("References.AutoPrepareAsync(job.Id", StringComparison.Ordinal);
        var productAutoPrepareCall = method.IndexOf("References.AutoPrepareAsync(job.Id", generatingAutoPrepareCall + 1, StringComparison.Ordinal);
        Assert.True(characterBranch >= 0 && productBranch > characterBranch);
        Assert.True(approveCall > characterBranch && approveCall < productBranch);
        Assert.True(generatingAutoPrepareCall > 0 && generatingAutoPrepareCall < characterBranch);
        Assert.True(productAutoPrepareCall > productBranch);
        Assert.DoesNotContain("References.AutoPrepareAsync(job.Id", method[characterBranch..productBranch]);

        var lifecycle = detail[detail.IndexOf("protected override async Task OnParametersSetAsync", StringComparison.Ordinal)..];
        Assert.Contains("if (ShouldResumeAutoReference)", lifecycle);
        Assert.Contains("await AutoPrepareReferenceAsync();", lifecycle);
        var polling = detail[detail.IndexOf("private async Task PollLoopAsync", StringComparison.Ordinal)..];
        Assert.Contains("if (ShouldResumeAutoReference)", polling);
        var autoFinish = detail[detail.IndexOf("private async Task ContinueAutoFinishAsync", StringComparison.Ordinal)..];
        Assert.Contains("if (IsActive", autoFinish);
        Assert.Contains("DanceSellJobStatuses.Completed", autoFinish);
        Assert.Contains("RenderJobStatuses.Cancelled", autoFinish);
    }

    [Fact]
    public void RdanceDetailRetryResumesDraftsWithoutReuploadAndGuardsActiveRenders()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var start = detail.IndexOf("private async Task RetryAsync()", StringComparison.Ordinal);
        var end = detail.IndexOf("private async Task DownloadResultAsync", start, StringComparison.Ordinal);
        var method = detail[start..end];

        Assert.Contains("await ReloadAsync();", method);
        Assert.Contains("if (IsActive", method);
        Assert.Contains("DanceSellJobStatuses.Draft", method);
        Assert.Contains("MotionVideoMediaId is null", method);
        Assert.Contains("await AutoPrepareReferenceAsync();", method);
        Assert.Contains("await ContinueAutoFinishAsync();", method);
        Assert.Contains("DanceSell.RetryAsync(job.Id", method);
        Assert.DoesNotContain("UploadMotionAsync", method);
    }

    [Fact]
    public void RdanceDetailUsesSelectedUsableReferenceForAutoFinish()
    {
        var detail = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");
        var resolverStart = detail.IndexOf("private static DanceSellReferenceVersionDto? ResolveAutoFinishReference", StringComparison.Ordinal);
        var resolverEnd = detail.IndexOf("private async Task EnsureEditableAsync", resolverStart, StringComparison.Ordinal);
        var resolver = detail[resolverStart..resolverEnd];

        Assert.Contains("version.IsSelected", resolver);
        Assert.Contains("version.Status is DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved", resolver);
        Assert.Contains("!string.IsNullOrWhiteSpace(version.PublicUrl)", resolver);
        Assert.Contains("_latestReference = ResolveAutoFinishReference(versions);", detail);
        Assert.Contains("_latestReference = ResolveAutoFinishReference(_referenceVersions);", detail);
    }

    [Fact]
    public void RdanceCharacterApprovalReconcilesExistingUsableReferenceBeforeCreatingFallback()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var start = service.IndexOf("public async Task<DanceSellJobDto> ApproveCharacterAsync", StringComparison.Ordinal);
        var end = service.IndexOf("private async Task<DanceSellJobDto> PrepareCharacterReferenceAsync", start, StringComparison.Ordinal);
        var method = service[start..end];

        Assert.Contains("var reusable = versions.FirstOrDefault(version =>", method);
        Assert.Contains("version.IsSelected", method);
        Assert.Contains("version.Status is DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved", method);
        Assert.Contains("version.MediaId is not null", method);
        Assert.Contains("!string.IsNullOrWhiteSpace(version.PublicUrl)", method);
        Assert.Contains("EnsureReferenceVersionRatioMatchesJob(reusable, job);", method);
        Assert.Contains("await _repo.SelectReferenceVersionAsync(job.Id, reusable.Id, ct);", method);
        Assert.Contains("DanceSellReferenceStatuses.Approved", method);
        Assert.Contains("reusable.MediaId", method);
        Assert.Contains("reusable.PublicUrl", method);

        var reuseBranch = method.IndexOf("if (reusable is not null)", StringComparison.Ordinal);
        var createCall = method.IndexOf("CreateReferenceVersionAsync", StringComparison.Ordinal);
        Assert.True(reuseBranch >= 0);
        Assert.True(createCall > reuseBranch);
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
