using System.Text.Json;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoFallbackStep(
    DateTime At,
    string Kind,            // "failed" | "switched" | "exhausted" | "duration"
    string Model,           // friendly model name of the attempt (the failed one, or the target of a switch)
    string? FromModel,      // for "switched": the model that did not work
    string? Reason,         // user-facing reason
    string? ErrorCode,      // provider/app error code
    string? ErrorDetail);   // raw provider message, shown to TodoX staff only

/// <summary>
/// Model fallback history of a scene's latest video attempt, rebuilt from the project events the video worker writes
/// (RVIDEO_VIDEO_FALLBACK_FAILED / _STARTED / _EXHAUSTED / RVIDEO_VIDEO_DURATION_FALLBACK_STARTED).
/// </summary>
public static class RVideoFallbackTimeline
{
    private const string Failed = "RVIDEO_VIDEO_FALLBACK_FAILED";
    private const string Started = "RVIDEO_VIDEO_FALLBACK_STARTED";
    private const string Exhausted = "RVIDEO_VIDEO_FALLBACK_EXHAUSTED";
    private const string Duration = "RVIDEO_VIDEO_DURATION_FALLBACK_STARTED";
    private static readonly HashSet<string> Types = [Failed, Started, Exhausted, Duration];

    public static IReadOnlyList<RVideoFallbackStep> ForScene(IEnumerable<VideoProjectEventDto> events, long sceneId)
    {
        var parsed = new List<(VideoProjectEventDto Event, JsonElement Data, string? JobId)>();
        foreach (var e in events)
        {
            if (!Types.Contains(e.EventType) || string.IsNullOrWhiteSpace(e.DataJson)) continue;
            JsonElement data;
            try { data = JsonDocument.Parse(e.DataJson).RootElement.Clone(); }
            catch (JsonException) { continue; }
            if (Long(data, "sceneId") != sceneId) continue;
            parsed.Add((e, data, Str(data, "renderJobId")));
        }
        if (parsed.Count == 0) return [];

        // Only the latest attempt (render job) of the scene.
        var latestJob = parsed.OrderByDescending(x => x.Event.CreatedAt).ThenByDescending(x => x.Event.Id).First().JobId;
        return parsed
            .Where(x => x.JobId == latestJob)
            .OrderBy(x => x.Event.CreatedAt).ThenBy(x => x.Event.Id)
            .Select(x => ToStep(x.Event, x.Data))
            .ToList();
    }

    private static RVideoFallbackStep ToStep(VideoProjectEventDto e, JsonElement d)
    {
        var classification = Str(d, "failureClassification");
        var code = Str(d, "providerErrorCode");
        var detail = Str(d, "errorMessage");
        return e.EventType switch
        {
            Started => new(e.CreatedAt, "switched", ModelName(Str(d, "toModel"), Str(d, "toMode")),
                ModelName(Str(d, "fromModel"), Str(d, "fromMode")), Reason(classification), code, detail),
            Exhausted => new(e.CreatedAt, "exhausted", ModelName(Str(d, "model"), Str(d, "mode")), null, Reason(classification), code, detail),
            Duration => new(e.CreatedAt, "duration", ModelName(Str(d, "model") ?? Str(d, "toModel"), Str(d, "mode") ?? Str(d, "toMode")),
                null, "Model không hỗ trợ đúng thời lượng, thử thời lượng gần nhất", code, detail),
            _ => new(e.CreatedAt, "failed", ModelName(Str(d, "model"), Str(d, "mode")), null, Reason(classification), code, detail)
        };
    }

    public static string Reason(string? classification) => classification switch
    {
        "AUTHENTICATION_FAILURE" => "lỗi xác thực với nhà cung cấp",
        "BILLING_FAILURE" => "tài khoản nhà cung cấp hết credit / lỗi thanh toán",
        "INVALID_INPUT" => "nhà cung cấp từ chối nội dung hoặc thiết lập (prompt, ảnh, thời lượng)",
        "TRANSIENT_PROVIDER_FAILURE" => "nhà cung cấp quá tải / lỗi tạm thời",
        "MODEL_PROVIDER_FAILURE" => "model báo lỗi khi tạo video",
        null or "" => "không rõ lý do",
        _ => classification.ToLowerInvariant().Replace('_', ' ')
    };

    public static string ModelName(string? model, string? mode)
    {
        var name = model?.Trim().ToLowerInvariant() switch
        {
            "veo_omni" => "Veo 3.1 Omni",
            "veo_3_1" => mode?.Trim().ToLowerInvariant() switch { "fast" => "Veo 3.1 Fast", "lite" => "Veo 3.1 Lite", _ => "Veo 3.1" },
            "gemini-omni-flash-1.2" => "Gemini Omni Flash 1.2",
            "grok_video_heavy" => "Grok Video",
            null or "" => "model không rõ",
            _ => model!.Trim()
        };
        return name;
    }

    private static string? Str(JsonElement d, string name)
        => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement d, string name)
        => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n
              : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) ? s : null
            : null;
}
