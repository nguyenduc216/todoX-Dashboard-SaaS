using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

// Turns the latest automatic-pipeline failure recorded on a project into one line the customer can act on
// (or report), instead of scenes silently waiting.
public static class RVideoPipelineIssue
{
    private static readonly string[] Types =
    {
        "RVIDEO_AUTO_VALIDATION_FAILED", "SCENE_IMAGE_REFERENCE_VALIDATION_FAILED", "RVIDEO_SCENE_LINK_FAILED"
    };

    public static string? Latest(VideoProjectDto project, DateTime? nowUtc = null)
    {
        var since = (nowUtc ?? DateTime.UtcNow).AddMinutes(-30);
        var issue = project.Events
            .Where(e => Types.Contains(e.EventType, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault();
        if (issue is null || ToUtc(issue.CreatedAt) < since) return null;
        // A later successful link or image run means the problem is over.
        var resolved = project.Events.Any(e => e.CreatedAt > issue.CreatedAt
            && (e.EventType is "RVIDEO_SCENE_LINK_STARTED" or "SCENE_IMAGE_QUEUED"));
        if (resolved) return null;
        var error = ReadError(issue.DataJson);
        var what = issue.EventType switch
        {
            "RVIDEO_SCENE_LINK_FAILED" => "Chưa tạo được ảnh nối tiếp từ khung cuối của cảnh trước",
            "SCENE_IMAGE_REFERENCE_VALIDATION_FAILED" => "Chưa dùng được ảnh tham chiếu để tạo ảnh scene",
            _ => "Chế độ tự động đang tạm dừng vì cấu hình chưa hợp lệ"
        };
        return string.IsNullOrWhiteSpace(error) ? $"{what}." : $"{what} ({error}).";
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static string? ReadError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                var text = e.GetString() ?? string.Empty;
                return text.Length > 160 ? text[..160] : text;
            }
        }
        catch (JsonException) { }
        return null;
    }
}
