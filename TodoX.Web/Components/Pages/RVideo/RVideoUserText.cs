namespace TodoX.Web.Components.Pages.RVideo;

// Presentation only; backend state, diagnostics and provider identities are preserved.
internal static class RVideoUserText
{
    // Return only recognized application diagnostics; never display transport/provider exception text.
    public static string ActionError(string? code, string? message)
    {
        var safeCode = code is { Length: > 0 and <= 80 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? code : "RVIDEO_ACTION_FAILED";
        var safeMessage = code switch
        {
            "RVIDEO_USER_REQUIRED" => "Phiên đăng nhập không hợp lệ.",
            "RVIDEO_SCENE_REQUIRED" => "Không tìm thấy scene.",
            "SCENE_IMAGE_PROMPT_EMPTY" => "Cần nhập prompt ảnh trước khi render.",
            "RVIDEO_REFERENCE_IMAGE_UNAVAILABLE" => "Không tìm thấy ảnh tham chiếu nhân vật.",
            "RVIDEO_VIDEO_IMAGE_REQUIRED" => "Cần chọn ảnh nguồn trước khi tạo video.",
            "RVIDEO_VIDEO_PROMPT_REQUIRED" or "RVIDEO_VIDEO_PROMPT_INVALID" => "Cần kiểm tra prompt video.",
            "RVIDEO_MODEL_INVALID" => "Tùy chọn video không được model hỗ trợ.",
            "RVIDEO_ESTIMATE_UNAVAILABLE" => "Chưa thể ước tính chi phí để tạo lượt mới.",
            "INSUFFICIENT_POINTS" => "Không đủ điểm để tạo lượt mới.",
            "SCENE_IMAGE_ACTIVE" => "Scene đang có phiên bản ảnh được xử lý.",
            "RVIDEO_SUBMIT_RECONCILIATION_PENDING" => "Lượt gửi cũ chưa được xác minh. Hãy chọn Tạo lượt video mới.",
            _ => "Không thể thực hiện. Hãy tải lại trạng thái trước khi thử lại."
        };
        return $"{safeCode}: {safeMessage}";
    }

    public static string Status(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "completed" or "success" or "video_ready" or "ready" => "Hoàn tất",
        "failed" or "failure" or "error" or "cancelled" or "canceled" => "Tạo video thất bại",
        "queued" or "video_queued" or "submitted" or "pending" or "preparing" or "processing"
            or "rendering" or "video_rendering" or "post_processing" or "downloading" or "importing" => "Đang tạo video",
        _ => "Chưa thể thử lại"
    };

    // Arbitrary backend error text must never become a user-facing provider diagnostic.
    public static string? Error(string? detail, string fallback = "Tạo video thất bại")
        => string.IsNullOrWhiteSpace(detail) ? null : fallback;
}
