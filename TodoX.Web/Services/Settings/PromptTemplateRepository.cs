using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Settings;

public sealed class PromptTemplateDto
{
    public Guid Id { get; set; }
    public string PromptCode { get; set; } = string.Empty;
    public string PromptName { get; set; } = string.Empty;
    public string PromptGroup { get; set; } = "image";
    public string PromptType { get; set; } = "avatar_chibi";
    public string LanguageCode { get; set; } = "vi";
    public int VersionNo { get; set; }
    public string TemplateText { get; set; } = string.Empty;
    public string? TemplateJson { get; set; }
    public string? NegativePrompt { get; set; }
    public string? Variables { get; set; }
    public string? ReferenceSchema { get; set; }
    public string? OutputSchema { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class PromptTemplateVersionDto
{
    public Guid Id { get; set; }
    public Guid PromptTemplateId { get; set; }
    public int VersionNo { get; set; }
    public string TemplateText { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PromptTemplateEditModel
{
    public Guid? Id { get; set; }
    public string PromptCode { get; set; } = string.Empty;
    public string PromptName { get; set; } = string.Empty;
    public string PromptGroup { get; set; } = "image";
    public string PromptType { get; set; } = "avatar_chibi";
    public string LanguageCode { get; set; } = "vi";
    public string TemplateText { get; set; } = string.Empty;
    public string? TemplateJson { get; set; }
    public string? NegativePrompt { get; set; }
    public string? Variables { get; set; }
    public string? ReferenceSchema { get; set; }
    public string? OutputSchema { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
    public string? ChangeNote { get; set; }
}

public sealed class PromptTemplateRepository
{
    private readonly TodoXConnectionFactory _factory;

    public PromptTemplateRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<PromptTemplateDto?> GetDefaultAsync(string promptType, string languageCode = "vi", CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PromptTemplateDto>(
            """
            SELECT id AS Id, prompt_code AS PromptCode, prompt_name AS PromptName,
                   prompt_group AS PromptGroup, prompt_type AS PromptType, language_code AS LanguageCode,
                   version_no AS VersionNo, template_text AS TemplateText, template_json::text AS TemplateJson,
                   negative_prompt AS NegativePrompt, variables::text AS Variables,
                   reference_schema::text AS ReferenceSchema, output_schema::text AS OutputSchema,
                   is_default AS IsDefault, is_active AS IsActive, is_system AS IsSystem,
                   description AS Description, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM settings.prompt_templates
             WHERE prompt_type=@promptType AND language_code=@languageCode AND is_active=true
             ORDER BY is_default DESC, updated_at DESC NULLS LAST, created_at DESC
             LIMIT 1;
            """, new { promptType, languageCode });
    }

    public async Task<PromptTemplateDto?> GetByCodeAsync(string promptCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PromptTemplateDto>(
            """
            SELECT id AS Id, prompt_code AS PromptCode, prompt_name AS PromptName,
                   prompt_group AS PromptGroup, prompt_type AS PromptType, language_code AS LanguageCode,
                   version_no AS VersionNo, template_text AS TemplateText, template_json::text AS TemplateJson,
                   negative_prompt AS NegativePrompt, variables::text AS Variables,
                   reference_schema::text AS ReferenceSchema, output_schema::text AS OutputSchema,
                   is_default AS IsDefault, is_active AS IsActive, is_system AS IsSystem,
                   description AS Description, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM settings.prompt_templates
             WHERE prompt_code=@promptCode
             LIMIT 1;
            """, new { promptCode });
    }

    public async Task<IReadOnlyList<PromptTemplateDto>> ListAsync(string? promptType = null, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<PromptTemplateDto>(
            """
            SELECT id AS Id, prompt_code AS PromptCode, prompt_name AS PromptName,
                   prompt_group AS PromptGroup, prompt_type AS PromptType, language_code AS LanguageCode,
                   version_no AS VersionNo, template_text AS TemplateText, template_json::text AS TemplateJson,
                   negative_prompt AS NegativePrompt, variables::text AS Variables,
                   reference_schema::text AS ReferenceSchema, output_schema::text AS OutputSchema,
                   is_default AS IsDefault, is_active AS IsActive, is_system AS IsSystem,
                   description AS Description, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM settings.prompt_templates
             WHERE (@promptType IS NULL OR prompt_type=@promptType)
             ORDER BY prompt_type, is_default DESC, prompt_name;
            """, new { promptType });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PromptTemplateVersionDto>> GetVersionsAsync(Guid promptTemplateId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<PromptTemplateVersionDto>(
            """
            SELECT id AS Id, prompt_template_id AS PromptTemplateId, version_no AS VersionNo,
                   template_text AS TemplateText, negative_prompt AS NegativePrompt,
                   change_note AS ChangeNote, created_at AS CreatedAt
              FROM settings.prompt_template_versions
             WHERE prompt_template_id=@promptTemplateId
             ORDER BY version_no DESC;
            """, new { promptTemplateId });
        return rows.ToList();
    }

    public async Task<PromptTemplateDto> SaveAsync(PromptTemplateEditModel model, Guid? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        if (model.IsDefault)
        {
            await conn.ExecuteAsync(
                """
                UPDATE settings.prompt_templates
                   SET is_default=false, updated_at=now(), updated_by=@userId
                 WHERE prompt_type=@promptType AND language_code=@languageCode
                   AND (@id IS NULL OR id<>@id);
                """, new { userId, model.PromptType, model.LanguageCode, id = model.Id }, tx);
        }

        PromptTemplateDto saved;
        if (model.Id is Guid id)
        {
            saved = await conn.QuerySingleAsync<PromptTemplateDto>(
                """
                UPDATE settings.prompt_templates
                   SET prompt_code=@PromptCode, prompt_name=@PromptName, prompt_group=@PromptGroup,
                       prompt_type=@PromptType, language_code=@LanguageCode, version_no=version_no+1,
                       template_text=@TemplateText, template_json=CAST(NULLIF(@TemplateJson, '') AS jsonb),
                       negative_prompt=@NegativePrompt, variables=COALESCE(CAST(NULLIF(@Variables, '') AS jsonb), '[]'::jsonb),
                       reference_schema=COALESCE(CAST(NULLIF(@ReferenceSchema, '') AS jsonb), '{}'::jsonb),
                       output_schema=COALESCE(CAST(NULLIF(@OutputSchema, '') AS jsonb), '{}'::jsonb),
                       is_default=@IsDefault, is_active=@IsActive, is_system=@IsSystem,
                       description=@Description, updated_at=now(), updated_by=@userId
                 WHERE id=@Id
                RETURNING id AS Id, prompt_code AS PromptCode, prompt_name AS PromptName,
                       prompt_group AS PromptGroup, prompt_type AS PromptType, language_code AS LanguageCode,
                       version_no AS VersionNo, template_text AS TemplateText, template_json::text AS TemplateJson,
                       negative_prompt AS NegativePrompt, variables::text AS Variables,
                       reference_schema::text AS ReferenceSchema, output_schema::text AS OutputSchema,
                       is_default AS IsDefault, is_active AS IsActive, is_system AS IsSystem,
                       description AS Description, created_at AS CreatedAt, updated_at AS UpdatedAt;
                """, new { model.Id, model.PromptCode, model.PromptName, model.PromptGroup, model.PromptType,
                    model.LanguageCode, model.TemplateText, model.TemplateJson, model.NegativePrompt,
                    model.Variables, model.ReferenceSchema, model.OutputSchema, model.IsDefault, model.IsActive,
                    model.IsSystem, model.Description, userId }, tx);
        }
        else
        {
            saved = await conn.QuerySingleAsync<PromptTemplateDto>(
                """
                INSERT INTO settings.prompt_templates
                    (prompt_code, prompt_name, prompt_group, prompt_type, language_code, version_no,
                     template_text, template_json, negative_prompt, variables, reference_schema, output_schema,
                     is_default, is_active, is_system, description, created_by)
                VALUES
                    (@PromptCode, @PromptName, @PromptGroup, @PromptType, @LanguageCode, 1,
                     @TemplateText, CAST(NULLIF(@TemplateJson, '') AS jsonb), @NegativePrompt,
                     COALESCE(CAST(NULLIF(@Variables, '') AS jsonb), '[]'::jsonb),
                     COALESCE(CAST(NULLIF(@ReferenceSchema, '') AS jsonb), '{}'::jsonb),
                     COALESCE(CAST(NULLIF(@OutputSchema, '') AS jsonb), '{}'::jsonb),
                     @IsDefault, @IsActive, @IsSystem, @Description, @userId)
                RETURNING id AS Id, prompt_code AS PromptCode, prompt_name AS PromptName,
                       prompt_group AS PromptGroup, prompt_type AS PromptType, language_code AS LanguageCode,
                       version_no AS VersionNo, template_text AS TemplateText, template_json::text AS TemplateJson,
                       negative_prompt AS NegativePrompt, variables::text AS Variables,
                       reference_schema::text AS ReferenceSchema, output_schema::text AS OutputSchema,
                       is_default AS IsDefault, is_active AS IsActive, is_system AS IsSystem,
                       description AS Description, created_at AS CreatedAt, updated_at AS UpdatedAt;
                """, new { model.PromptCode, model.PromptName, model.PromptGroup, model.PromptType,
                    model.LanguageCode, model.TemplateText, model.TemplateJson, model.NegativePrompt,
                    model.Variables, model.ReferenceSchema, model.OutputSchema, model.IsDefault, model.IsActive,
                    model.IsSystem, model.Description, userId }, tx);
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO settings.prompt_template_versions
                (prompt_template_id, version_no, template_text, template_json, negative_prompt,
                 variables, reference_schema, output_schema, change_note, created_by)
            VALUES
                (@id, @version, @templateText, CAST(NULLIF(@templateJson, '') AS jsonb), @negativePrompt,
                 COALESCE(CAST(NULLIF(@variables, '') AS jsonb), '[]'::jsonb),
                 COALESCE(CAST(NULLIF(@referenceSchema, '') AS jsonb), '{}'::jsonb),
                 COALESCE(CAST(NULLIF(@outputSchema, '') AS jsonb), '{}'::jsonb),
                 @changeNote, @userId)
            ON CONFLICT(prompt_template_id, version_no) DO NOTHING;
            """, new
            {
                id = saved.Id,
                version = saved.VersionNo,
                templateText = saved.TemplateText,
                templateJson = saved.TemplateJson,
                negativePrompt = saved.NegativePrompt,
                variables = saved.Variables,
                referenceSchema = saved.ReferenceSchema,
                outputSchema = saved.OutputSchema,
                changeNote = string.IsNullOrWhiteSpace(model.ChangeNote) ? "Admin update" : model.ChangeNote,
                userId
            }, tx);

        if (saved.IsDefault && saved.PromptType == "avatar_chibi")
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO settings.system_settings(setting_key, setting_value, setting_json, description, is_secret, is_active)
                VALUES ('avatar_chibi.default_prompt_template', @templateText, CAST(NULLIF(@templateJson, '') AS jsonb),
                        'Default editable prompt template for avatar chibi generation', false, true)
                ON CONFLICT(setting_key)
                DO UPDATE SET setting_value=EXCLUDED.setting_value, setting_json=EXCLUDED.setting_json,
                              is_active=true, updated_at=now();
                """, new { templateText = saved.TemplateText, templateJson = saved.TemplateJson }, tx);
        }

        tx.Commit();
        return saved;
    }
}
