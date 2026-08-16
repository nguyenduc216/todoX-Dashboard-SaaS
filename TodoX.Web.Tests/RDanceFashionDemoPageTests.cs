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
            "IOptionsMonitor<DanceSellPhase2Options>",
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
    public void PageUsesStyledMp4DropZoneInsteadOfRawMotionInput()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("class=\"@MotionUploadZoneClass\"", page, StringComparison.Ordinal);
        Assert.Contains("rdance-hidden-file-input", page, StringComparison.Ordinal);
        Assert.Contains("OnChange=\"OnMotionSelected\" accept=\"video/mp4\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<InputFile OnChange=\"OnMotionSelected\" accept=\"video/mp4\"", page, StringComparison.Ordinal);
        Assert.Contains("Kéo thả video MP4 vào đây", page, StringComparison.Ordinal);
        Assert.Contains("hoặc bấm để chọn video", page, StringComparison.Ordinal);
        Assert.Contains("MP4 · tối đa @MaxMotionVideoLabel", page, StringComparison.Ordinal);
        Assert.Contains("OnMotionDragEnter", page, StringComparison.Ordinal);
        Assert.Contains("OnMotionDrop", page, StringComparison.Ordinal);
        Assert.Contains("Thay video", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageValidatesMotionUploadBeforeBackendUpload()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("ValidateMotionVideo(file)", page, StringComparison.Ordinal);
        Assert.Contains("file.Size > MaxMotionVideoBytes", page, StringComparison.Ordinal);
        Assert.Contains("file.ContentType.Equals(\"video/mp4\"", page, StringComparison.Ordinal);
        Assert.Contains("Path.GetExtension(file.Name).Equals(\".mp4\"", page, StringComparison.Ordinal);
        Assert.Contains("Chỉ hỗ trợ video MP4.", page, StringComparison.Ordinal);
        Assert.Contains("Video vượt quá dung lượng cho phép.", page, StringComparison.Ordinal);
        Assert.Contains("DanceSell.UploadMotionAsync", page, StringComparison.Ordinal);
        Assert.Contains("OpenReadStream(MaxMotionVideoBytes)", page, StringComparison.Ordinal);

        Assert.True(page.IndexOf("ValidateMotionVideo(file)", StringComparison.Ordinal) < page.IndexOf("DanceSell.UploadMotionAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void PageShowsMotionReadyStateAndContinueGate()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("Video chuyển động đã sẵn sàng", page, StringComparison.Ordinal);
        Assert.Contains("MotionSourceName", page, StringComparison.Ordinal);
        Assert.Contains("_motionFileName", page, StringComparison.Ordinal);
        Assert.Contains("_motionFileSize", page, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(_job?.MotionVideoMediaId is null || _busy)\"", page, StringComparison.Ordinal);
        Assert.Contains("DanceSell.StageTikTokAsync", page, StringComparison.Ordinal);
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
