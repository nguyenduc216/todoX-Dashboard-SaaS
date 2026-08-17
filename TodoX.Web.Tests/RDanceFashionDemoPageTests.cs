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
        Assert.Contains("<PageTitle>Video nhảy quảng cáo thời trang</PageTitle>", page, StringComparison.Ordinal);
        Assert.Contains("Đang chuyển tới trang video nhảy quảng cáo thời trang", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/jobs/rdance/new\", replace: true)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudTabs", page, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadMotionAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RDance", page, StringComparison.Ordinal);
        Assert.DoesNotContain("rDance", page, StringComparison.Ordinal);
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
            "Video nhảy quảng cáo thời trang",
            "Tạo video và tiếp tục",
            "Kéo thả video MP4 vào đây",
            "Chỉ hỗ trợ video MP4.",
            "Dịch vụ video nhảy quảng cáo thời trang chưa được cấu hình Motion Control."
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("/rdance-fashion-demo", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RDance", page, StringComparison.Ordinal);
        Assert.DoesNotContain("rDance", page, StringComparison.Ordinal);
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
            "AutoPrepareReferenceAsync",
            "ApproveLatestReferenceAsync",
            "ApproveCharacterAsync",
            "OpenTikTokAsync",
            "ShowMessageBoxAsync",
            "Kling Motion Control",
            "Provider chính: 79AI",
            "Video nhảy quảng cáo thời trang",
            "Dịch vụ: Video nhảy quảng cáo thời trang",
            "Lịch sử video quảng cáo thời trang",
            "Bạn không có quyền xem video này.",
            "Không tìm thấy video quảng cáo thời trang.",
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
        Assert.DoesNotContain("RDance", page, StringComparison.Ordinal);
        Assert.DoesNotContain("rDance", page, StringComparison.Ordinal);
        Assert.Contains("MotionSourceUrl", page, StringComparison.Ordinal);
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
            "Video nhảy quảng cáo thời trang",
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
        Assert.Contains("private async Task OnCharacterSelected(InputFileChangeEventArgs args)", page, StringComparison.Ordinal);
        Assert.Contains("DanceSell.UploadCharacterAsync(_job!.Id, bytes, file.Name, file.ContentType, AuthState.CurrentUser!)", page, StringComparison.Ordinal);
        Assert.Contains("private async Task OnProductSelected(InputFileChangeEventArgs args)", page, StringComparison.Ordinal);
        Assert.Contains("DanceSell.UploadProductAsync(_job!.Id, bytes, file.Name, file.ContentType, AuthState.CurrentUser!)", page, StringComparison.Ordinal);
        Assert.Contains("AutoPrepareReferenceAsync", page, StringComparison.Ordinal);
        Assert.Contains("file.OpenReadStream(maxBytes)", page, StringComparison.Ordinal);
        Assert.Contains("Video vượt quá dung lượng cho phép.", page, StringComparison.Ordinal);
        Assert.Contains("Video chuyển động đã sẵn sàng", page, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageUsesCustomImageUploadZonesAndReferenceCopy()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        foreach (var expected in new[]
        {
            "class=\"@CharacterImageUploadZoneClass\"",
            "class=\"@ProductImageUploadZoneClass\"",
            "Icons.Material.Filled.CloudUpload",
            "Kéo thả ảnh vào đây",
            "hoặc bấm để chọn ảnh",
            "PNG / JPG / JPEG / WEBP · tối đa @MaxImageLabel",
            "Thay ảnh",
            "private const string AcceptedImageTypes = \"image/png,image/jpeg,image/webp\"",
            "private string MaxImageLabel",
            "private bool _characterDragActive",
            "private bool _productDragActive",
            "OnCharacterDragEnter",
            "OnProductDragEnter",
            "Ảnh dùng để tạo video",
            "AI sẽ kết hợp ảnh người mẫu và ảnh sản phẩm để tạo ảnh dùng cho video.",
            "Ảnh người mẫu sẽ được dùng trực tiếp để tạo video.",
            "Tạo lại ảnh",
            "Duyệt ảnh",
            "Đã duyệt",
            "Nguồn chuyển động: TikTok",
            "Link gốc:",
            "Mở TikTok",
            "HasTikTokSource",
            "_tiktokUrl = _job.MotionSourceUrl",
            "await AutoPrepareReferenceAsync()"
        })
        {
            Assert.Contains(expected, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("<MudText Typo=\"Typo.h6\">Ảnh tham chiếu</MudText>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<InputFile OnChange=\"OnCharacterSelected\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<InputFile OnChange=\"OnProductSelected\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Tạo ảnh AI", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Dùng ảnh người mẫu", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderCatalogRouteQueryUsesProductionSchema()
    {
        var root = FindRepoRoot();
        var source = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellAiOperations.cs"));
        var queryStart = source.IndexOf("SELECT id AS Id, feature_code AS FeatureCode", StringComparison.Ordinal);
        Assert.True(queryStart >= 0, "Expected DanceSell provider route SELECT query.");
        var queryEnd = source.IndexOf("ORDER BY is_default DESC", queryStart, StringComparison.Ordinal);
        Assert.True(queryEnd > queryStart, "Expected provider route ORDER BY clause.");
        var query = source[queryStart..source.IndexOf(';', queryEnd)];

        Assert.Contains("provider_code AS ProviderCode", query, StringComparison.Ordinal);
        Assert.Contains("model_name AS ModelName", query, StringComparison.Ordinal);
        Assert.Contains("model_mode AS ModelMode", query, StringComparison.Ordinal);
        Assert.Contains("route_priority AS Priority", query, StringComparison.Ordinal);
        Assert.Contains("fallback_on AS FallbackOn", query, StringComparison.Ordinal);
        Assert.Contains("ORDER BY is_default DESC, route_priority, provider_code, model_name", query, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_capability_id", query, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_account_id", query, StringComparison.Ordinal);
        Assert.DoesNotContain("allow_user_select", query, StringComparison.Ordinal);
        Assert.DoesNotContain(" priority AS Priority", query, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageAllowsEditingWhenProviderRoutesAreUnavailable()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        var upload = GetMethodSection(page, "UploadAsync");
        var stageTikTok = GetMethodSection(page, "StageTikTokAsync");
        var autoPrepare = GetMethodSection(page, "AutoPrepareReferenceAsync");
        var queueRender = GetMethodSection(page, "ConfirmAndQueueAsync");

        Assert.Contains("_referenceReadinessError", page, StringComparison.Ordinal);
        Assert.Contains("_motionReadinessError", page, StringComparison.Ordinal);
        Assert.Contains("await EnsureEditableAsync()", upload, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureReferenceProviderReady", upload, StringComparison.Ordinal);
        Assert.Contains("await EnsureEditableAsync()", stageTikTok, StringComparison.Ordinal);
        Assert.Contains("EnsureReferenceProviderReady()", autoPrepare, StringComparison.Ordinal);
        Assert.Contains("EnsureMotionProviderReady()", queueRender, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageSupportsUnapproveAndRegeneratesAfterImageChanges()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        Assert.Contains("OnClick=\"UnapproveReferenceAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("References.UnapproveAsync(_job!.Id, AuthState.CurrentUser!)", page, StringComparison.Ordinal);
        Assert.Contains("if (await UploadAsync(args, MaxImageBytes", GetMethodSection(page, "OnCharacterSelected"), StringComparison.Ordinal);
        Assert.Contains("await AutoPrepareReferenceAsync()", GetMethodSection(page, "OnCharacterSelected"), StringComparison.Ordinal);
        Assert.Contains("if (await UploadAsync(args, MaxImageBytes", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
        Assert.Contains("await AutoPrepareReferenceAsync()", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSeedMakes79AiPrimaryAndKieBackup()
    {
        var root = FindRepoRoot();
        var sql = ReadStrictUtf8(Path.Combine(root, "database", "manual", "rdance-fashion", "01_seed_79ai_kling_motion_routes.sql"));

        Assert.Contains("'79ai'", sql, StringComparison.Ordinal);
        Assert.Contains("'seedream_5_0'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling_video_motion'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling-2.6/motion-control'", sql, StringComparison.Ordinal);
        Assert.Contains("provider_code = 'local_composite'", sql, StringComparison.Ordinal);
        Assert.Contains("enabled = false", sql, StringComparison.Ordinal);
        Assert.Contains("\"motion_video_field\":\"video\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"character_image_field\":\"base64Image\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"product_image_field\":\"image_2\"", sql, StringComparison.Ordinal);
        Assert.Contains("is_default = false", sql, StringComparison.Ordinal);
        Assert.Contains("model_mode", sql, StringComparison.Ordinal);
        Assert.Contains("route_priority", sql, StringComparison.Ordinal);
        Assert.Contains("fallback_on", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_capability_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_account_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allow_user_select", sql, StringComparison.Ordinal);
        Assert.Contains("Reference generation now uses the production 79AI image route for seedream_5_0.", sql, StringComparison.Ordinal);
    }

    private static string ReadStrictUtf8(string file)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));

    private static string GetMethodSection(string source, string methodName)
    {
        var start = source.IndexOf($"private async Task {methodName}(", StringComparison.Ordinal);
        if (start < 0)
        {
            start = source.IndexOf($"private async Task<bool> {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(start >= 0, $"Could not locate {methodName}.");
        var nextMethod = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        return nextMethod > start ? source[start..nextMethod] : source[start..];
    }

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
