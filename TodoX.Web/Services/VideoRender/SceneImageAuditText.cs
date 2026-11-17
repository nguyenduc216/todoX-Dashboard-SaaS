using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// The image audit line under a scene: which model is working on the image and for how long, or why the last
/// attempt failed. Before this, a stuck 79AI task only showed "Đang tạo ảnh" with no way to tell it apart from
/// normal work.
/// </summary>
public static class SceneImageAuditText
{
    public enum Tone { Info, Warning, Error }

    public sealed record Line(string Text, Tone Tone);

    private static readonly string[] Active = ["queued", "submitted", "pending", "processing", "pending_reconciliation"];

    public static bool IsActive(string? status) => Active.Contains(status?.Trim().ToLowerInvariant());

    public static Line? Describe(SceneImageAudit? audit, DateTime utcNow)
    {
        if (audit is null) return null;
        var model = ModelLabel(audit.Model);
        if (IsActive(audit.Status))
        {
            var waited = Math.Max(0, (int)(utcNow - AsUtc(audit.CreatedAt)).TotalMinutes);
            var who = model is null ? "Đang tạo ảnh" : $"Đang tạo ảnh bằng {model}";
            var text = audit.ProviderTaskId is null ? $"{who} · chờ xếp hàng {waited} phút" : $"{who} · đã chờ 79AI {waited} phút";
            return waited >= SceneImageRenderWorkItemHandler.DefaultPollTimeoutMinutes
                ? new Line(text + " — lâu bất thường, hệ thống sẽ tự chuyển model khác.", Tone.Warning)
                : new Line(text + ".", Tone.Info);
        }

        if (string.Equals(audit.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = Reason(audit);
            return new Line($"Lần tạo ảnh gần nhất{(model is null ? "" : $" ({model})")} lỗi: {reason}", Tone.Error);
        }

        return null;
    }

    private static string Reason(SceneImageAudit audit) => audit.ErrorCode switch
    {
        SceneImageRenderWorkItemHandler.ProviderTimeoutCode => audit.ErrorMessage ?? "79AI không trả ảnh đúng hạn.",
        SceneImageStuckSweeper.StuckCode => "bị treo, hệ thống đã gỡ. Bấm \"Tạo ảnh\" để tạo lại.",
        _ => string.IsNullOrWhiteSpace(audit.ErrorMessage) ? "không rõ nguyên nhân." : audit.ErrorMessage!.Trim()
    };

    public static string? ModelLabel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var entry = RVideoImageModelPolicy.Models.FirstOrDefault(x => string.Equals(x.Model, model, StringComparison.OrdinalIgnoreCase));
        return entry?.DisplayName ?? model;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
