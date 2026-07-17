using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public class AiProviderCatalogTests
{
    [Fact]
    public void CapabilityCatalog_IncludesSceneImageGeneration()
    {
        Assert.Contains(AiProviderCatalog.SceneImageGeneration, AiProviderCatalog.CapabilityCodes);
    }

    [Fact]
    public void QuickDefaultFeatureCatalog_MapsFiveRuntimeFeatures()
    {
        var rows = AiFeatureProviderCatalog.QuickDefaultFeatures.OrderBy(x => x.SortOrder).ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal(
            [
                AiProviderCatalog.AvatarGeneration,
                AiProviderCatalog.CharacterGeneration,
                AiProviderCatalog.SceneImageGeneration,
                AiProviderCatalog.ChibiAvatarGeneration,
                AiProviderCatalog.ImageToVideo
            ],
            rows.Select(x => x.CapabilityCode).ToArray());
        Assert.Equal(
            [
                "admin_avatar_manager",
                "character_manager",
                "render_job_scene_image",
                "avatar_builder",
                "render_job_scene_video"
            ],
            rows.Select(x => x.FeatureCode).ToArray());
    }

    [Fact]
    public void QuickDefaultFeatureCatalog_KeepsAvatarBuilderIndependentFromAvatarGeneration()
    {
        var avatar = AiFeatureProviderCatalog.QuickDefaultFeatures.Single(x => x.FeatureKey == "avatar_image");
        var builder = AiFeatureProviderCatalog.QuickDefaultFeatures.Single(x => x.FeatureKey == "avatar_builder");

        Assert.Equal(AiProviderCatalog.AvatarGeneration, avatar.CapabilityCode);
        Assert.Equal(AiProviderCatalog.ChibiAvatarGeneration, builder.CapabilityCode);
        Assert.NotEqual(avatar.CapabilityCode, builder.CapabilityCode);
    }

    [Theory]
    [InlineData("image_ai_creative_render", "todox_image")]
    [InlineData("todox_image", "todox_image")]
    [InlineData("openrouter_image", "openrouter_image")]
    [InlineData("yescale_task_image", "yescale_task_image")]
    public void ProviderCodeMap_SupportsSceneImageQuickDefaultProviders(string providerCode, string factoryKey)
    {
        Assert.Equal(factoryKey, ProviderCodeMap.ToFactoryKey(providerCode));
        Assert.True(ProviderCodeMap.IsRoutedImageProvider(providerCode));
    }

    [Fact]
    public void ProviderCodeMap_MapsYEScaleVideoProvider()
    {
        Assert.Equal("yescale_task_video", ProviderCodeMap.ToFactoryKey("yescale_task_video"));
    }
}
