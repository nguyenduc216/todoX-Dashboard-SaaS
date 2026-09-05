using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class StaticImageBillingPolicyRegressionTests
{
    [Fact]
    public void RdanceStaticInputsAreDistinctByPersistedInputIdentity()
    {
        var shared = Guid.NewGuid();
        var job = new DanceSellJobDto
        {
            CharacterMediaId = shared,
            CharacterObjectKey = "character/shared.png",
            CharacterImageUrl = "https://example.invalid/character/shared.png",
            ProductMediaId = shared,
            ProductObjectKey = "product/shared.png",
            ProductImageUrl = "https://example.invalid/product/shared.png",
            DirectReferenceMediaId = shared,
            DirectReferenceObjectKey = "direct/shared.png",
            DirectReferenceUrl = "https://example.invalid/direct/shared.png"
        };

        Assert.Equal(1, StaticImageBillingPolicy.ResolveRdanceStaticInputCount(job));
        Assert.Equal(1, StaticImageBillingPolicy.ResolveBillableStaticImageCount(1, true));
        Assert.Equal(0, StaticImageBillingPolicy.ResolveBillableStaticImageCount(1, false));
    }

    [Fact]
    public void TimelapseStaticInputsDeduplicateSharedMediaAcrossAnchors()
    {
        var mediaId = Guid.NewGuid();
        var snapshot = new TimelapseJobSnapshot
        {
            OriginalImage = new TimelapseOriginalImageSnapshot
            {
                MediaId = mediaId,
                ObjectKey = "timelapse/original.png",
                PublicUrl = "https://example.invalid/timelapse/original.png"
            },
            StartImage = new TimelapseOriginalImageSnapshot
            {
                MediaId = mediaId,
                ObjectKey = "timelapse/start.png",
                PublicUrl = "https://example.invalid/timelapse/start.png"
            }
        };

        Assert.Equal(1, StaticImageBillingPolicy.ResolveTimelapseStaticInputCount(snapshot));
        Assert.Equal(0, StaticImageBillingPolicy.ResolveBillableStaticImageCount(1, false));
    }

    [Fact]
    public void RVideoStaticInputsDeduplicateResolvedSharedSources()
    {
        var selectedVersionId = Guid.NewGuid();
        var shared = new[]
        {
            new RVideoEffectiveSceneImageSource(true, null, "https://example.invalid/shared.png", "shared.png", "shared_reference"),
            new RVideoEffectiveSceneImageSource(true, null, "https://example.invalid/shared.png", "shared.png", "shared_reference"),
            new RVideoEffectiveSceneImageSource(false, selectedVersionId, "https://example.invalid/scene-2.png", "scene-2.png", RVideoEffectiveSceneImageSourceResolver.SceneImageVersion),
            new RVideoEffectiveSceneImageSource(false, selectedVersionId, "https://example.invalid/scene-2.png", "scene-2.png", RVideoEffectiveSceneImageSourceResolver.SceneImageVersion),
            new RVideoEffectiveSceneImageSource(false, null, null, null, RVideoEffectiveSceneImageSourceResolver.Missing)
        };

        Assert.Equal(2, StaticImageBillingPolicy.ResolveRVideoStaticInputCount(shared));
        Assert.Equal(2, StaticImageBillingPolicy.ResolveBillableStaticImageCount(2, true));
        Assert.Equal(0, StaticImageBillingPolicy.ResolveBillableStaticImageCount(2, false));
    }

    [Fact]
    public void RVideoInitialEstimateWiresStaticImageBillingSetting()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TodoX.Web",
            "Services", "VideoRender", "RVideoInitialPointEstimateService.cs"));

        Assert.Contains("GetChargeStaticImagePointsAsync", source);
        Assert.Contains("var staticInputCount = StaticImageBillingPolicy.ResolveRVideoStaticInputCount(imageSources);", source);
        Assert.Contains("var imageCount = StaticImageBillingPolicy.ResolveBillableStaticImageCount(staticInputCount, chargeStaticImagePoints);", source);
    }
}
