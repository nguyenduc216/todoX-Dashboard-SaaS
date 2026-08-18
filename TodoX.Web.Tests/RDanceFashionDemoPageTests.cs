using System.Text;
using TodoX.Web.Services.DanceSell;
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
        Assert.DoesNotContain("_history", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Lịch sử video quảng cáo thời trang", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DanceSell.ListAsync(AuthState.CurrentUser", page, StringComparison.Ordinal);
        Assert.Contains("IRenderJobService RenderJobs", page, StringComparison.Ordinal);
        Assert.Contains("RenderJobs.GetAsync(renderJobId)", page, StringComparison.Ordinal);
        Assert.Contains("IsCoreCancelled", page, StringComparison.Ordinal);
        Assert.Contains("MudItem xs=\"12\" md=\"8\"", page, StringComparison.Ordinal);
        Assert.Contains("MudItem xs=\"12\" md=\"4\"", page, StringComparison.Ordinal);
        Assert.Contains("DisplayStatusLabel(_job!)", page, StringComparison.Ordinal);
        Assert.Contains("Đã dừng tạo video.", page, StringComparison.Ordinal);
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
            "AI sẽ tạo ảnh người mẫu mặc sản phẩm để dùng làm ảnh nguồn cho video.",
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
        Assert.DoesNotContain("AI sẽ kết hợp ảnh người mẫu và ảnh sản phẩm", page, StringComparison.Ordinal);
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
        Assert.Contains("await ReloadAsync()", autoPrepare, StringComparison.Ordinal);
        Assert.Contains("_job.ProductMediaId is not null", autoPrepare, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageSupportsUnapproveAndRegeneratesAfterImageChanges()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        Assert.Contains("OnClick=\"UnapproveReferenceAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("References.UnapproveAsync(_job!.Id, AuthState.CurrentUser!)", page, StringComparison.Ordinal);
        Assert.Contains("if (await UploadAsync(args, MaxImageBytes", GetMethodSection(page, "OnCharacterSelected"), StringComparison.Ordinal);
        Assert.Contains("await ReloadAsync()", GetMethodSection(page, "OnCharacterSelected"), StringComparison.Ordinal);
        Assert.Contains("await AutoPrepareReferenceAsync()", GetMethodSection(page, "OnCharacterSelected"), StringComparison.Ordinal);
        Assert.Contains("if (await UploadAsync(args, MaxImageBytes", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
        Assert.Contains("await ReloadAsync()", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
        Assert.Contains("_job?.ProductMediaId is not null", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(_job.ProductImageUrl)", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
        Assert.Contains("await AutoPrepareReferenceAsync()", GetMethodSection(page, "OnProductSelected"), StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceDetailPageReReadsReferenceBeforeQueueAndMapsLifecycleErrors()
    {
        var page = ReadStrictUtf8(Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));
        var queue = GetMethodSection(page, "ConfirmAndQueueAsync");
        var errors = page;

        Assert.Contains("await UpdateBusinessAsync()", queue, StringComparison.Ordinal);
        Assert.Contains("_job = await DanceSell.GetAsync(_job!.Id, AuthState.CurrentUser!)", queue, StringComparison.Ordinal);
        Assert.Contains("_job.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved", queue, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(_job.PreparedReferenceUrl)", queue, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_REFERENCE_NOT_APPROVED", queue, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_INVALID_PRODUCT", errors, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_INVALID_CHARACTER", errors, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_REFERENCE_NOT_APPROVED", errors, StringComparison.Ordinal);
        Assert.Contains("Ảnh dùng để tạo video đã thay đổi", errors, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(_job.PreparedReferenceUrl)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSeedMakes79AiPrimaryAndKieBackup()
    {
        var root = FindRepoRoot();
        var sql = ReadStrictUtf8(Path.Combine(root, "database", "manual", "rdance-fashion", "01_seed_79ai_kling_motion_routes.sql"));
        var motionSql = ReadStrictUtf8(Path.Combine(root, "database", "manual", "dance-sell-motion", "03_switch_79ai_motion_to_upload_url_flow.sql"));

        Assert.Contains("'79ai'", sql, StringComparison.Ordinal);
        Assert.Contains("'imagegen_2_0'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling_video_motion'", sql, StringComparison.Ordinal);
        Assert.Contains("'kling-2.6/motion-control'", sql, StringComparison.Ordinal);
        Assert.Contains("provider_code = 'local_composite'", sql, StringComparison.Ordinal);
        Assert.Contains("enabled = false", sql, StringComparison.Ordinal);
        Assert.Contains("\"motion_video_field\":\"motion_video\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"reference_image_field\":\"character_image\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"standard\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ratio\":\"default\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"subject_schema\":\"form_subject_url_fields\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"domain\":\"79ai.net\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"sync\":\"false\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"FASHION\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"resolution\":\"1k\"", sql, StringComparison.Ordinal);
        Assert.Contains("is_default = false", sql, StringComparison.Ordinal);
        Assert.Contains("model_mode", sql, StringComparison.Ordinal);
        Assert.Contains("route_priority", sql, StringComparison.Ordinal);
        Assert.Contains("fallback_on", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_capability_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_account_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allow_user_select", sql, StringComparison.Ordinal);
        Assert.Contains("Reference generation now uses the verified 79AI GPT Image 2 fashion route.", sql, StringComparison.Ordinal);
        Assert.Contains("'kling_video_motion_3'", motionSql, StringComparison.Ordinal);
        Assert.Contains("'kling_video_motion'", motionSql, StringComparison.Ordinal);
        Assert.Contains("enabled = false", motionSql, StringComparison.Ordinal);
        Assert.Contains("\"upload_image_path\":\"/ai/upload/image\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"upload_video_path\":\"/ai/upload/video\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"motion_submit_path\":\"/ai/jobs/video/kling_video_motion_3\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"poll_path\":\"/ai/jobs/{task_id}?media=video\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"poll_id_field\":\"id_base\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"upload_video_field\":\"video_file\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"subType\":\"motion\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("\"background_source\":\"input_video\"", motionSql.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void DanceSell79AiMotionSubmitUsesRouteFieldsAndProviderMode()
    {
        var root = FindRepoRoot();
        var handler = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellRenderHandler.cs"));
        var client = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "AiProviders", "Ai79TaskClient.cs"));
        var submit = GetMethodSection(handler, "Submit79AiAsync");
        var runtime = GetMethodSection(handler, "Resolve79AiRuntimeAsync");
        var models = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellModels.cs"));
        var motionSubmit = GetMethodSection(client, "SubmitMotionControlAsync");

        Assert.DoesNotContain("?? danceJob.CharacterImageUrl", submit, StringComparison.Ordinal);
        Assert.Contains("danceJob.PreparedReferenceMediaId", submit, StringComparison.Ordinal);
        Assert.Contains("danceJob.PreparedReferenceObjectKey", submit, StringComparison.Ordinal);
        Assert.Contains("danceJob.PreparedReferenceUrl", submit, StringComparison.Ordinal);
        Assert.Contains("TryUseApprovedReferenceUrl", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_REFERENCE_USING_APPROVED_URL", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_REFERENCE_UPLOAD_SKIPPED", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_REFERENCE_UPLOADED", submit, StringComparison.Ordinal);
        Assert.Contains("UploadMediaAsync(new Ai79MediaUploadRequest", submit, StringComparison.Ordinal);
        Assert.Contains("runtime.UploadVideoPath", submit, StringComparison.Ordinal);
        Assert.Contains("DanceSellAssetRoles.MotionProviderUpload", submit, StringComparison.Ordinal);
        Assert.Contains("GetLatestAssetAsync", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SOURCE_UPLOAD_REUSED", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SOURCE_UPLOADED", submit, StringComparison.Ordinal);
        Assert.Contains("motionProviderUrl", submit, StringComparison.Ordinal);
        Assert.Contains("new Ai79MotionControlSubmitRequest", submit, StringComparison.Ordinal);
        Assert.Contains("SubmitMotionControlAsync(request, ct)", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_STARTED", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_TIMEOUT", submit, StringComparison.Ordinal);
        Assert.Contains("BeginMotionSubmitAttemptAsync", submit, StringComparison.Ordinal);
        Assert.Contains("SubmitMaxRetry", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_RETRY_EXHAUSTED", submit, StringComparison.Ordinal);
        Assert.Contains("contentType = \"application/x-www-form-urlencoded\"", submit, StringComparison.Ordinal);
        Assert.Contains("referenceSource", submit, StringComparison.Ordinal);
        Assert.Contains("referenceUrlUsed", submit, StringComparison.Ordinal);
        Assert.Contains("motionSource", submit, StringComparison.Ordinal);
        Assert.Contains("reference = new", submit, StringComparison.Ordinal);
        Assert.Contains("url = referenceUrlUsed", submit, StringComparison.Ordinal);
        Assert.Contains("submitEndpointPath", submit, StringComparison.Ordinal);
        Assert.Contains("providerModel", submit, StringComparison.Ordinal);
        Assert.Contains("imageUrl = referenceUrlUsed", submit, StringComparison.Ordinal);
        Assert.Contains("images0Url = runtime.IncludeImagesZeroUrl ? referenceUrlUsed : null", submit, StringComparison.Ordinal);
        Assert.Contains("videoUrl = motionProviderUrl", submit, StringComparison.Ordinal);
        Assert.True(
            submit.IndexOf("TryUseApprovedReferenceUrl", StringComparison.Ordinal)
            < submit.IndexOf("UploadMediaAsync(new Ai79MediaUploadRequest", StringComparison.Ordinal),
            "Approved public reference URL must be considered before any 79AI media upload.");
        Assert.True(
            submit.IndexOf("UpsertAssetAsync(new AiOperationAssetDto", StringComparison.Ordinal)
            < submit.IndexOf("SubmitMotionControlAsync(request, ct)", StringComparison.Ordinal),
            "Provider-uploaded motion video URL must be persisted before provider submit can timeout.");
        Assert.DoesNotContain("imageUrl = referenceUpload.Url", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("images0Url = runtime.IncludeImagesZeroUrl ? referenceUpload.Url : null", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("new Ai79MultipartTaskSubmitRequest", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitMultipartAsync(request, ct)", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[referenceImageUrl]", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[runtime.MotionVideoField] = motionVideoUrl", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"mode\"] = danceJob.Mode", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"ratio\"] = \"9:16\"", submit, StringComparison.Ordinal);
        Assert.Contains("mode = runtime.ProviderMode", submit, StringComparison.Ordinal);
        Assert.Contains("ratio = runtime.ProviderRatio", submit, StringComparison.Ordinal);
        Assert.Contains("subType = runtime.SubType", submit, StringComparison.Ordinal);
        Assert.Contains("backgroundSource = runtime.BackgroundSource", submit, StringComparison.Ordinal);

        Assert.Contains("DanceSellMotionProviderContract.ResolveProviderMode(route, job.Mode)", runtime, StringComparison.Ordinal);
        Assert.Contains("DanceSellMotionProviderContract.ResolveProviderRatio(route)", runtime, StringComparison.Ordinal);
        Assert.Contains("DanceSellMotionProviderContract.ResolveReferenceImageField(route)", runtime, StringComparison.Ordinal);
        Assert.Contains("DanceSellMotionProviderContract.ResolveMotionVideoField(route)", runtime, StringComparison.Ordinal);
        Assert.Contains("\"upload_image_path\"", runtime, StringComparison.Ordinal);
        Assert.Contains("\"upload_video_path\"", runtime, StringComparison.Ordinal);
        Assert.Contains("\"motion_submit_path\"", runtime, StringComparison.Ordinal);
        Assert.Contains("\"poll_id_field\"", runtime, StringComparison.Ordinal);
        Assert.Contains("\"subType\"", runtime, StringComparison.Ordinal);
        Assert.Contains("\"background_source\"", runtime, StringComparison.Ordinal);
        Assert.Contains("route.ConfigJson", runtime, StringComparison.Ordinal);

        Assert.Contains("\"720p\" => \"standard\"", models, StringComparison.Ordinal);
        Assert.Contains("\"1080p\" => \"professional\"", models, StringComparison.Ordinal);
        Assert.Contains("DefaultProviderRatio = \"default\"", models, StringComparison.Ordinal);
        Assert.Contains("DefaultReferenceImageField = \"character_image\"", models, StringComparison.Ordinal);
        Assert.Contains("DefaultMotionVideoField = \"motion_video\"", models, StringComparison.Ordinal);

        Assert.Contains("Task<Ai79TaskSubmitResult> SubmitMultipartAsync", client, StringComparison.Ordinal);
        Assert.Contains("Task<Ai79MediaUploadResult> UploadMediaAsync", client, StringComparison.Ordinal);
        Assert.Contains("Task<Ai79TaskSubmitResult> SubmitMotionControlAsync", client, StringComparison.Ordinal);
        Assert.Contains("DefaultMotionControlSubmitTimeout = TimeSpan.FromSeconds(120)", client, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(ct)", client, StringComparison.Ordinal);
        Assert.Contains("timeoutCts.CancelAfter(_motionControlSubmitTimeout)", client, StringComparison.Ordinal);
        Assert.Contains("new MultipartFormDataContent()", client, StringComparison.Ordinal);
        Assert.Contains("new StreamContent(stream)", client, StringComparison.Ordinal);
        Assert.Contains("ContentDispositionHeaderValue(\"form-data\")", client, StringComparison.Ordinal);
        Assert.Contains("CreateMultipartTextPart(\"domain\", request.Domain)", client, StringComparison.Ordinal);
        Assert.Contains("CreateMultipartTextPart(\"project_id\", request.ProjectId)", client, StringComparison.Ordinal);
        Assert.Contains("body.Add(content);", client, StringComparison.Ordinal);
        Assert.Contains("[\"image_url\"] = request.ImageUrl", client, StringComparison.Ordinal);
        Assert.Contains("[\"images[0][url]\"] = request.ImageUrl", client, StringComparison.Ordinal);
        Assert.Contains("[\"video_url\"] = request.VideoUrl", client, StringComparison.Ordinal);
        Assert.Contains("[\"subType\"] = request.SubType", client, StringComparison.Ordinal);
        Assert.Contains("[\"background_source\"] = request.BackgroundSource", client, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"model\"] = request.Model", motionSubmit, StringComparison.Ordinal);
        Assert.Contains("MediaTypeHeaderValue.Parse(file.MimeType)", client, StringComparison.Ordinal);
        Assert.Contains("FindUploadAssetUrl", client, StringComparison.Ordinal);
        Assert.Contains("\"download_url\"", client, StringComparison.Ordinal);
        Assert.Contains("public async Task<Ai79TaskSubmitResult> SubmitAsync", client, StringComparison.Ordinal);
        Assert.Contains("new FormUrlEncodedContent(form)", client, StringComparison.Ordinal);
    }

    [Fact]
    public void RDanceRetryAndResultUiUseFreshMotionStateAndSharedLoadingAnimation()
    {
        var root = FindRepoRoot();
        var repository = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellRepository.cs"));
        var service = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellPhase2Services.cs"));
        var page = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));

        var reset = GetMethodSection(repository, "ResetMotionRenderStateAsync");
        Assert.Contains("provider_task_id=NULL", reset, StringComparison.Ordinal);
        Assert.Contains("provider_status=NULL", reset, StringComparison.Ordinal);
        Assert.Contains("submit_response_json=NULL", reset, StringComparison.Ordinal);
        Assert.Contains("poll_response_json=NULL", reset, StringComparison.Ordinal);
        Assert.Contains("poll_count=0", reset, StringComparison.Ordinal);
        Assert.Contains("error_code=NULL", reset, StringComparison.Ordinal);
        Assert.Contains("error_message=NULL", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("result_video_url", reset, StringComparison.OrdinalIgnoreCase);

        var retry = GetMethodSection(service, "RetryAsync");
        Assert.Contains("ResetMotionRenderStateAsync(job.Id, retry.Id, ct)", retry, StringComparison.Ordinal);

        Assert.Contains("IsResultActive", page, StringComparison.Ordinal);
        Assert.Contains("rdance-result-frame", page, StringComparison.Ordinal);
        Assert.Contains("rdance-processing-overlay", page, StringComparison.Ordinal);
        Assert.Contains("rdance-processing-sweep", page, StringComparison.Ordinal);
        Assert.Contains("rdance-processing-spinner", page, StringComparison.Ordinal);
        Assert.Contains("rdance-processing-dots", page, StringComparison.Ordinal);
        Assert.Contains("ResultErrorText", page, StringComparison.Ordinal);
        Assert.Contains("Render lại video", page, StringComparison.Ordinal);
        Assert.Contains("video src=\"@_job.ResultVideoUrl\"", page, StringComparison.Ordinal);
        Assert.Contains("Tải video", page, StringComparison.Ordinal);
        Assert.Contains("StatusLabel", page, StringComparison.Ordinal);
        Assert.Contains("Bắt đầu:", page, StringComparison.Ordinal);
        Assert.Contains("Kết thúc:", page, StringComparison.Ordinal);
        Assert.Contains("Thời gian thực hiện:", page, StringComparison.Ordinal);
        Assert.Contains("Đã thực hiện:", page, StringComparison.Ordinal);
        Assert.Contains("ReloadAsync(wasActive)", page, StringComparison.Ordinal);
        Assert.Contains("_tabIndex = 3", page, StringComparison.Ordinal);
        Assert.Contains("/api/dance-sell/jobs/{_job.Id}/download", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DanceSellDownloadEndpointUsesOwnedJobUrlAndBlocksArbitraryUrl()
    {
        var root = FindRepoRoot();
        var endpoints = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellPhase2Endpoints.cs"));

        Assert.Contains("group.MapGet(\"/jobs/{id:guid}/download\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("service.GetAsync(id, user, ct)", endpoints, StringComparison.Ordinal);
        Assert.Contains("job.ResultVideoUrl", endpoints, StringComparison.Ordinal);
        Assert.Contains("EnsurePublicHttpsUrlAsync", endpoints, StringComparison.Ordinal);
        Assert.Contains("Results.Stream", endpoints, StringComparison.Ordinal);
        Assert.Contains("video/mp4", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("string url", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Request.Query", endpoints, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DanceSellMotionCostUsesProviderModeInsteadOfBusinessQuality()
    {
        var route = new DanceSellProviderRouteDto
        {
            ProviderCode = "79ai",
            ModelName = "kling_video_motion",
            ConfigJson = """{"mode":"standard","ratio":"default"}"""
        };

        Assert.Equal("standard", DanceSellMotionProviderContract.ResolveProviderMode(route, "720p"));
        Assert.Equal("default", DanceSellMotionProviderContract.ResolveProviderRatio(route));
        Assert.Equal("professional", DanceSellMotionProviderContract.MapBusinessMode("1080p"));

        var root = FindRepoRoot();
        var service = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellPhase2Services.cs"));
        var page = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));
        var estimator = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellAiOperations.cs"));

        Assert.Contains("var providerMode = DanceSellMotionProviderContract.ResolveProviderMode(motionRoute, job.Mode)", service, StringComparison.Ordinal);
        Assert.Contains("EstimateAsync(motionRoute, providerMode", service, StringComparison.Ordinal);
        Assert.Contains("ResolveProviderMode(route, \"720p\")", page, StringComparison.Ordinal);
        Assert.Contains("IAiPricingService _pricing", estimator, StringComparison.Ordinal);
        Assert.Contains("ProviderCode = route.ProviderCode", estimator, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCode = route.ModelName", estimator, StringComparison.Ordinal);
        Assert.Contains("DurationSeconds = duration is null", estimator, StringComparison.Ordinal);
        Assert.Contains("PricingSource = \"provider_catalog\"", estimator, StringComparison.Ordinal);
        Assert.Contains("DanceSell:Pricing:{route.ProviderCode}:{route.ModelName}:{mode}:UsdPerRequest", estimator, StringComparison.Ordinal);
    }

    [Fact]
    public void DanceSellCancelIsIdempotentWhenCoreJobAlreadyCancelled()
    {
        var root = FindRepoRoot();
        var service = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellPhase2Services.cs"));
        var page = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Components", "Pages", "RDanceJobDetail.razor"));
        var cancel = GetMethodSection(service, "CancelAsync");

        Assert.Contains("_renderJobs.GetAsync(job.RenderJobId.Value, ct)", cancel, StringComparison.Ordinal);
        Assert.Contains("coreJob?.Status == RenderJobStatuses.Cancelled", cancel, StringComparison.Ordinal);
        Assert.Contains("if (!await _renderJobs.CancelAsync(job.RenderJobId.Value, reason, user.UserId, ct))", cancel, StringComparison.Ordinal);
        Assert.Contains("coreJob = await _renderJobs.GetAsync(job.RenderJobId.Value, ct)", cancel, StringComparison.Ordinal);
        Assert.Contains("coreJob?.Status != RenderJobStatuses.Cancelled", cancel, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(\"DANCE_SELL_CANCEL_FAILED\")", cancel, StringComparison.Ordinal);

        Assert.Contains("CustomerErrorMessage", page, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_CANCEL_FAILED", page, StringComparison.Ordinal);
        Assert.Contains("Không thể dừng video lúc này. Vui lòng thử lại.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DanceSellMotionUploadFirstFlowValidatesFilesAndKeepsSuccessfulPollWithoutUrlPollable()
    {
        var root = FindRepoRoot();
        var handler = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellRenderHandler.cs"));
        var media = ReadStrictUtf8(Path.Combine(root, "TodoX.Web", "Services", "Media", "MediaFileService.cs"));
        var poll = GetMethodSection(handler, "Poll79AiAsync");

        Assert.Contains("AllowedMotionImageMime", handler, StringComparison.Ordinal);
        Assert.Contains("\"image/jpeg\"", handler, StringComparison.Ordinal);
        Assert.Contains("\"image/png\"", handler, StringComparison.Ordinal);
        Assert.Contains("\"image/webp\"", handler, StringComparison.Ordinal);
        Assert.Contains("AllowedMotionVideoMime", handler, StringComparison.Ordinal);
        Assert.Contains("\"video/mp4\"", handler, StringComparison.Ordinal);
        Assert.Contains("\"video/webm\"", handler, StringComparison.Ordinal);
        Assert.Contains("MaxMotionControlVideoBytes = 50L * 1024 * 1024", handler, StringComparison.Ordinal);
        Assert.Contains("_media.OpenReadAsync(mediaIdValue", handler, StringComparison.Ordinal);
        Assert.Contains("Task<Stream?> OpenReadAsync", media, StringComparison.Ordinal);
        var submit = GetMethodSection(handler, "Submit79AiAsync");
        Assert.DoesNotContain("Convert.ToBase64String", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitMultipartAsync(request, ct)", submit, StringComparison.Ordinal);
        Assert.Contains("UploadMediaAsync(new Ai79MediaUploadRequest", submit, StringComparison.Ordinal);
        Assert.Contains("SubmitMotionControlAsync(request, ct)", submit, StringComparison.Ordinal);
        Assert.Contains("GetLatestAssetAsync", submit, StringComparison.Ordinal);
        Assert.Contains("DanceSellAssetRoles.MotionProviderUpload", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SOURCE_UPLOAD_REUSED", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_STARTED", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_TIMEOUT", submit, StringComparison.Ordinal);
        Assert.Contains("BeginMotionSubmitAttemptAsync", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_SUBMIT_RETRY_EXHAUSTED", submit, StringComparison.Ordinal);
        Assert.Contains("reference = new", submit, StringComparison.Ordinal);
        Assert.Contains("motionUpload = new", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_REFERENCE_USING_APPROVED_URL", submit, StringComparison.Ordinal);
        Assert.Contains("AI79_MOTION_REFERENCE_UPLOAD_SKIPPED", submit, StringComparison.Ordinal);
        Assert.Contains("referenceSource", submit, StringComparison.Ordinal);
        Assert.Contains("referenceUrlUsed", submit, StringComparison.Ordinal);

        Assert.Contains("AI79_MOTION_OUTPUT_PENDING", poll, StringComparison.Ordinal);
        Assert.Contains("runtime.PollIdField", poll, StringComparison.Ordinal);
        Assert.Contains("DANCE_SELL_OUTPUT_URL_TIMEOUT", poll, StringComparison.Ordinal);
        Assert.Contains("await _repo.UpdatePollingAsync(danceJob.Id, status.NormalizedStatus", poll, StringComparison.Ordinal);
        Assert.Contains("await ScheduleNextPollAsync(renderJob, \"79AI motion output URL pending; next poll scheduled.\"", poll, StringComparison.Ordinal);
        Assert.Contains("await _completion.CompleteAsync", poll, StringComparison.Ordinal);
        Assert.Contains("ResultVideoUrl = status.OutputUrl", poll, StringComparison.Ordinal);
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
        if (start < 0)
        {
            start = source.IndexOf($"private async Task<Ai79MotionRuntime> {methodName}(", StringComparison.Ordinal);
        }
        if (start < 0)
        {
            start = source.IndexOf($"public async Task<DanceSellJobDto> {methodName}(", StringComparison.Ordinal);
        }
        if (start < 0)
        {
            start = source.IndexOf($"public async Task<Ai79TaskSubmitResult> {methodName}(", StringComparison.Ordinal);
        }
        if (start < 0)
        {
            start = source.IndexOf($"public async Task {methodName}(", StringComparison.Ordinal);
        }
        if (start < 0)
        {
            start = source.IndexOf($"private static string {methodName}(", StringComparison.Ordinal);
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
