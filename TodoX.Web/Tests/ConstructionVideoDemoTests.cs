using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ConstructionVideoDemoTests
{
    [Fact]
    public void ConstructionDemoPage_ExposesSequentialImageRenderFlow()
    {
        var page = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "ConstructionVideoDemo.razor"),
            Encoding.UTF8);

        Assert.Contains("@page \"/construction-video-demo\"", page);
        Assert.Contains("Text=\"Render ảnh\"", page);
        Assert.Contains("TẠO ẢNH TIẾN ĐỘ", page);
        Assert.Contains("TẠO VIDEO", page);
        Assert.Contains("private int _imageCompletedCount;", page);
        Assert.Contains("Task.Delay(2500)", page);
        Assert.Contains("Giai đoạn 1 – Khởi công", page);
        Assert.Contains("Giai đoạn 4 – Công trình hoàn thiện", page);
        Assert.Contains("MockupBasePath = \"/resources/mockup/construction-video\"", page);
        Assert.Contains("construction-video-input.jpg", page);
        Assert.Contains("construction-video-scene-04.mp4", page);
        Assert.DoesNotContain("Scene 05", page);
    }
}
