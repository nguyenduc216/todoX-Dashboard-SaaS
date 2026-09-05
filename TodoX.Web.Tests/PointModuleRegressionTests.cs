using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PointModuleRegressionTests
{
    [Fact]
    public void PointManagementPageBindsInteractiveTabsAndSynchronizesSelectedRate()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Pages", "Wallets.razor"));

        Assert.Contains("@bind-ActivePanelIndex=\"_activeTabIndex\"", page, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnRateResourceChangedAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnRateQualityChangedAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("SyncSelectedGlobalRate();", page, StringComparison.Ordinal);
        Assert.Contains("SearchFunc=\"SearchCustomersAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("Text=\"Voucher điểm\"", page, StringComparison.Ordinal);
        Assert.Contains("Tạo voucher điểm", page, StringComparison.Ordinal);
        Assert.Contains("Lịch sử điểm", page, StringComparison.Ordinal);
        Assert.Contains("@if (IsCustomer)", page, StringComparison.Ordinal);
        Assert.Contains("_rateValue = 0;", page, StringComparison.Ordinal);
        Assert.Contains("Point rate cannot be negative.", page, StringComparison.Ordinal);
        Assert.Contains("Point override cannot be negative.", page, StringComparison.Ordinal);
        Assert.Contains("\"inactive\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"locked\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"adjust_plus\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"credit\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"purchase\" =>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private decimal _rateValue = 3000", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PointManagementPageIsolatesEachLoadSection()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Pages", "Wallets.razor"));

        Assert.Contains("@inject ILogger<Wallets> Logger", page, StringComparison.Ordinal);
        Assert.Contains("private string? _rateLoadError;", page, StringComparison.Ordinal);
        Assert.Contains("private string? _walletLoadError;", page, StringComparison.Ordinal);
        Assert.Contains("private string? _historyLoadError;", page, StringComparison.Ordinal);
        Assert.Contains("private string? _voucherLoadError;", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Failed loading point rate configuration.\")", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Failed loading customer wallets.\")", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Failed loading point transaction history.\")", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Failed loading point vouchers.\")", page, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(_rateLoadError))", page, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(_walletLoadError))", page, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(_historyLoadError))", page, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(_voucherLoadError))", page, StringComparison.Ordinal);
        Assert.Contains("try", page, StringComparison.Ordinal);
    }

    [Fact]
    public void WalletMutationsAndCustomerVoucherUxAreGuarded()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Pages", "Wallets.razor"));
        var layout = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Layout", "MainLayout.razor"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "WalletService.cs"));

        Assert.Contains("Disabled=\"@_mutationBusy\"", page, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@_redeemBusy\"", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Wallet mutation {Kind} failed for customer {CustomerId}.\", kind, wallet.CustomerId)", page, StringComparison.Ordinal);
        Assert.Contains("Logger.LogError(ex, \"Voucher redemption failed for customer {CustomerId}.\", customerId)", page, StringComparison.Ordinal);
        Assert.Contains("Point amount must be greater than zero.", page, StringComparison.Ordinal);
        Assert.Contains("MutationSuccessMessage", page, StringComparison.Ordinal);
        Assert.Contains("\"topup\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"adjust_plus\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"adjust_minus\" =>", page, StringComparison.Ordinal);
        Assert.Contains("\"refund\" =>", page, StringComparison.Ordinal);
        Assert.Contains("Điểm của tôi", page, StringComparison.Ordinal);
        Assert.Contains("Nạp voucher", page, StringComparison.Ordinal);
        Assert.Contains("Nhận điểm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchCustomersAsync", page[..page.IndexOf("else", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("Title = \"Điểm của tôi\"", layout, StringComparison.Ordinal);
        Assert.Contains("Href = \"/wallets\"", layout, StringComparison.Ordinal);
        Assert.Contains("RequireOwnCustomer(actor, customerId)", service, StringComparison.Ordinal);
        Assert.Contains("transactionType", service, StringComparison.Ordinal);

        var customer = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Role = TodoXUserRole.CustomerOwner,
            IsAuthenticated = true
        };
        Assert.True(NavigationAccessRules.CanAccessPath(customer, "/wallets"));
    }

    [Fact]
    public void ServicePointRatesPageUsesInlineLocalizedOverrideEditor()
    {
        var dialog = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Dialogs", "ServicePointRatesDialog.razor"));

        Assert.Contains("Cấu hình điểm riêng", dialog, StringComparison.Ordinal);
        Assert.Contains("GetOverrideValue(context)", dialog, StringComparison.Ordinal);
        Assert.Contains("SaveOverrideAsync(context)", dialog, StringComparison.Ordinal);
        Assert.Contains("RemoveOverrideAsync(context)", dialog, StringComparison.Ordinal);
        Assert.Contains("EffectiveRate(context)", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _resource", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("private decimal _rate = 3000", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void PointBalanceNotifierIsolatesSubscriberFailures()
    {
        var notifier = new PointBalanceChangeNotifier(LoggerFactory.Create(builder => { }).CreateLogger<PointBalanceChangeNotifier>());
        var observed = Guid.Empty;
        notifier.Changed += _ => throw new InvalidOperationException("subscriber failure");
        notifier.Changed += customerId => observed = customerId;

        var customerId = Guid.NewGuid();
        notifier.NotifyChanged(customerId);

        Assert.Equal(customerId, observed);
    }

    [Fact]
    public void PointBalanceNotifierLogsSubscriberFailures()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "PointBalanceChangeNotifier.cs"));

        Assert.Contains("ILogger<PointBalanceChangeNotifier>", source, StringComparison.Ordinal);
        Assert.Contains("_logger.LogWarning", source, StringComparison.Ordinal);
        Assert.Contains("customerId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerUserIdDoesNotGrantPointAdminPermission()
    {
        var customer = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Role = TodoXUserRole.CustomerOwner,
            IsAuthenticated = true
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            PointModuleAuthorization.Require(customer, PointModulePermissions.PointConfigManage));
        Assert.Throws<UnauthorizedAccessException>(() =>
            PointModuleAuthorization.Require(customer, PointModulePermissions.WalletTopUp));
    }

    [Fact]
    public void AuthorizedPointOperatorCanManageConfiguredRates()
    {
        var operatorUser = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            Role = TodoXUserRole.SystemOperator,
            IsAuthenticated = true,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PointModulePermissions.PointConfigManage
            }
        };

        PointModuleAuthorization.Require(operatorUser, PointModulePermissions.PointConfigManage);
    }

    [Fact]
    public void UserRerenderReferenceIsDeterministic()
    {
        var jobId = Guid.NewGuid();

        var first = PointBillingReference.ForRerender(jobId, "video", "scene-9");
        var second = PointBillingReference.ForRerender(jobId, "video", "scene-9");
        var differentAsset = PointBillingReference.ForRerender(jobId, "video", "scene-10");

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentAsset);
    }

    [Fact]
    public void TimelapsePromptEditRerenderUsesFreshOperationIdentity()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor"));

        Assert.Contains("edit.Rerender ? Guid.NewGuid() : null", page, StringComparison.Ordinal);
        Assert.Contains("RetryImageAsync(JobId, image.ProgressPercent, AuthState.CurrentUser, Guid.NewGuid())", page, StringComparison.Ordinal);
        Assert.Contains("RetryVideoAsync(JobId, clipIndex, AuthState.CurrentUser, Guid.NewGuid())", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PointBalanceNotifierIsSingleton()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Program.cs"));

        Assert.Contains("AddSingleton<IPointBalanceChangeNotifier, PointBalanceChangeNotifier>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RVideoInitialRenderChargesAndSnapshotsBeforeProviderSubmission()
    {
        var handler = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "VideoRender", "SceneVideoRenderHandler.cs"));

        Assert.Contains("CustomerPointRate = pointEstimate.Video.Rate", handler, StringComparison.Ordinal);
        Assert.Contains("BillingIntent = input.BillingIntent", handler, StringComparison.Ordinal);
        Assert.Contains("BillingOperationId = input.BillingOperationId ?? RVideoParentBillingState.ResolveBillingOperationId", handler, StringComparison.Ordinal);
        Assert.Contains("PointCostEstimate = 0", handler, StringComparison.Ordinal);
        Assert.Contains("PointStatus = RenderPointStatuses.Pending", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void RVideoImagePlanCountsOnlyScenesWithoutUsableImageInput()
    {
        var project = new VideoProjectDto
        {
            SourceImageUrl = null,
            UploadedCharacterUrl = null,
            Scenes =
            [
                new() { Id = 1, StaticImageUrl = null },
                new() { Id = 2, StaticImageUrl = null },
                new() { Id = 3, StaticImageUrl = null },
                new() { Id = 4, StaticImageUrl = null },
                new() { Id = 5, StaticImageUrl = "uploaded.png" },
                new() { Id = 6, StaticImageUrl = null }
            ]
        };
        var settings = new RVideoJobSettingsDto { UseReferenceImageForAllScenes = false };
        var selected = new SceneImageVersionDto { Id = Guid.NewGuid(), IsSelected = true, Status = "completed", PublicUrl = "reused.png" };

        var imageCount = project.Scenes.Count(scene =>
            RVideoEffectiveSceneImageSourceResolver.RequiresAiGeneration(
                scene, settings, scene.Id == 6 ? selected : null, project));

        Assert.Equal(4, imageCount);
    }
    [Fact]
    public void PointPricingCalculatorUsesCountAndSecondsFormulas()
    {
        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 3000, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 1500, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 500, "per_render", "global");

        var estimate = PointPricingCalculator.Estimate(6, imageRate, 48, videoRate, 6, voiceRate);

        Assert.Equal(18000, estimate.Image.Points);
        Assert.Equal(72000, estimate.Video.Points);
        Assert.Equal(3000, estimate.Voice.Points);
        Assert.Equal(93000, estimate.TotalPoints);
    }

    [Fact]
    public void RVideoVideoAndVoiceBillingSkipsSecondChargeAfterParentBill()
    {
        var handler = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "VideoRender", "SceneVideoRenderHandler.cs"));
        var audio = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs"));

        Assert.Contains("CustomerPointRate = pointEstimate.Video.Rate", handler, StringComparison.Ordinal);
        Assert.Contains("CustomerPointRate = customerPointRate", audio, StringComparison.Ordinal);
        Assert.Contains("customerPointRate = pointEstimate.Voice.Rate", audio, StringComparison.Ordinal);
        Assert.Contains("billingOperationId = billingOperationId", audio, StringComparison.Ordinal);
        Assert.Contains("PointCostEstimate = parentJobBilled ? 0", audio, StringComparison.Ordinal);
        Assert.Contains("PointStatus = parentJobBilled", audio, StringComparison.Ordinal);
    }

    [Fact]
    public void RVideoVideoCompletionCalculatesActualPointsFromDurationAndRate()
    {
        var method = typeof(RVideoSceneVideoCompletionService).GetMethod(
            "CalculateActualVideoPoints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var first = (decimal)method!.Invoke(null, new object[] { 6m, 2.5m })!;
        var second = (decimal)method.Invoke(null, new object[] { 5.5m, 2m })!;

        Assert.Equal(15m, first);
        Assert.Equal(11m, second);
    }

    [Fact]
    public void TimelapseSellPriceSnapshotCarriesUnifiedEstimate()
    {
        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 3000, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 1500, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 500, "per_render", "global");
        var estimate = PointPricingCalculator.Estimate(5, imageRate, 36, videoRate, 0, voiceRate);

        var snapshot = TimelapseSellPriceSnapshot.FromPointEstimate(estimate, 6);

        Assert.Equal(15000, snapshot.ImageSubtotal);
        Assert.Equal(54000, snapshot.VideoSubtotal);
        Assert.Equal(0, snapshot.VoiceSubtotal);
        Assert.Equal(69000, snapshot.TotalPoints);
    }

    [Fact]
    public void RDanceQueueRenderFreezesConsumedImageAndUsesLogicalTotal()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "DanceSell", "DanceSellPhase2Services.cs"));

        Assert.Contains("logicalTotalPoints", source, StringComparison.Ordinal);
        Assert.Contains("total_planned_points = pointEstimate.TotalPoints", source, StringComparison.Ordinal);
        Assert.Contains("total_charged_points = chargeNow", source, StringComparison.Ordinal);
        Assert.Contains("PointCostEstimate = chargeNow", source, StringComparison.Ordinal);
        Assert.Contains("chargeNow", source, StringComparison.Ordinal);
        Assert.Contains("var imageCount = isDirectReference ? billableStaticImageCount : 1;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PreRenderUsagePlanSumsExplicitSceneDurations()
    {
        var plan = new PreRenderUsagePlan(
            null,
            4,
            ServiceSellPriceQualityTiers.Standard,
            new[]
            {
                new PreRenderVideoScene(1, 5),
                new PreRenderVideoScene(2, 7),
                new PreRenderVideoScene(3, 6),
                new PreRenderVideoScene(4, 8),
                new PreRenderVideoScene(5, 5),
                new PreRenderVideoScene(6, 9)
            },
            ServiceSellPriceQualityTiers.Standard,
            6,
            ServiceSellPriceQualityTiers.Standard,
            true).Validate();

        Assert.Equal(40, plan.VideoSeconds);
        Assert.Equal(4, plan.ImageCount);
        Assert.Equal(6, plan.VoiceCount);
        Assert.Equal(40, plan.ToPricingRequest().VideoSeconds);
    }

    [Fact]
    public void PreRenderUsagePlanRejectsMissingSceneDuration()
    {
        var plan = new PreRenderUsagePlan(
            null, 0, ServiceSellPriceQualityTiers.Standard,
            new[] { new PreRenderVideoScene(1, 0) },
            ServiceSellPriceQualityTiers.Standard, 0,
            ServiceSellPriceQualityTiers.Standard, false);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Equal("VIDEO_SCENE_DURATION_REQUIRED", exception.Message);
    }

    [Fact]
    public void CoreBillingUsesPointPricingService()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "Platform", "CoreBillingService.cs"));

        Assert.Contains("IPointPricingService", source);
        Assert.Contains("PointPricingEstimateRequest", source);
        Assert.Contains("PointPricingEstimate?", source);
    }

    [Fact]
    public void PointModuleMigrationSeedsRatesForEveryTenant()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "database", "migrations", "20260902_point_module.sql"));

        Assert.Contains("FROM system.tenants", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM crm.customers", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PointModuleMigrationConstrainsResourceUnitPairs()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "database", "migrations", "20260902_point_module.sql"));

        Assert.Contains("chk_point_rate_config_resource_unit", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chk_service_point_rate_override_resource_unit", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'video' AND lower(unit) = 'per_second'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'image' AND lower(unit) = 'per_render'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'voice' AND lower(unit) = 'per_render'", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot
        => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
