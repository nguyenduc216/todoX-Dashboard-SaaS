using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// Fixed settings a skill applies when it runs (null = the customer's choice or TodoX's default). They stay on the
/// server: the plan service applies them, the customer's machine never receives them.
/// </summary>
public sealed record EditVideoSkillSettings(
    string? Aspect = null,
    double? TargetSeconds = null,
    string? StyleId = null,
    string? CaptionStyle = null,
    string? Transition = null,
    double? TransitionSeconds = null,
    double? MusicVolume = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static EditVideoSkillSettings Parse(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<EditVideoSkillSettings>(json, Json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>A skill as edited in the dashboard (the draft) plus its latest published version.</summary>
public sealed class EditVideoSkillRow
{
    public Guid Id { get; init; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int? LatestVersionNo { get; init; }
    public string? LatestVersionName { get; init; }
    /// <summary>The draft differs from the latest published version (or nothing is published yet).</summary>
    public bool HasUnpublishedChanges { get; init; }
}

/// <summary>A published version: a frozen copy of the skill, never edited.</summary>
public sealed class EditVideoSkillVersionRow
{
    public Guid Id { get; init; }
    public Guid SkillId { get; init; }
    public int VersionNo { get; init; }
    public string VersionName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string SettingsJson { get; init; } = "{}";
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>What the customer's tool downloads: every published version of every active skill.</summary>
public sealed record EditVideoSkillFile(
    string Slug, string Title, string? Category, int LatestVersion, IReadOnlyList<EditVideoSkillVersionFile> Versions)
{
    // Kept for 0.6.0 tools, which read one markdown per skill.
    public int Version => LatestVersion;
    public string Markdown => Versions.First(v => v.Version == LatestVersion).Markdown;
}

public sealed record EditVideoSkillVersionFile(int Version, string Name, string? Notes, DateTimeOffset CreatedAt, string Markdown);

/// <summary>
/// TodoX skill library (editvideo.skills + editvideo.skill_versions). Admins edit a draft in the dashboard and publish
/// it as a numbered, named version; only published versions reach customers, and every version is kept so a
/// customer can stay on the one they like. Skill names always start with "todox-".
/// </summary>
public sealed partial class EditVideoSkillService
{
    public const string SlugPrefix = "todox-";

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public EditVideoSkillService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<EditVideoSkillRow>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoSkillRow>(
            """
            SELECT s.id, s.slug, s.title, s.description, s.category, s.content, s.settings::text AS SettingsJson, s.is_active AS IsActive,
                   s.sort_order AS SortOrder, s.updated_at AS UpdatedAt,
                   v.version_no AS LatestVersionNo, v.version_name AS LatestVersionName,
                   (v.id IS NULL OR (s.title, s.description, s.content, s.settings) IS DISTINCT FROM (v.title, v.description, v.content, v.settings)) AS HasUnpublishedChanges
              FROM editvideo.skills s
              LEFT JOIN LATERAL (
                    SELECT id, version_no, version_name, title, description, content, settings
                      FROM editvideo.skill_versions
                     WHERE skill_id = s.id
                     ORDER BY version_no DESC
                     LIMIT 1) v ON true
             WHERE s.tenant_id = @tenantId AND (@activeOnly = false OR s.is_active)
             ORDER BY s.sort_order, s.title;
            """,
            new { tenantId = _tenant.TenantId, activeOnly });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<EditVideoSkillVersionRow>> ListVersionsAsync(Guid skillId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return (await conn.QueryAsync<EditVideoSkillVersionRow>(
            """
            SELECT id, skill_id AS SkillId, version_no AS VersionNo, version_name AS VersionName, title, description,
                   content, settings::text AS SettingsJson, notes, created_at AS CreatedAt
              FROM editvideo.skill_versions
             WHERE skill_id = @skillId AND tenant_id = @tenantId
             ORDER BY version_no DESC;
            """,
            new { skillId, tenantId = _tenant.TenantId })).ToList();
    }

    /// <summary>Active skills that have at least one published version, with all their versions.</summary>
    public async Task<IReadOnlyList<EditVideoSkillFile>> ListFilesAsync(CancellationToken ct = default)
    {
        var skills = (await ListAsync(activeOnly: true, ct)).Where(s => s.LatestVersionNo is not null).ToList();
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var versions = (await conn.QueryAsync<EditVideoSkillVersionRow>(
            """
            SELECT id, skill_id AS SkillId, version_no AS VersionNo, version_name AS VersionName, title, description,
                   content, settings::text AS SettingsJson, notes, created_at AS CreatedAt
              FROM editvideo.skill_versions
             WHERE tenant_id = @tenantId AND skill_id = ANY(@ids)
             ORDER BY version_no;
            """,
            new { tenantId = _tenant.TenantId, ids = skills.Select(s => s.Id).ToArray() })).ToLookup(v => v.SkillId);

        return skills
            .Where(s => versions[s.Id].Any())
            .Select(s => new EditVideoSkillFile(s.Slug, s.Title, s.Category, s.LatestVersionNo!.Value,
                versions[s.Id].Select(v => new EditVideoSkillVersionFile(v.VersionNo, v.VersionName, v.Notes, v.CreatedAt,
                    BuildCustomerMarkdown(s.Slug, v.Title, v.Description, v.VersionNo, v.VersionName))).ToList()))
            .ToList();
    }

    /// <summary>Saves the draft. Nothing reaches customers until a version is published.</summary>
    public async Task<EditVideoResult<EditVideoSkillRow>> SaveAsync(EditVideoSkillRow skill, Guid? actorUserId, CancellationToken ct = default)
    {
        var slug = NormalizeSlug(skill.Slug);
        if (!IsValidSlug(slug))
        {
            return EditVideoResult<EditVideoSkillRow>.Fail(EditVideoErrorCodes.InvalidRequest,
                "Mã skill phải bắt đầu bằng \"todox-\", chỉ gồm chữ thường không dấu, số và dấu gạch ngang (tối đa 64 ký tự).");
        }

        if (string.IsNullOrWhiteSpace(skill.Title) || string.IsNullOrWhiteSpace(skill.Description) || string.IsNullOrWhiteSpace(skill.Content))
        {
            return EditVideoResult<EditVideoSkillRow>.Fail(EditVideoErrorCodes.InvalidRequest, "Cần nhập tên, mô tả khi nào dùng và nội dung skill.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var args = new
        {
            id = skill.Id,
            tenantId = _tenant.TenantId,
            slug,
            title = skill.Title.Trim(),
            description = skill.Description.Trim(),
            category = string.IsNullOrWhiteSpace(skill.Category) ? null : skill.Category.Trim(),
            content = skill.Content.Replace("\r\n", "\n").Trim(),
            settings = EditVideoSkillSettings.Parse(skill.SettingsJson).ToJson(),
            isActive = skill.IsActive,
            sortOrder = skill.SortOrder,
            actor = actorUserId
        };

        var duplicate = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM editvideo.skills WHERE tenant_id = @tenantId AND slug = @slug AND id <> @id);", args);
        if (duplicate)
        {
            return EditVideoResult<EditVideoSkillRow>.Fail(EditVideoErrorCodes.InvalidRequest, $"Đã có skill mã \"{slug}\".");
        }

        if (skill.Id != Guid.Empty)
        {
            // The slug is the folder name on customers' machines; once a version is out it must not move.
            var published = await conn.ExecuteScalarAsync<string?>(
                """
                SELECT s.slug FROM editvideo.skills s
                 WHERE s.id = @id AND s.tenant_id = @tenantId AND EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = s.id);
                """, args);
            if (published is not null && published != slug)
            {
                return EditVideoResult<EditVideoSkillRow>.Fail(EditVideoErrorCodes.InvalidRequest,
                    $"Skill đã phát hành nên không đổi mã được (đang là \"{published}\").");
            }
        }

        var id = skill.Id == Guid.Empty
            ? await conn.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, sort_order, updated_by)
                VALUES (@tenantId, @slug, @title, @description, @category, @content, CAST(@settings AS jsonb), @isActive, @sortOrder, @actor)
                RETURNING id;
                """, args)
            : await conn.ExecuteScalarAsync<Guid>(
                """
                UPDATE editvideo.skills
                   SET slug = @slug, title = @title, description = @description, category = @category, content = @content,
                       settings = CAST(@settings AS jsonb),
                       is_active = @isActive, sort_order = @sortOrder, updated_by = @actor, updated_at = now()
                 WHERE id = @id AND tenant_id = @tenantId
                RETURNING id;
                """, args);

        return await FindAsync(id, ct) is { } saved
            ? EditVideoResult<EditVideoSkillRow>.Success(saved)
            : EditVideoResult<EditVideoSkillRow>.Fail("not_found", "Không tìm thấy skill.");
    }

    /// <summary>Freezes the current draft as the next version number. Earlier versions are kept unchanged.</summary>
    public async Task<EditVideoResult<EditVideoSkillVersionRow>> PublishVersionAsync(Guid skillId, string? versionName, string? notes, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return EditVideoResult<EditVideoSkillVersionRow>.Fail(EditVideoErrorCodes.InvalidRequest, "Đặt tên cho phiên bản (ví dụ: \"Thêm bản đồ vị trí\").");
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        // Lock the skill so two admins publishing at once cannot take the same number.
        var locked = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM editvideo.skills WHERE id = @skillId AND tenant_id = @tenantId FOR UPDATE;",
            new { skillId, tenantId = _tenant.TenantId }, tx);
        if (locked is null)
        {
            return EditVideoResult<EditVideoSkillVersionRow>.Fail("not_found", "Không tìm thấy skill.");
        }

        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, settings, notes, created_by)
            SELECT s.tenant_id, s.id,
                   COALESCE((SELECT max(version_no) FROM editvideo.skill_versions WHERE skill_id = s.id), 0) + 1,
                   @versionName, s.title, s.description, s.content, s.settings, @notes, @actor
              FROM editvideo.skills s
             WHERE s.id = @skillId AND s.tenant_id = @tenantId
            RETURNING id;
            """,
            new { skillId, tenantId = _tenant.TenantId, versionName = versionName.Trim(), notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), actor = actorUserId }, tx);
        tx.Commit();

        var version = (await ListVersionsAsync(skillId, ct)).First(v => v.Id == id);
        return EditVideoResult<EditVideoSkillVersionRow>.Success(version);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM editvideo.skills WHERE id = @id AND tenant_id = @tenantId;", new { id, tenantId = _tenant.TenantId });
    }

    /// <summary>A published version of an active skill by slug; the latest one when no number is given.</summary>
    public async Task<EditVideoSkillVersionRow?> FindVersionAsync(string slug, int? versionNo, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<EditVideoSkillVersionRow>(
            """
            SELECT v.id, v.skill_id AS SkillId, v.version_no AS VersionNo, v.version_name AS VersionName, v.title, v.description,
                   v.content, v.settings::text AS SettingsJson, v.notes, v.created_at AS CreatedAt
              FROM editvideo.skill_versions v
              JOIN editvideo.skills s ON s.id = v.skill_id
             WHERE s.tenant_id = @tenantId AND s.slug = @slug AND s.is_active
               AND (@versionNo::int IS NULL OR v.version_no = @versionNo)
             ORDER BY v.version_no DESC
             LIMIT 1;
            """,
            new { tenantId = _tenant.TenantId, slug = NormalizeSlug(slug), versionNo });
    }

    private async Task<EditVideoSkillRow?> FindAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(activeOnly: false, ct)).FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// SKILL.md as installed on the customer's machine: only when to use the skill and how to start it. The skill's
    /// steps stay on the server and reach Claude one at a time through todox_skill_start / todox_skill_next.
    /// </summary>
    public static string BuildCustomerMarkdown(string slug, string title, string description, int versionNo, string? versionName)
    {
        var md = new StringBuilder();
        md.Append("---\n");
        md.Append("name: ").Append(slug).Append('\n');
        // A JSON string is valid YAML and keeps quotes, colons and line breaks in the description safe.
        md.Append("description: ").Append(JsonSerializer.Serialize(OneLine(description), YamlSafeJson)).Append('\n');
        md.Append("---\n\n");
        md.Append("# ").Append(title.Trim()).Append("\n\n");
        md.Append(description.Replace("\r\n", "\n").Trim()).Append("\n\n");
        md.Append($"Phiên bản skill: **v{versionNo} – {OneLine(versionName ?? string.Empty)}** (`{slug}` v{versionNo}).\n\n");
        md.Append("Cách dùng: gọi `todox_skill_start` với ").Append($"`skill: \"{slug}\"`")
          .Append(" (bỏ `version` để dùng phiên bản người dùng đã chọn). TodoX trả về từng bước: làm đúng bước đó bằng các công cụ `todox_*`, ")
          .Append("xong thì gọi `todox_skill_next` để nhận bước tiếp theo, đến khi TodoX báo hoàn tất. ")
          .Append("Khi gọi `todox_edit_start` / `todox_auto_edit`, truyền `skill` và `skillVersion` mà TodoX trả về để video được đánh dấu.\n\n");
        md.Append("Nếu không có công cụ `todox_skill_start`, hãy nhắc người dùng cập nhật TodoX EditVideo.\n");
        md.Append("Người dùng muốn đổi phiên bản: `todox_skill_versions` để xem, `todox_skill_use` để đặt mặc định.\n");
        return md.ToString();
    }

    /// <summary>
    /// Splits the hidden content into steps at level-2 headings ("## Bước 1 …"). Text before the first heading belongs
    /// to step 1; content without headings is a single step.
    /// </summary>
    public static IReadOnlyList<string> SplitSteps(string content)
    {
        var steps = new List<StringBuilder> { new() };
        var hasHeading = false;
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var heading = line.StartsWith("## ", StringComparison.Ordinal);
            if (heading && hasHeading)
            {
                steps.Add(new StringBuilder());
            }

            hasHeading |= heading;
            steps[^1].Append(line).Append('\n');
        }

        return steps.Select(b => b.ToString().Trim()).Where(t => t.Length > 0).ToList();
    }

    /// <summary>One step as sent to Claude: the step text plus where it is in the run and what to do next.</summary>
    public static string BuildStep(string title, int versionNo, string? versionName, IReadOnlyList<string> steps, int index) =>
        $"[{title} v{versionNo}{(string.IsNullOrWhiteSpace(versionName) ? "" : " – " + OneLine(versionName))} · bước {index + 1}/{steps.Count}]\n\n{steps[index]}";

    /// <summary>Dashboard preview: what lands on the customer's machine and what only the server keeps.</summary>
    public static string BuildPreview(string slug, string title, string description, string content, string settingsJson, int versionNo, string? versionName)
    {
        var steps = SplitSteps(content);
        var md = new StringBuilder();
        md.Append("=== Trên máy khách (SKILL.md Claude đọc) ===\n\n");
        md.Append(BuildCustomerMarkdown(slug, title, description, versionNo, versionName));
        md.Append("\n=== Chỉ server giữ: Claude nhận từng bước một ===\n");
        md.Append("Thiết lập cố định: ").Append(DescribeSettings(EditVideoSkillSettings.Parse(settingsJson))).Append('\n');
        for (var i = 0; i < steps.Count; i++)
        {
            md.Append("\n--- ").Append(BuildStep(title, versionNo, versionName, steps, i)).Append('\n');
        }

        return md.ToString();
    }

    public static string BuildDraftPreview(EditVideoSkillRow skill) =>
        BuildPreview(skill.Slug, skill.Title, skill.Description, skill.Content, skill.SettingsJson, (skill.LatestVersionNo ?? 0) + 1, "(bản nháp)");

    public static string BuildVersionPreview(string slug, EditVideoSkillVersionRow v) =>
        BuildPreview(slug, v.Title, v.Description, v.Content, v.SettingsJson, v.VersionNo, v.VersionName);

    public static string DescribeSettings(EditVideoSkillSettings s)
    {
        var parts = new List<string>();
        if (s.Aspect is { } a) parts.Add("khung " + a);
        if (s.TargetSeconds is { } t) parts.Add($"{t:0.#} giây");
        if (s.StyleId is { } st) parts.Add("style " + st);
        if (s.CaptionStyle is { } c) parts.Add("caption " + c);
        if (s.Transition is { } tr) parts.Add("chuyển cảnh " + tr + (s.TransitionSeconds is { } ts ? $" {ts:0.##}s" : ""));
        else if (s.TransitionSeconds is { } ts2) parts.Add($"chuyển cảnh {ts2:0.##}s");
        if (s.MusicVolume is { } m) parts.Add($"nhạc {m:0.##}");
        return parts.Count == 0 ? "không (theo yêu cầu của khách)" : string.Join(" · ", parts);
    }

    public static string NormalizeSlug(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidSlug(string slug) => slug.Length <= 64 && SlugPattern().IsMatch(slug);

    /// <summary>Suggests a slug from a Vietnamese title: "TodoX BĐS vẽ bản đồ" → "todox-bds-ve-ban-do".</summary>
    public static string SuggestSlug(string? title)
    {
        var plain = RemoveDiacritics(title ?? string.Empty).ToLowerInvariant();
        var slug = NonSlugChars().Replace(plain, "-").Trim('-');
        if (!slug.StartsWith(SlugPrefix, StringComparison.Ordinal))
        {
            slug = slug == "todox" ? SlugPrefix.TrimEnd('-') : SlugPrefix + slug;
        }

        return slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
    }

    internal static string RemoveDiacritics(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string OneLine(string text) => Regex.Replace(text.Trim(), @"\s+", " ");

    private static readonly JsonSerializerOptions YamlSafeJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [GeneratedRegex("^todox-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
