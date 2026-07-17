using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class TodoXVideoPromptParserTests
{
    [Fact]
    public void Parse_ReadsSupportedAspectRatio()
    {
        var parser = new TodoXVideoPromptParser();

        var result = parser.Parse("""
        {
          "aspect_ratio": "16:9",
          "video_title": "Demo",
          "scenes": []
        }
        """);

        Assert.True(result.IsJsonValid);
        Assert.Equal("16:9", result.Model.AspectRatio);
        Assert.Equal("16:9", result.Summary.AspectRatio);
        Assert.True(result.IsTodoXPrompt);
    }

    [Fact]
    public void Parse_ReadsPortraitAspectRatio()
    {
        var parser = new TodoXVideoPromptParser();

        var result = parser.Parse("""{"aspectRatio":"9:16","video_title":"Demo"}""");

        Assert.True(result.IsJsonValid);
        Assert.Equal("9:16", result.Model.AspectRatio);
        Assert.False(result.HasInvalidAspectRatio);
    }

    [Fact]
    public void Parse_ReadsAliasAspectRatioKeys()
    {
        var parser = new TodoXVideoPromptParser();

        var videoAspect = parser.Parse("""{"video_aspect_ratio":"16:9","video_title":"Demo"}""");
        var ratio = parser.Parse("""{"ratio":"9:16","video_title":"Demo"}""");

        Assert.Equal("16:9", videoAspect.Model.AspectRatio);
        Assert.Equal("9:16", ratio.Model.AspectRatio);
    }

    [Fact]
    public void Parse_AllowsMissingAspectRatio()
    {
        var parser = new TodoXVideoPromptParser();

        var result = parser.Parse("""{"video_title":"Demo"}""");

        Assert.True(result.IsJsonValid);
        Assert.Null(result.Model.AspectRatio);
        Assert.False(result.HasInvalidAspectRatio);
    }

    [Fact]
    public void Parse_FlagsUnsupportedAspectRatio()
    {
        var parser = new TodoXVideoPromptParser();

        var result = parser.Parse("""{"aspect_ratio":"1:1","video_title":"Demo"}""");

        Assert.True(result.IsJsonValid);
        Assert.Null(result.Model.AspectRatio);
        Assert.Null(result.Summary.AspectRatio);
        Assert.True(result.HasInvalidAspectRatio);
        Assert.Equal("1:1", result.InvalidAspectRatio);
        Assert.Contains("16:9", result.ErrorMessage);
    }
}
