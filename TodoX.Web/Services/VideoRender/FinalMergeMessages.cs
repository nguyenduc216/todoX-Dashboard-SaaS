using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public static class FinalMergeMessages
{
    public static bool IsActive(string status) => status is RenderJobStatuses.Queued or RenderJobStatuses.Preparing
        or RenderJobStatuses.Rendering or RenderJobStatuses.PostProcessing or RenderJobStatuses.PendingReconciliation;

    public static string Blocked(string reason) => reason switch
    {
        "unauthorized" => "Bạn không có quyền ghép lại project này.",
        "final_versioning_required" => "Lịch sử final video đang tắt. Quản trị viên cần kiểm tra preflight, staging và phê duyệt bật versioning; bản final cũ được giữ nguyên.",
        "final_versioning_schema_not_ready" => "Cấu trúc lưu lịch sử final video chưa sẵn sàng. Cần quản trị viên kiểm tra preflight; không thể ghép lại an toàn lúc này.",
        "final_versioning_setting_missing_or_ambiguous" => "Cấu hình final video versioning bị thiếu, không hoạt động hoặc trùng lặp. Cần quản trị viên kiểm tra preflight.",
        "final_versioning_unavailable" => "Chưa kiểm tra được trạng thái final video versioning. Vui lòng thử lại sau; bản final cũ được giữ nguyên.",
        "not_ready" => "Chưa thể ghép: mỗi scene cần có selected video version đã hoàn tất và liên kết voice hợp lệ. Cần kiểm tra dữ liệu scene; hệ thống không tự render lại.",
        "settings_missing" => "Project chưa có cấu hình ghép video. Cần kiểm tra dữ liệu project.",
        "no_scenes" => "Project chưa có scene để ghép.",
        "project_missing" => "Không tìm thấy project.",
        "already_final" => "Final video đã sẵn sàng.",
        _ => "Chưa thể ghép video. Vui lòng kiểm tra project và thử lại."
    };
}
