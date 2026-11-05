using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoSkillRow
{
    public Guid Id { get; init; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; init; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>What the customer's tool downloads: the ready-to-write SKILL.md for each active skill.</summary>
public sealed record EditVideoSkillFile(string Slug, string Title, string? Category, int Version, DateTimeOffset UpdatedAt, string Markdown);

/// <summary>
/// TodoX skill library (editvideo.skills). Admins write skills in the dashboard; every activated tool downloads the
/// active ones and installs them into Claude / Codex. Skill names always start with "todox-".
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
            SELECT id, slug, title, description, category, content, is_active AS IsActive, version, sort_order AS SortOrder, updated_at AS UpdatedAt
              FROM editvideo.skills
             WHERE tenant_id = @tenantId AND (@activeOnly = false OR is_active)
             ORDER BY sort_order, title;
            """,
            new { tenantId = _tenant.TenantId, activeOnly });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<EditVideoSkillFile>> ListFilesAsync(CancellationToken ct = default) =>
        (await ListAsync(activeOnly: true, ct))
            .Select(s => new EditVideoSkillFile(s.Slug, s.Title, s.Category, s.Version, s.UpdatedAt, BuildMarkdown(s)))
            .ToList();

    /// <summary>Inserts or updates a skill; the version goes up whenever what the customer receives changes.</summary>
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
                   SET version = version + CASE WHEN (slug, title, description, content, is_active)
                                                 IS DISTINCT FROM (@slug, @title, @description, @content, @isActive) THEN 1 ELSE 0 END,
                       slug = @slug, title = @title, description = @description, category = @category, content = @content,
                       is_active = @isActive, sort_order = @sortOrder, updated_by = @actor, updated_at = now()
                 WHERE id = @id AND tenant_id = @tenantId
                RETURNING id;
                """, args);

        var saved = (await ListAsync(activeOnly: false, ct)).FirstOrDefault(s => s.Id == id);
        return saved is null
            ? EditVideoResult<EditVideoSkillRow>.Fail("not_found", "Không tìm thấy skill.")
            : EditVideoResult<EditVideoSkillRow>.Success(saved);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM editvideo.skills WHERE id = @id AND tenant_id = @tenantId;", new { id, tenantId = _tenant.TenantId });
    }

    /// <summary>SKILL.md as installed on the customer's machine: front matter (name, description) + body.</summary>
    public static string BuildMarkdown(EditVideoSkillRow skill)
    {
        var md = new StringBuilder();
        md.Append("---\n");
        md.Append("name: ").Append(skill.Slug).Append('\n');
        // A JSON string is valid YAML and keeps quotes, colons and line breaks in the description safe.
        md.Append("description: ").Append(JsonSerializer.Serialize(OneLine(skill.Description), YamlSafeJson)).Append('\n');
        md.Append("---\n\n");
        md.Append("# ").Append(skill.Title.Trim()).Append("\n\n");
        md.Append(skill.Content.Replace("\r\n", "\n").Trim()).Append('\n');
        return md.ToString();
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
