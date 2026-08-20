using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TodoXVideoPromptParserTests
{
    [Fact]
    public void ParsesCurrentTodoXSchemaAndIgnoresUnknownFields()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "_doc": { "version": "2" },
          "meta": {
            "total_duration_seconds": 24,
            "product_name": "TodoX Demo",
            "kieu_kich_ban": "Product showcase",
            "style": "clean",
            "cta": "Try TodoX"
          },
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "A bright product desk",
              "video_prompt": "Camera pushes in",
              "motion_prompt": "Camera pushes in",
              "voice": "Welcome"
            },
            {
              "scene": 2,
              "duration_seconds": 8,
              "image_prompt": "A user working",
              "video_prompt": "User moves naturally",
              "motion_prompt": "User moves naturally",
              "voice": "Work faster"
            },
            {
              "scene": 3,
              "duration_seconds": 8,
              "image_prompt": "A finished result",
              "video_prompt": "Logo settles",
              "motion_prompt": "Logo settles",
              "voice": "Start today"
            }
          ],
          "qc": { "approved": true },
          "motion_beats": [],
          "image_prompt_fallback": "ignored",
          "tts_rate": 1.0
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXPrompt);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal(3, result.Summary.SceneCount);
        Assert.Equal(24, result.Summary.SceneDurationTotal);
        Assert.Equal(24, result.Summary.DeclaredDurationSeconds);
        Assert.Equal("TodoX Demo", result.Summary.VideoTitle);
    }

    [Fact]
    public void UsesVideoAndVoiceAliasesWithoutMakingJsonInvalid()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "real image",
              "video_prompt": "move camera",
              "voice_text": "narration"
            }
          ]
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Equal("move camera", result.Model.Scenes[0].MotionPrompt);
        Assert.Equal("narration", result.Model.Scenes[0].Voice);
    }

    [Fact]
    public void PlaceholderProducesWarningInsteadOfInvalidJson()
    {
        var result = new TodoXVideoPromptParser().Parse("""
        {
          "scenes": [
            {
              "scene": 1,
              "duration_seconds": 8,
              "image_prompt": "[[THAY BẰNG ẢNH THỰC TẾ]]",
              "motion_prompt": "slow pan"
            }
          ]
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.True(result.IsTodoXSchemaValid);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("image_prompt đang là placeholder", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidJsonWithMissingMetadataIsWarningNotSyntaxError()
    {
        var result = new TodoXVideoPromptParser().Parse("""{"scenes": []}""");

        Assert.True(result.IsJsonValid);
        Assert.False(result.IsTodoXSchemaValid);
        Assert.Null(result.ErrorMessage);
        Assert.Contains(result.Warnings, warning => warning.Contains("Metadata thiếu", StringComparison.Ordinal));
    }
}
