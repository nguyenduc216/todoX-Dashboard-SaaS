using System.Text;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services;
using TodoX.Web.Services.Timelapse;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoTimelapseBillingCountsRegressionTests
{
    [Fact]
    public void RVideoInitialSettingOnBillsEveryStaticInputScene()
    {
        var imageWorkSources = new[]
        {
            StaticSource("https://example.test/static-1.png"),
            StaticSource("https://example.test/static-2.png"),
            MissingSource(),
            MissingSource()
        };

        var imageCount = RVideoInitialPointEstimateService.ResolveInitialImageCount(
            imageWorkSources,
            chargeStaticImagePoints: true);

        Assert.Equal(2, imageCount);
    }

    [Fact]
    public void RVideoInitialSettingOnDoesNotDeduplicateSharedStaticInputs()
    {
        var billingScenes = Enumerable.Range(1, 5)
            .Select(index => CreateScene(index, "https://example.test/shared.png"))
            .ToArray();
        var imageWorkSources = billingScenes
            .Select(_ => StaticSource("https://example.test/shared.png"))
            .ToArray();

        var imageCount = RVideoInitialPointEstimateService.ResolveInitialImageCount(
            imageWorkSources,
            chargeStaticImagePoints: true);

        Assert.Equal(5, imageCount);
    }

    [Fact]
    public void RVideoInitialSettingOffBillsZeroEvenWithMixedStaticAndAiScenes()
    {
        var imageWorkSources = new[]
        {
            StaticSource("https://example.test/static-1.png"),
            StaticSource("https://example.test/static-2.png"),
            MissingSource(),
            MissingSource()
        };

        var imageCount = RVideoInitialPointEstimateService.ResolveInitialImageCount(
            imageWorkSources,
            chargeStaticImagePoints: false);

        Assert.Equal(0, imageCount);
    }

    [Fact]
    public void RVideoVideoUserRerenderPreflightDoesNotIncludeImagePoints()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("input.BillingIntent == PointBillingIntent.InitialRender", source);
        Assert.Contains("EstimateSceneVideoRerenderPointsAsync", source);
        Assert.Contains("new PreRenderUsagePlan(\n            serviceId,\n            0,", normalized);
    }

    [Fact]
    public void RVideoInitialStaticDebitAllStaticSettingOnMatchesEstimate()
    {
        var sources = Enumerable.Range(1, 6)
            .Select(index => StaticSource($"https://example.test/static-{index}.png"))
            .ToArray();

        var staticDirectSceneCount = RVideoInitialStaticImageDebit.ResolveStaticDirectSceneCount(
            chargeStaticImagePoints: true,
            sources);
        var points = RVideoInitialStaticImageDebit.ResolveStaticDirectPoints(
            imageRate: 0.5m,
            staticDirectSceneCount);

        Assert.Equal(6, staticDirectSceneCount);
        Assert.Equal(3.0m, points);
    }

    [Fact]
    public void RVideoInitialStaticDebitSettingOffDoesNotChargeStaticDirectScenes()
    {
        var sources = Enumerable.Range(1, 6)
            .Select(_ => StaticSource("https://example.test/static.png"))
            .ToArray();

        var staticDirectSceneCount = RVideoInitialStaticImageDebit.ResolveStaticDirectSceneCount(
            chargeStaticImagePoints: false,
            sources);
        var points = RVideoInitialStaticImageDebit.ResolveStaticDirectPoints(
            imageRate: 0.5m,
            staticDirectSceneCount);

        Assert.Equal(0, staticDirectSceneCount);
        Assert.Equal(0m, points);
    }

    [Fact]
    public void RVideoInitialStaticDebitMixedStaticAndAiChargesOnlyStaticDirectNow()
    {
        var sources = new[]
        {
            StaticSource("https://example.test/static-1.png"),
            StaticSource("https://example.test/static-2.png"),
            StaticSource("https://example.test/static-3.png"),
            StaticSource("https://example.test/static-4.png"),
            SceneImageVersionSource(),
            SceneImageVersionSource()
        };

        var imageBatchStaticCount = RVideoInitialStaticImageDebit.ResolveStaticDirectSceneCount(
            chargeStaticImagePoints: true,
            sources);
        var videoBatchStaticCount = RVideoInitialStaticImageDebit.ResolveStaticDirectSceneCount(
            chargeStaticImagePoints: false,
            sources);

        Assert.Equal(4, imageBatchStaticCount);
        Assert.Equal(0, videoBatchStaticCount);
        Assert.Equal(2.0m, RVideoInitialStaticImageDebit.ResolveStaticDirectPoints(0.5m, imageBatchStaticCount));
    }

    [Fact]
    public void RVideoInitialStaticDebitSharedStaticImageStillChargesPerScene()
    {
        var sources = Enumerable.Range(1, 5)
            .Select(_ => StaticSource("https://example.test/shared.png"))
            .ToArray();

        var staticDirectSceneCount = RVideoInitialStaticImageDebit.ResolveStaticDirectSceneCount(
            chargeStaticImagePoints: true,
            sources);

        Assert.Equal(5, staticDirectSceneCount);
    }

    [Fact]
    public void RVideoInitialStaticDebitReferenceIsStableForSameBillingOperation()
    {
        var operationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sameReference = RVideoInitialStaticImageDebit.BuildReferenceId(operationId);
        var repeatedReference = RVideoInitialStaticImageDebit.BuildReferenceId(operationId);
        var differentReference = RVideoInitialStaticImageDebit.BuildReferenceId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        Assert.Equal(sameReference, repeatedReference);
        Assert.NotEqual(sameReference, differentReference);
    }

    [Fact]
    public void RVideoInitialStaticDebitUsesQuantityRateAndStableReferenceInWalletCharge()
    {
        var imageBatch = ReadRepoFile("Services", "Render", "SceneImageBatchRenderHandler.cs");
        var videoBatch = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");
        var normalizedImageBatch = imageBatch.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedVideoBatch = videoBatch.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("RVideoInitialStaticImageDebit.BuildReferenceId(billingOperationId)", imageBatch);
        Assert.Contains("RVideoInitialStaticImageDebit.BuildReferenceId(billingOperationId)", videoBatch);
        Assert.Contains("staticDirectPoints,\n            staticDirectSceneCount", normalizedImageBatch);
        Assert.Contains("staticDirectPoints,\n            staticDirectSceneCount", normalizedVideoBatch);
        Assert.Contains("\"rvideo_initial_render_static_image\"", imageBatch);
        Assert.Contains("\"rvideo_initial_static_image\"", imageBatch);
        Assert.Contains("\"rvideo_initial_render_static_image\"", videoBatch);
        Assert.Contains("\"rvideo_initial_static_image\"", videoBatch);
    }

    [Fact]
    public void RVideoUserRerenderImageBillsOneImagePerSuccessfulSceneVersion()
    {
        var imageRate = new PointPricingRate(
            PointPricingResourceTypes.Image,
            ServiceSellPriceQualityTiers.Premium,
            0.5m,
            "image",
            "test",
            ServiceId: null);
        var zeroRate = new PointPricingRate(
            PointPricingResourceTypes.Video,
            ServiceSellPriceQualityTiers.Standard,
            0m,
            "second",
            "test",
            ServiceId: null);

        var estimate = PointPricingCalculator.Estimate(2, imageRate, 0, zeroRate, 0, zeroRate);

        Assert.Equal(2, estimate.Image.Count);
        Assert.Equal(1.0m, estimate.Image.Points);
        Assert.Equal(estimate.Image.Points, estimate.TotalPoints);
    }

    [Fact]
    public void RVideoVideoRerenderChargesSceneDurationTimesRate()
    {
        var points = RVideoSceneVideoCompletionService.CalculateActualVideoPoints(
            durationSeconds: 6m,
            ratePerSecond: 0.8m);

        Assert.Equal(4.8m, points);
    }

    [Fact]
    public void RVideoRerenderHandlersKeepUserRerenderChargeAndSystemRetryUsageOnlyPaths()
    {
        var imageHandler = ReadRepoFile("Services", "Render", "SceneImageRenderWorkItemHandler.cs");
        var videoBatch = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");
        var videoCompletion = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        Assert.Contains("input.BillingIntent != PointBillingIntent.SystemRetry", imageHandler);
        Assert.Contains("rvideo_user_rerender_image", imageHandler);
        Assert.Contains("rate, 1", imageHandler);
        Assert.Contains("rvideo_system_retry_image", imageHandler);

        Assert.Contains("request.BillingIntent != PointBillingIntent.SystemRetry", videoCompletion);
        Assert.Contains("rvideo_user_rerender_video", videoCompletion);
        Assert.Contains("actualVideoPoints", videoCompletion);
        Assert.Contains("rvideo_system_retry_video", videoCompletion);
        Assert.Contains("EstimateSceneVideoRerenderPointsAsync", videoBatch);
    }

    [Fact]
    public void TimelapseImageRerenderBillsEveryCascadeGeneratedStage()
    {
        var imageCount = TimelapseJobService.ResolveImageRerenderBillingCount(sceneCount: 4, progressPercent: 75);
        var points = TimelapseJobService.ResolveImageRerenderPoints(imageRate: 0.5m, imageCount);

        Assert.Equal(4, imageCount);
        Assert.Equal(2.0m, points);
    }

    [Fact]
    public void TimelapseImageRerenderBillsOneWhenCascadeHasOnlySelectedStage()
    {
        var imageCount = TimelapseJobService.ResolveImageRerenderBillingCount(sceneCount: 4, progressPercent: 0);
        var points = TimelapseJobService.ResolveImageRerenderPoints(imageRate: 0.5m, imageCount);

        Assert.Equal(1, imageCount);
        Assert.Equal(0.5m, points);
    }

    [Fact]
    public void TimelapseRetryImageChargeUsesCascadeCountAndQuantity()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseJobService.cs");

        Assert.Contains("ResolveImageRerenderBillingCount(view.Snapshot.SceneCount, progressPercent)", source);
        Assert.Contains("ResolveImageRerenderPoints(rate.Rate, imageCount)", source);
        Assert.Contains("currentUser.CustomerId, currentUser.UserId, requiredPoints, imageCount", source);
        Assert.DoesNotContain("currentUser.CustomerId, currentUser.UserId, rate.Rate, 1", source);
    }

    private static VideoProjectSceneDto CreateScene(int index, string? staticImageUrl = null)
        => new()
        {
            Id = index,
            SceneIndex = index,
            DurationSeconds = 6,
            StaticImageUrl = staticImageUrl,
            Status = VideoSceneStatuses.Draft
        };

    private static RVideoEffectiveSceneImageSource MissingSource()
        => new(false, null, null, null, RVideoEffectiveSceneImageSourceResolver.Missing);

    private static RVideoEffectiveSceneImageSource StaticSource(string url)
        => new(false, null, url, null, RVideoEffectiveSceneImageSourceResolver.SceneStaticImage);

    private static RVideoEffectiveSceneImageSource SceneImageVersionSource()
        => new(false, Guid.NewGuid(), "https://example.test/generated.png", "generated.png", RVideoEffectiveSceneImageSourceResolver.SceneImageVersion);

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(
            Path.Combine(RepoRoot, Path.Combine(parts)),
            Encoding.UTF8);

    private static string RepoRoot
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
