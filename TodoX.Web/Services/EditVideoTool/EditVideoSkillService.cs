using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>A skill as edited in the dashboard (the draft) plus its latest published version.</summary>
public sealed class EditVideoSkillRow
{
    public Guid Id { get; init; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Content { get; set; } = string.Empty;
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
            SELECT s.id, s.slug, s.title, s.description, s.category, s.content, s.is_active AS IsActive,
                   s.sort_order AS SortOrder, s.updated_at AS UpdatedAt,
                   v.version_no AS LatestVersionNo, v.version_name AS LatestVersionName,
                   (v.id IS NULL OR (s.title, s.description, s.content) IS DISTINCT FROM (v.title, v.description, v.content)) AS HasUnpublishedChanges
              FROM editvideo.skills s
              LEFT JOIN LATERAL (
                    SELECT id, version_no, version_name, title, description, content
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
                   content, notes, created_at AS CreatedAt
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
                   content, notes, created_at AS CreatedAt
              FROM editvideo.skill_versions
             WHERE tenant_id = @tenantId AND skill_id = ANY(@ids)
             ORDER BY version_no;
            """,
            new { tenantId = _tenant.TenantId, ids = skills.Select(s => s.Id).ToArray() })).ToLookup(v => v.SkillId);

        return skills
            .Where(s => versions[s.Id].Any())
            .Select(s => new EditVideoSkillFile(s.Slug, s.Title, s.Category, s.LatestVersionNo!.Value,
                versions[s.Id].Select(v => new EditVideoSkillVersionFile(v.VersionNo, v.VersionName, v.Notes, v.CreatedAt,
                    BuildMarkdown(s.Slug, v.Title, v.Description, v.Content, v.VersionNo, v.VersionName))).ToList()))
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
                INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, is_active, sort_order, updated_by)
                VALUES (@tenantId, @slug, @title, @description, @category, @content, @isActive, @sortOrder, @actor)
                RETURNING id;
                """, args)
            : await conn.ExecuteScalarAsync<Guid>(
                """
                UPDATE editvideo.skills
                   SET slug = @slug, title = @title, description = @description, category = @category, content = @content,
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
            INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, created_by)
            SELECT s.tenant_id, s.id,
                   COALESCE((SELECT max(version_no) FROM editvideo.skill_versions WHERE skill_id = s.id), 0) + 1,
                   @versionName, s.title, s.description, s.content, @notes, @actor
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

    private async Task<EditVideoSkillRow?> FindAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(activeOnly: false, ct)).FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// SKILL.md as installed on the customer's machine: front matter (name, description) + body, and for a published
    /// version a closing note so Claude passes the skill and version to todox_auto_edit (each video is tagged).
    /// </summary>
    public static string BuildMarkdown(string slug, string title, string description, string content, int? versionNo = null, string? versionName = null)
    {
        var md = new StringBuilder();
        md.Append("---\n");
        md.Append("name: ").Append(slug).Append('\n');
        // A JSON string is valid YAML and keeps quotes, colons and line breaks in the description safe.
        md.Append("description: ").Append(JsonSerializer.Serialize(OneLine(description), YamlSafeJson)).Append('\n');
        md.Append("---\n\n");
        md.Append("# ").Append(title.Trim()).Append("\n\n");
        md.Append(content.Replace("\r\n", "\n").Trim()).Append('\n');
        if (versionNo is { } no)
        {
            md.Append("\n---\n\n");
            md.Append($"Phiên bản skill: **v{no} – {OneLine(versionName ?? string.Empty)}** (`{slug}` v{no}).\n");
            md.Append($"Khi gọi `todox_auto_edit`, luôn truyền `skill: \"{slug}\"` và `skillVersion: {no}` để video được đánh dấu theo phiên bản này.\n");
            md.Append("Người dùng muốn đổi phiên bản: `todox_skill_versions` để xem, `todox_skill_use` để đặt mặc định, `todox_skill_get` để dùng một phiên bản khác cho lần này.\n");
        }

        return md.ToString();
    }

    public static string BuildDraftMarkdown(EditVideoSkillRow skill) =>
        BuildMarkdown(skill.Slug, skill.Title, skill.Description, skill.Content, (skill.LatestVersionNo ?? 0) + 1, "(bản nháp)");

    public static string BuildVersionMarkdown(string slug, EditVideoSkillVersionRow v) =>
        BuildMarkdown(slug, v.Title, v.Description, v.Content, v.VersionNo, v.VersionName);

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
