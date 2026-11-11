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

    /// <summary>Confirmation for "Bắt đầu tạo video" (AUTO), describing only what this job will actually do.</summary>
    public static string AutoRunConfirm(int sceneCount, int scenesWithoutImage, bool sharedReferenceImage, bool linked)
    {
        var steps = new List<string>();
        if (!sharedReferenceImage && scenesWithoutImage > 0)
            steps.Add($"tạo ảnh cho {scenesWithoutImage} scene chưa có ảnh");
        steps.Add(sharedReferenceImage ? "tạo video từ ảnh tham chiếu chung" : "tạo video từ ảnh của từng scene");
        var text = $"Hệ thống sẽ tự chạy cho {sceneCount} scene: {string.Join(", rồi ", steps)}";
        if (linked) text += "; scene nối tiếp tạo video từ khung cuối của scene trước";
        return text + ". Không cần duyệt từng scene. Điểm được trừ theo từng ảnh / video tạo ra. Bắt đầu?";
    }

    /// <summary>Why a scene video cannot be started right now, in words the user can act on.</summary>
    public static string VideoBlocked(bool hasActiveJob, string? reason, string? latestJobStatus) => reason switch
    {
        "SceneJobInFlightOrManualReview" or "SceneVersionInFlightOrReconciliation" when !hasActiveJob
            => "Lượt tạo video trước của scene này đang chờ TodoX xác minh. Đợi vài phút rồi thử lại, hoặc dùng \"Tạo lượt video mới\".",
        "ExistingIntentUnresolved" or "pending_reconciliation"
            => "Lượt gửi video trước chưa được xác minh với nhà cung cấp. Đợi vài phút rồi thử lại, hoặc dùng \"Tạo lượt video mới\".",
        _ when hasActiveJob
            => $"Scene này đang có job video chạy ({Status(latestJobStatus)}). Đợi job xong rồi mới tạo lại.",
        _ => "Chưa thể tạo video cho scene này lúc này. Tải lại trang rồi thử lại."
    };

    // Arbitrary backend error text must never become a user-facing provider diagnostic.
    public static string? Error(string? detail, string fallback = "Tạo video thất bại")
        => string.IsNullOrWhiteSpace(detail) ? null : fallback;
}
