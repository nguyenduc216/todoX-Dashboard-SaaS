using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class HealthVideoDemoPageTests
{
    [Fact]
    public void PageIsAdminOnlyAndRegisteredInNavigation()
    {
        var repoRoot = FindRepoRoot();
        var page = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "HealthVideoDemo.razor"));
        var layout = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Layout", "MainLayout.razor"));

        Assert.Contains("@page \"/health-video-demo\"", page, StringComparison.Ordinal);
        Assert.Contains("Bạn cần quyền quản trị để mở trang này.", page, StringComparison.Ordinal);
        Assert.Contains("private bool IsAdmin => AuthState.CurrentUser?.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator || AuthState.CurrentUser?.IsRoot == true;", page, StringComparison.Ordinal);
        Assert.Contains("Video Sức Khoẻ", layout, StringComparison.Ordinal);
        Assert.Contains("/health-video-demo", layout, StringComparison.Ordinal);
        Assert.Contains("HealthAndSafety", layout, StringComparison.Ordinal);
        Assert.Contains("BuildHealthVideoDemoItem", layout, StringComparison.Ordinal);
        Assert.Contains("VisibilityPolicy = \"admin_avatar_manager\"", layout, StringComparison.Ordinal);
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
