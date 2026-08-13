using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class HealthVideoDemoPageTests
{
    [Fact]
    public void PageIsAuthenticatedAndRegisteredInNavigation()
    {
        var repoRoot = FindRepoRoot();
        var page = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "HealthVideoDemo.razor"));
        var layout = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Layout", "MainLayout.razor"));

        Assert.Contains("@page \"/health-video-demo\"", page, StringComparison.Ordinal);
        Assert.Contains("Vui lòng đăng nhập để mở trang này.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool IsAdmin =>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Bạn cần quyền quản trị để mở trang này.", page, StringComparison.Ordinal);
        Assert.Contains("Video Sức Khoẻ", layout, StringComparison.Ordinal);
        Assert.Contains("/health-video-demo", layout, StringComparison.Ordinal);
        Assert.Contains("HealthAndSafety", layout, StringComparison.Ordinal);
        Assert.Contains("BuildHealthVideoDemoItem", layout, StringComparison.Ordinal);
        Assert.Contains("VisibilityPolicy = \"always\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void PageUsesMockResourcesAndFourTabFlow()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "HealthVideoDemo.razor"));

        foreach (var expected in new[]
        {
            "MudTabPanel Text=\"Thông tin\"",
            "MudTabPanel Text=\"Scene / Hình ảnh\"",
            "MudTabPanel Text=\"Video\"",
            "MudTabPanel Text=\"Kết quả\"",
            "/resources/mockup/health-video/health-video-input-01.jpg",
            "health-video-prompt.json",
            "/resources/mockup/health-video/health-video-music.mp3",
            "/resources/mockup/health-video/health-video-scene-01.jpg",
            "/resources/mockup/health-video/health-video-scene-06.mp4",
            "/resources/mockup/health-video/health-video-result.mp4",
            "Tạo video",
            "Hoàn thiện video",
            "Tải video"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConfigurationIsFixedAndAudioPreviewsStayInPage()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "HealthVideoDemo.razor"));

        Assert.Contains("Số scene", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@SceneCountText\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@DurationText\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@AspectRatioText\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@ResolutionText\"", page, StringComparison.Ordinal);
        Assert.Contains("private const string SceneCountText = \"6\";", page, StringComparison.Ordinal);
        Assert.Contains("private const string DurationText = \"36 giây\";", page, StringComparison.Ordinal);
        Assert.Contains("private const string AspectRatioText = \"9:16\";", page, StringComparison.Ordinal);
        Assert.Contains("private const string ResolutionText = \"1080 × 1920\";", page, StringComparison.Ordinal);
        Assert.Contains("<audio controls preload=\"metadata\" src=\"@SelectedVoiceAudioUrl\"", page, StringComparison.Ordinal);
        Assert.Contains("<audio controls preload=\"metadata\" src=\"@MusicAudioUrl\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneCountOptions", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DurationOptions", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AspectRatioOptions", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayVoiceAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayMusicAsync", page, StringComparison.Ordinal);
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
