using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ConstructionVideoDemoTests
{
    [Fact]
    public void ConstructionDemoPage_ExposesExpectedRouteAndIndustryCopy()
    {
        var page = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "ConstructionVideoDemo.razor"),
            Encoding.UTF8);

        Assert.Contains("@page \"/construction-video-demo\"", page);
        Assert.Contains("Video Xây Dựng", page);
        Assert.Contains("Xây dựng &amp; Công trình", page);
        Assert.Contains("MockupBasePath = \"/resources/mockup/construction-video\"", page);
        Assert.Contains("construction-video-input.jpg", page);
        Assert.Contains("construction-video-scene-04.mp4", page);
    }
}
