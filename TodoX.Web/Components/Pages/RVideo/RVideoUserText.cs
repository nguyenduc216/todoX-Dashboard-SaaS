namespace TodoX.Web.Components.Pages.RVideo;

// Presentation only; backend state, diagnostics and provider identities are preserved.
internal static class RVideoUserText
{
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
