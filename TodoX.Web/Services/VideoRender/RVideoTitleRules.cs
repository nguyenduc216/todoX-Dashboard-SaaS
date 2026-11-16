using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// RVideo drafts start as "Video mới". Once the video has content, a short title is taken from it so "Jobs của tôi"
/// never lists a row of identical "Video mới". The same rules run in
/// database/manual/rvideo/20261116_rvideo_titles_from_content.sql for the videos made before.
/// </summary>
public static class RVideoTitleRules
{
    public const string DraftTitle = "Video mới";
    public const int MaxLength = 60;

    private static readonly string[] Placeholders = [DraftTitle, "Prompt workspace"];

    /// <summary>True when the title says nothing about the video (empty, the draft default, or raw JSON).</summary>
    public static bool IsPlaceholder(string? title)
    {
        var value = title?.Trim();
        return string.IsNullOrEmpty(value)
               || Placeholders.Contains(value, StringComparer.OrdinalIgnoreCase)
               || value.StartsWith('{') || value.StartsWith('[');
    }

    /// <summary>
    /// A short title from the video's content: the script's own title (video_title / title / meta.product_name /
    /// meta.video_title), else the description the customer typed, else a plain-text prompt. Null when there is none.
    /// </summary>
    public static string? FromContent(string? prompt, string? draftInput)
    {
        var trimmedPrompt = prompt?.Trim() ?? string.Empty;
        var promptIsJson = trimmedPrompt.StartsWith('{') || trimmedPrompt.StartsWith('[');
        if (promptIsJson && ScriptTitle(trimmedPrompt) is { } scriptTitle && Shorten(scriptTitle) is { } fromScript)
            return fromScript;

        var draft = draftInput?.Trim();
        if (!string.IsNullOrEmpty(draft) && !draft.StartsWith('{') && !draft.StartsWith('[') && Shorten(draft) is { } fromDraft)
            return fromDraft;

        return !promptIsJson && trimmedPrompt.Length > 0 ? Shorten(trimmedPrompt) : null;
    }

    /// <summary>First line, first sentence, at most <see cref="MaxLength"/> characters cut at a word.</summary>
    public static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Replace("\r", string.Empty).Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        line = Regex.Replace(line, @"^\s*(?:[-*•#>]+|\d+[.)])\s*", string.Empty);
        line = Regex.Replace(line, @"\s+", " ").Trim().Trim('"', '“', '”', '\'').Trim();
        var sentence = Regex.Match(line, @"^(.+?[.!?])(\s|$)");
        if (sentence.Success && sentence.Groups[1].Value.Length >= 10) line = sentence.Groups[1].Value;
        line = line.TrimEnd('.', ' ');
        if (line.Length > MaxLength)
        {
            var cut = Regex.Replace(line[..MaxLength], @"\s+\S*$", string.Empty);
            line = (cut.Length > 0 ? cut : line[..MaxLength]).TrimEnd(',', ';', ':', '-', ' ') + "…";
        }
        return line.Length == 0 || IsPlaceholder(line) ? null : line;
    }

    private static string? ScriptTitle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var meta = root.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            return Read(root, "video_title") ?? Read(root, "title") ?? Read(meta, "product_name") ?? Read(meta, "video_title");
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Read(JsonElement element, string key)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value)
               && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Gives an RVideo job a title from its content when it still has a placeholder title (core job input + project).
    /// Returns the new title, or null when nothing changed.
    /// </summary>
    public static async Task<string?> FillPlaceholderAsync(IDbConnection conn, Guid tenantId, Guid? jobId = null, long? projectId = null)
    {
        if (jobId is null && projectId is null) return null;
        var row = await conn.QuerySingleOrDefaultAsync<(Guid JobId, long ProjectId, string? JobTitle, string? ProjectTitle, string? Prompt, string? Draft)>(
            """
            SELECT j.id, p.id, j.input_json->>'title', p.title, p.original_prompt, j.input_json->>'draftInput'
              FROM render.render_jobs j
              JOIN video_render.video_projects p ON p.core_job_id = j.id AND p.tenant_id = j.tenant_id
             WHERE j.tenant_id = @tenantId
               AND (@jobId::uuid IS NULL OR j.id = @jobId)
               AND (@projectId::bigint IS NULL OR p.id = @projectId)
             LIMIT 1;
            """,
            new { tenantId, jobId, projectId });
        if (row.JobId == Guid.Empty || !IsPlaceholder(row.JobTitle) || !IsPlaceholder(row.ProjectTitle)) return null;
        var title = FromContent(row.Prompt, row.Draft);
        if (title is null) return null;
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs SET input_json = jsonb_set(COALESCE(input_json, '{}'::jsonb), '{title}', to_jsonb(@title::text))
             WHERE id = @jobId AND tenant_id = @tenantId;
            UPDATE video_render.video_projects SET title = @title WHERE id = @projectId AND tenant_id = @tenantId;
            """,
            new { title, jobId = row.JobId, projectId = row.ProjectId, tenantId });
        return title;
    }
}
