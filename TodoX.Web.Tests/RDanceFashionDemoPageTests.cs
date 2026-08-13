using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceFashionDemoPageTests
{
    [Fact]
    public void PageHasTheTwoTabMockFlowAndLocalResources()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        foreach (var expected in new[]
        {
            "@page \"/rdance-fashion-demo\"",
            "MudTabPanel Text=\"Thông tin\"",
            "MudTabPanel Text=\"Kết quả\"",
            "https://www.tiktok.com/@kh.nh.n23/video/7666103921814850837",
            "/resources/mockup/rdance-fashion/rdance-fashion-source.mp4",
            "/resources/mockup/rdance-fashion/rdance-fashion-character.jpg",
            "/resources/mockup/rdance-fashion/rdance-fashion-result.mp4",
            "rdance-fashion-source-frame",
            "<source src=\"@SourceVideoUrl\" type=\"video/mp4\" />",
            "MudProgressCircular",
            "MudProgressLinear",
            "await Task.Delay(500);",
            "Tải video"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("TikTok API", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MudTabPanel Text=\"Scene", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudTabPanel Text=\"Video\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageIsAuthenticatedAndRegisteredInNavigation()
    {
        var repoRoot = FindRepoRoot();
        var page = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));
        var layout = ReadStrictUtf8(Path.Combine(repoRoot, "TodoX.Web", "Components", "Layout", "MainLayout.razor"));

        Assert.Contains("Vui lòng đăng nhập để mở trang này.", page, StringComparison.Ordinal);
        Assert.Contains("rDance Thời Trang", layout, StringComparison.Ordinal);
        Assert.Contains("/rdance-fashion-demo", layout, StringComparison.Ordinal);
        Assert.Contains("Checkroom", layout, StringComparison.Ordinal);
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
