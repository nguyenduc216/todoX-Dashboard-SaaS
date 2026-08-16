using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceFashionDemoPageTests
{
    [Fact]
    public void LegacyDemoPageRedirectsToProductionCreateRoute()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceFashionDemo.razor"));

        Assert.Contains("@page \"/rdance-fashion-demo\"", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/jobs/rdance/new\", replace: true)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudTabs", page, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadMotionAsync", page, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceCreatePageUsesProductionCreateFlow()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobCreate.razor"));

        foreach (var expected in new[]
        {
            "@page \"/jobs/rdance/new\"",
            "[SupplyParameterFromQuery] public Guid? ServiceId",
            "[SupplyParameterFromQuery] public string? ServiceCode",
            "IDanceSellPhase2Service",
            "CreateJobAsync",
            "CreateDraftAndOpenAsync",
            "StageTikTokAndOpenAsync",
            "OnMotionSelected",
            "Navigation.NavigateTo($\"/jobs/rdance/{job.Id}\")",
            "Tạo draft và tiếp tục",
            "Kéo thả video MP4 vào đây",
            "Chỉ hỗ trợ video MP4.",
            "RDance chưa được cấu hình provider Motion Control."
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("/rdance-fashion-demo", page, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageUsesJobRouteAndKeepsWorkflowTabs()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        foreach (var expected in new[]
        {
            "@page \"/jobs/rdance/{JobId:guid}\"",
            "[Parameter] public Guid JobId",
            "MudTabPanel Text=\"Thông tin\"",
            "MudTabPanel Text=\"Hình ảnh\"",
            "MudTabPanel Text=\"Video\"",
            "MudTabPanel Text=\"Kết quả\"",
            "IDanceSellPhase2Service",
            "IDanceSellReferenceImageService",
            "IDanceSellProviderCatalog",
            "IOptionsMonitor<DanceSellPhase2Options>",
            "DanceSell.GetAsync(JobId, AuthState.CurrentUser)",
            "StageTikTokAsync",
            "GenerateReferenceAsync",
            "ApproveLatestReferenceAsync",
            "ApproveCharacterAsync",
            "ShowMessageBoxAsync",
            "Kling Motion Control",
            "Provider chính: 79AI",
            "Bạn không có quyền xem job RDance này.",
            "CancelAsync",
            "RetryAsync"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("DEMO", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(500)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/rdance-fashion-demo", page, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay", page, StringComparison.Ordinal);
        Assert.Contains("NavigateTo(\"/jobs/rdance/new\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void MyJobsIncludesRDanceJobsAndRoutesToDetail()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "MyJobs.razor"));

        foreach (var expected in new[]
        {
            "IDanceSellPhase2Service DanceJobs",
            "DanceJobs.ListAsync(currentUser, 100)",
            "rDance Thời Trang",
            "$\"/jobs/rdance/{x.Id}\"",
            "Navigation.NavigateTo(context.Route)"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RDanceDetailPageKeepsMotionDropZoneAndValidation()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        Assert.Contains("class=\"@MotionUploadZoneClass\"", page, StringComparison.Ordinal);
        Assert.Contains("rdance-hidden-file-input", page, StringComparison.Ordinal);
        Assert.Contains("ValidateMotionVideo(file)", page, StringComparison.Ordinal);
        Assert.Contains("private long MaxImageBytes", page, StringComparison.Ordinal);
        Assert.Contains("UploadAsync(args, MaxMotionVideoBytes", page, StringComparison.Ordinal);
        Assert.Contains("OnCharacterSelected(InputFileChangeEventArgs args) => UploadAsync(args, MaxImageBytes", page, StringComparison.Ordinal);
        Assert.Contains("OnProductSelected(InputFileChangeEventArgs args) => UploadAsync(args, MaxImageBytes", page, StringComparison.Ordinal);
        Assert.Contains("file.OpenReadStream(maxBytes)", page, StringComparison.Ordinal);
        Assert.Contains("Video vượt quá dung lượng cho phép.", page, StringComparison.Ordinal);
        Assert.Contains("Video chuyển động đã sẵn sàng", page, StringComparison.Ordinal);
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
