using System.Text.Json;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ImagePromptPlaceholderRegressionTests
{
    private static TodoXVideoPromptParseResult Parse(string image, string? fallback = null, string motion = "Camera pans across the product")
        => new TodoXVideoPromptParser().Parse(JsonSerializer.Serialize(new
        {
            video_title = "Product", scenes = new[] { new { scene = 1, duration_seconds = 8, image_prompt = image, image_prompt_fallback = fallback, motion_prompt = motion } }
        }));

    [Theory]
    [InlineData("Show TodoX Dashboard on a laptop in a bright office.")]
    [InlineData("Show todoX branding beside Microsoft and Adobe logos.")]
    [InlineData("Show MYTODO: branding on the product label.")]
    public void BrandNamesAreUsableWithoutChangingPrompt(string prompt)
    {
        var result = Parse(prompt);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal(prompt, result.Model.Scenes[0].EffectiveImagePrompt);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("SCENE_IMAGE_SOURCE_UNRESOLVED"));
        Assert.True(ScenePromptMetadata.IsUsableImagePrompt(prompt));
        Assert.Equal(prompt, ScenePromptMetadata.NormalizeEffectiveImagePrompt(prompt, "fallback"));
        Assert.Equal(prompt, ScenePromptMetadata.NormalizeEditedEffectiveImagePrompt(prompt, "[TODO]", "fallback"));
    }

    [Theory]
    [InlineData("[TODO]")]
    [InlineData("TODO: describe the scene")]
    [InlineData("[todo: describe the scene]")]
    [InlineData("[PLACEHOLDER]")]
    [InlineData("[[image prompt]]")]
    [InlineData("THAY BANG: prompt")]
    [InlineData("THAY B\u1eb0NG: prompt")]
    [InlineData("")]
    public void MarkersAndEmptyPromptsRemainInvalid(string prompt)
    {
        Assert.False(Parse(prompt).IsTodoXSchemaValid);
        Assert.False(ScenePromptMetadata.IsUsableImagePrompt(prompt));
    }

    [Fact]
    public void PlaceholderUsesValidBrandedFallbackButOtherSchemaErrorsRemain()
    {
        const string fallback = "Show TodoX Dashboard on the monitor.";
        var result = Parse("[TODO]", fallback);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal(fallback, result.Model.Scenes[0].EffectiveImagePrompt);
        Assert.Equal(fallback, ScenePromptMetadata.NormalizeEffectiveImagePrompt("[TODO]", fallback));
        Assert.False(Parse("[TODO]", "[PLACEHOLDER]").IsTodoXSchemaValid);
        Assert.False(Parse(fallback, motion: "").IsTodoXSchemaValid);
    }
}
