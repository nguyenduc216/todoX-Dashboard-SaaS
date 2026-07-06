namespace TodoX.Web.Services.Images;

public static class ServiceImageRenderStepCatalog
{
    public static readonly IReadOnlyDictionary<string, string> VietnameseNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01_RENDER_REQUEST_RECEIVED"] = "Nhận yêu cầu render",
            ["02_BRIEF_ANALYSIS_STARTED"] = "Bắt đầu phân tích brief",
            ["03_BRIEF_ANALYZED"] = "Đã phân tích brief",
            ["02_CREATIVE_BRIEF_CREATED"] = "Tạo creative brief",
            ["03_LAYOUT_PLAN_CREATED"] = "Tạo layout plan",
            ["04_RENDER_PLAN_CREATED"] = "Tạo render plan",
            ["05_RENDER_PLAN_QC_CHECKED"] = "Kiểm tra render plan",
            ["04_PROMPT_COMPILED"] = "Compile prompt nền",
            ["06_FORBIDDEN_TERMS_BLOCKED"] = "Chặn prompt sai nghiệp vụ",
            ["07_REFERENCE_IMAGES_CLASSIFIED"] = "Phân loại ảnh tham chiếu",
            ["08_FIXED_ASSETS_NORMALIZED"] = "Chuẩn hóa fixed assets",
            ["08_FIXED_ASSET_PIPELINE_SELECTED"] = "Chọn pipeline fixed asset",
            ["09_BACKGROUND_RENDER_REQUEST"] = "Gọi API render nền",
            ["10_BACKGROUND_RENDER_RESPONSE"] = "Nhận phản hồi render nền",
            ["11_FIXED_ASSET_LOADED"] = "Load fixed asset",
            ["12_FIXED_ASSET_BACKGROUND_PROCESSING_STARTED"] = "Bắt đầu xử lý nền robot",
            ["13_FIXED_ASSET_BACKGROUND_PROCESSED"] = "Đã xử lý nền robot",
            ["14_FIXED_ASSET_COMPOSITED"] = "Composite robot",
            ["15_TEXT_OVERLAY_APPLIED"] = "Overlay text",
            ["16_FINAL_IMAGE_STORED"] = "Lưu ảnh cuối",
            ["17_QC_STARTED"] = "Bắt đầu QC",
            ["18_QC_PASSED"] = "QC đạt",
            ["18_QC_FAILED"] = "QC không đạt",
            ["19_RENDER_COMPLETED"] = "Hoàn tất render",
            ["19_RENDER_FAILED"] = "Render lỗi"
        };

    public static string GetName(string? stepCode)
        => !string.IsNullOrWhiteSpace(stepCode) && VietnameseNames.TryGetValue(stepCode, out var name)
            ? name
            : stepCode ?? string.Empty;
}
