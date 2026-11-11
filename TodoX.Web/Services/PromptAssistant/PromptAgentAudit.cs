using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.Web.Services.PromptAssistant;

public static class PromptAgentAuditStatuses
{
    /// <summary>The agent's own output already follows the TodoX video contract.</summary>
    public const string Pass = "pass";
    /// <summary>Usable only because TodoX fills / rounds fields (PromptAssistantVideoOutputNormalizer).</summary>
    public const string Fixed = "fixed";
    /// <summary>Jobs will fail: required content is missing even after TodoX's fixes.</summary>
    public const string Fail = "fail";
    /// <summary>The agent could not be run (disabled, connection, malformed JSON...).</summary>
    public const string Error = "error";

    public static string Label(string? status) => status switch
    {
        Pass => "Agent đạt chuẩn",
        Fixed => "Đạt nhờ TodoX tự bổ sung",
        Fail => "Agent không đạt",
        Error => "Không chạy được agent",
        _ => "Chưa kiểm tra agent"
    };
}

public static class PromptAgentAuditCheckStatuses
{
    public const string Ok = "ok";
    public const string Fixed = "fixed";
    public const string Missing = "missing";
}

public sealed record PromptAgentAuditCheck(string Field, string Label, string Status, string Detail);

/// <summary>Result of checking one prompt agent against the TodoX scene contract; stored on the service.</summary>
public sealed record PromptAgentAuditReport
{
    public string Status { get; init; } = PromptAgentAuditStatuses.Error;
    public DateTime CheckedAtUtc { get; init; }
    public string? CheckedBy { get; init; }
    public string? ModelCode { get; init; }
    public int? AgentId { get; init; }
    public string? AgentIdBase { get; init; }
    public int SceneCount { get; init; }
    public int? TotalSeconds { get; init; }
    public Guid? GenerationId { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<PromptAgentAuditCheck> Checks { get; init; } = [];

    /// <summary>The agent configuration changed after this audit, so it no longer describes the agent in use.</summary>
    public bool IsStaleFor(ServicePromptAssistantDto assistant)
        => AgentId != assistant.GommoAgentId
           || !string.Equals(AgentIdBase ?? "", assistant.GommoAgentIdBase ?? "", StringComparison.Ordinal)
           || !string.Equals(ModelCode ?? "", assistant.ModelCode ?? "", StringComparison.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static PromptAgentAuditReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() is "null" or "{}") return null;
        try { return JsonSerializer.Deserialize<PromptAgentAuditReport>(json, Json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>Grades an agent's raw script field by field: what it gets right, what TodoX repairs, what is missing.</summary>
public static class PromptAgentAudit
{
    // Sample used when the admin does not type one: short Vietnamese narration like real customer input.
    public const string DefaultSampleInput =
        "Người gan dạ nhất thế gian kỳ thực chính là phụ nữ. Họ dám đánh cược cả cuộc đời vào hai ván bài: chưa hiểu thấu bản chất đàn ông đã dám trao thân gửi phận, "
        + "và chưa biết nuôi dạy con thế nào đã dám sinh con đẻ cái. Hôn nhân với phụ nữ, thắng là may mắn, còn thua chính là thực tế phũ phàng của cuộc đời.";

    private static readonly (string Field, string Label)[] Fields =
    [
        ("scenes", "Danh sách scene"),
        ("duration_seconds", $"Thời lượng mỗi scene (số nguyên {PromptAssistantVideoOutputValidator.MinSceneSeconds}–{PromptAssistantVideoOutputValidator.MaxSceneSeconds} giây)"),
        ("image_prompt", "Prompt tạo ảnh (image_prompt)"),
        ("motion_prompt", "Mô tả chuyển động để tạo video (motion_prompt)"),
        ("voice", "Lời đọc (voice)"),
        ("tts_rate", "Tốc độ đọc (tts_rate)")
    ];

    public static PromptAgentAuditReport Grade(string? generatedJson)
    {
        if (string.IsNullOrWhiteSpace(generatedJson))
            return new() { Status = PromptAgentAuditStatuses.Error, Error = "Agent không trả về JSON." };

        JsonDocument raw;
        try { raw = JsonDocument.Parse(generatedJson); }
        catch (JsonException) { return new() { Status = PromptAgentAuditStatuses.Error, Error = "Agent trả về nội dung không phải JSON hợp lệ." }; }

        using (raw)
        {
            var rawErrors = PromptAssistantVideoOutputValidator.Validate(raw.RootElement);
            var normalizedJson = PromptAssistantVideoOutputNormalizer.Normalize(generatedJson);
            using var normalized = JsonDocument.Parse(normalizedJson);
            var fixedErrors = PromptAssistantVideoOutputValidator.Validate(normalized.RootElement);
            var sceneList = normalized.RootElement.ValueKind == JsonValueKind.Object
                            && normalized.RootElement.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array
                ? scenes.EnumerateArray().ToList()
                : [];
            var sceneCount = sceneList.Count;
            int? total = sceneCount > 0 && fixedErrors.All(e => FieldOf(e) != "duration_seconds")
                ? sceneList.Sum(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("duration_seconds", out var d) && d.TryGetInt32(out var v) ? v : 0)
                : null;

            var checks = Fields.Select(f =>
            {
                var before = ScenesWith(rawErrors, f.Field);
                var after = ScenesWith(fixedErrors, f.Field);
                if (f.Field == "scenes")
                {
                    return after > 0 || sceneCount == 0
                        ? new PromptAgentAuditCheck(f.Field, f.Label, PromptAgentAuditCheckStatuses.Missing, "Agent không trả về mảng scenes.")
                        : new PromptAgentAuditCheck(f.Field, f.Label, PromptAgentAuditCheckStatuses.Ok, $"{sceneCount} scene.");
                }
                if (after > 0)
                    return new PromptAgentAuditCheck(f.Field, f.Label, PromptAgentAuditCheckStatuses.Missing, $"Sai hoặc thiếu ở {after}/{sceneCount} scene, TodoX không tự bổ sung được.");
                if (before > 0)
                    return new PromptAgentAuditCheck(f.Field, f.Label, PromptAgentAuditCheckStatuses.Fixed, $"Agent sai hoặc thiếu ở {before}/{sceneCount} scene; TodoX tự bổ sung khi tạo job.");
                return new PromptAgentAuditCheck(f.Field, f.Label, PromptAgentAuditCheckStatuses.Ok, "Đạt.");
            }).ToList();

            var status = checks.Any(c => c.Status == PromptAgentAuditCheckStatuses.Missing) ? PromptAgentAuditStatuses.Fail
                : checks.Any(c => c.Status == PromptAgentAuditCheckStatuses.Fixed) ? PromptAgentAuditStatuses.Fixed
                : PromptAgentAuditStatuses.Pass;
            return new() { Status = status, SceneCount = sceneCount, TotalSeconds = total, Checks = checks };
        }
    }

    private static int ScenesWith(IEnumerable<ServicePromptValidationError> errors, string field)
        => errors.Where(e => FieldOf(e) == field).Select(e => e.Path).Distinct().Count();

    // "$.scenes[2].motion_prompt" -> "motion_prompt"; "$.scenes" -> "scenes".
    private static string FieldOf(ServicePromptValidationError error)
    {
        var last = error.Path.Split('.').Last();
        return last.StartsWith("scenes", StringComparison.Ordinal) ? "scenes" : last;
    }
}
