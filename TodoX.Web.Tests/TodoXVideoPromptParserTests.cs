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
    public void Parse_IgnoresUnsupportedAspectRatio()
    {
        var parser = new TodoXVideoPromptParser();

        var result = parser.Parse("""{"aspect_ratio":"1:1","video_title":"Demo"}""");

        Assert.True(result.IsJsonValid);
        Assert.Null(result.Model.AspectRatio);
        Assert.Null(result.Summary.AspectRatio);
    }
}
