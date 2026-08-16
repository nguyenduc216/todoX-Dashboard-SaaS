using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RealEstateVideoDemoPageTests
{
    [Fact]
    public void PageClonesHealthVideoFourTabFlowWithRealEstateResources()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RealEstateVideoDemo.razor"));

        foreach (var expected in new[]
        {
            "@page \"/real-estate-video-demo\"",
            "Video Bất Động Sản",
            "Tạo video giới thiệu bất động sản chuyên nghiệp bằng AI",
            "MudTabPanel Text=\"Thông tin\"",
            "MudTabPanel Text=\"Scene / Hình ảnh\"",
            "MudTabPanel Text=\"Video\"",
            "MudTabPanel Text=\"Kết quả\"",
            "Ảnh bất động sản",
            "real-estate-video-input-01.jpg",
            "real-estate-video-prompt.json",
            "/resources/mockup/real-estate-video/real-estate-video-music.mp3",
            "/resources/mockup/real-estate-video/real-estate-video-voice-03.mp3",
            "/resources/mockup/real-estate-video/real-estate-video-scene-01.jpg",
            "/resources/mockup/real-estate-video/real-estate-video-scene-05.mp4",
            "/resources/mockup/real-estate-video/real-estate-video-result.mp4",
            "5 Scene · 30 giây · 9:16 · 1080x1920",
            "Chưa có video kết quả"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("health-video-", page, StringComparison.Ordinal);
        Assert.DoesNotContain("sức khoẻ", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PageKeepsCompactGridAndMockLoading()
    {
        var repoRoot = FindRepoRoot();
        var page = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "RealEstateVideoDemo.razor"));
        var css = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "RealEstateVideoDemo.razor.css"));

        Assert.Contains("<MudItem xs=\"12\" sm=\"6\" md=\"3\">", page, StringComparison.Ordinal);
        Assert.Contains("real-estate-video-compact-grid", page, StringComparison.Ordinal);
        Assert.Contains("real-estate-video-video-frame", page, StringComparison.Ordinal);
        Assert.Contains("RunMockProgressAsync", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(450);", page, StringComparison.Ordinal);
        Assert.Contains("Đang phân tích hình ảnh bất động sản...", page, StringComparison.Ordinal);
        Assert.Contains("Đang dựng chuyển động cho bất động sản...", page, StringComparison.Ordinal);
        Assert.Contains("Đang hoàn thiện video bất động sản...", page, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 9 / 16;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void PageIsNotRegisteredInProductionNavigation()
    {
        var layout = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Layout", "MainLayout.razor"));

        Assert.DoesNotContain("BuildRealEstateVideoDemoItem", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("real_estate_video_demo", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Video Bất Động Sản", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("/real-estate-video-demo", layout, StringComparison.Ordinal);
    }

    private static string ReadStrictUtf8(string file)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
