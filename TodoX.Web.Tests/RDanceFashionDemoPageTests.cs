using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceFashionDemoPageTests
{
    [Fact]
    public void PageUsesTheFourTabProductionFlow()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        foreach (var expected in new[]
        {
            "@page \"/rdance-fashion-demo\"",
            "MudTabPanel Text=\"Thông tin\"",
            "MudTabPanel Text=\"Hình ảnh\"",
            "MudTabPanel Text=\"Video\"",
            "MudTabPanel Text=\"Kết quả\"",
            "IDanceSellPhase2Service",
            "IDanceSellReferenceImageService",
            "IDanceSellProviderCatalog",
            "InputFile",
            "StageTikTokAsync",
            "GenerateReferenceAsync",
            "ApproveLatestReferenceAsync",
            "ApproveCharacterAsync",
            "ShowMessageBoxAsync",
            "Kling Motion Control",
            "Provider chính: 79AI",
            "CancelAsync",
            "RetryAsync"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("DEMO", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(500)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/resources/mockup/rdance-fashion", page, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageShowsProviderReadinessGuardWithoutHidingCatalogRoute()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("RDance chưa được cấu hình provider Motion Control.", page, StringComparison.Ordinal);
        Assert.Contains("ProviderCatalog.GetDefaultRouteAsync(DanceSellOperationTypes.MotionVideo)", page, StringComparison.Ordinal);
        Assert.Contains("_readinessError", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Dịch vụ RDance đang hoàn thiện.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageKeepsReferenceApprovalGateAndNoProviderSecrets()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("PreparedReferenceStatus == DanceSellReferenceStatuses.Approved", page, StringComparison.Ordinal);
        Assert.Contains("DanceSell.QueueRenderAsync", page, StringComparison.Ordinal);
        Assert.Contains("ShowMessageBoxAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RouteSeedMakes79AiPrimaryAndKieBackup()
    {
        var root = FindRepoRoot();
        var sql = ReadStrictUtf8(Path.Combine(root, "database", "manual", "rdance-fashion", "01_seed_79ai_kling_motion_routes.sql"));

        Assert.Contains("'79ai'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling_video_motion'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling-2.6/motion-control'", sql, StringComparison.Ordinal);
        Assert.Contains("'local_composite'", sql, StringComparison.Ordinal);
        Assert.Contains("\"motion_video_field\":\"video\"", sql, StringComparison.Ordinal);
        Assert.Contains("is_default = false", sql, StringComparison.Ordinal);
        Assert.Contains("No verified 79AI image-edit model", sql, StringComparison.Ordinal);
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
