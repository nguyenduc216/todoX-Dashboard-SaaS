using Dapper;
using System.Text.Json;
using TodoX.Web.Data;

namespace TodoX.Web.Services.PromptAssistant;

public sealed class ServicePromptAssistantRepository
{
    private readonly TodoXConnectionFactory _factory;

    public ServicePromptAssistantRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ServicePromptServiceContext?> GetServiceContextAsync(Guid serviceId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ServicePromptServiceContext>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_code AS ServiceCode, service_name AS ServiceName,
                       service_type AS ServiceType, description AS Description
                  FROM catalog.services
                 WHERE id=@serviceId
                 LIMIT 1;
                """,
                new { serviceId },
                cancellationToken: ct));
    }

    public async Task<ServicePromptAssistantDto> GetOrCreateAssistantAsync(Guid serviceId, ServicePromptAssistantOptions options, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var existing = await conn.QuerySingleOrDefaultAsync<ServicePromptAssistantDto>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_id AS ServiceId, enabled AS Enabled,
                       provider_code AS ProviderCode, model_code AS ModelCode,
                       temperature AS Temperature, max_tokens AS MaxTokens,
                       max_repair_attempts AS MaxRepairAttempts,
                       active_training_version_id AS ActiveTrainingVersionId
                  FROM settings.service_prompt_assistants
                 WHERE service_id=@serviceId
                 LIMIT 1;
                """,
                new { serviceId },
                cancellationToken: ct));
        if (existing is not null)
        {
            return existing;
        }

        return await conn.QuerySingleAsync<ServicePromptAssistantDto>(
            new CommandDefinition(
                """
                INSERT INTO settings.service_prompt_assistants
                    (id, service_id, enabled, provider_code, model_code, max_repair_attempts)
                VALUES
                    (gen_random_uuid(), @serviceId, @enabled, @provider, '', 1)
                RETURNING id AS Id, service_id AS ServiceId, enabled AS Enabled,
                          provider_code AS ProviderCode, model_code AS ModelCode,
                          temperature AS Temperature, max_tokens AS MaxTokens,
                          max_repair_attempts AS MaxRepairAttempts,
                          active_training_version_id AS ActiveTrainingVersionId;
                """,
                new
                {
                    serviceId,
                    enabled = options.EnabledByDefault,
                    provider = string.IsNullOrWhiteSpace(options.ProviderCode) ? "79ai" : options.ProviderCode.Trim()
                },
                cancellationToken: ct));
    }

    public async Task<ServicePromptAssistantDto> SaveAssistantAsync(
        ServicePromptAssistantSaveRequest request,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleAsync<ServicePromptAssistantDto>(
            new CommandDefinition(
                """
                INSERT INTO settings.service_prompt_assistants
                    (id, service_id, enabled, provider_code, model_code, temperature, max_tokens, max_repair_attempts)
                VALUES
                    (gen_random_uuid(), @ServiceId, @Enabled, @ProviderCode, @ModelCode, @Temperature, @MaxTokens, @MaxRepairAttempts)
                ON CONFLICT (service_id)
                DO UPDATE SET enabled=EXCLUDED.enabled,
                              provider_code=EXCLUDED.provider_code,
                              model_code=EXCLUDED.model_code,
                              temperature=EXCLUDED.temperature,
                              max_tokens=EXCLUDED.max_tokens,
                              max_repair_attempts=EXCLUDED.max_repair_attempts,
                              updated_at=now()
                RETURNING id AS Id, service_id AS ServiceId, enabled AS Enabled,
                          provider_code AS ProviderCode, model_code AS ModelCode,
                          temperature AS Temperature, max_tokens AS MaxTokens,
                          max_repair_attempts AS MaxRepairAttempts,
                          active_training_version_id AS ActiveTrainingVersionId;
                """,
                new
                {
                    request.ServiceId,
                    request.Enabled,
                    ProviderCode = request.ProviderCode.Trim(),
                    ModelCode = request.ModelCode.Trim(),
                    request.Temperature,
                    request.MaxTokens,
                    MaxRepairAttempts = Math.Clamp(request.MaxRepairAttempts, 0, 3)
                },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ServicePromptTrainingVersionDto>> GetTrainingVersionsAsync(Guid assistantId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ServicePromptTrainingVersionDto>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_prompt_assistant_id AS ServicePromptAssistantId,
                       version_no AS VersionNo, version_name AS VersionName, status AS Status,
                       template_file_name AS TemplateFileName, template_json::text AS TemplateJson,
                       description_file_name AS DescriptionFileName, description_content AS DescriptionContent,
                       created_by AS CreatedBy, created_at AS CreatedAt, published_at AS PublishedAt
                  FROM settings.service_prompt_training_versions
                 WHERE service_prompt_assistant_id=@assistantId
                 ORDER BY version_no DESC;
                """,
                new { assistantId },
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ServicePromptGenerationDto>> GetGenerationsAsync(Guid serviceId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ServicePromptGenerationRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_id AS ServiceId, service_prompt_assistant_id AS ServicePromptAssistantId,
                       training_version_id AS TrainingVersionId, user_input AS UserInput,
                       provider_code AS ProviderCode, model_code AS ModelCode, generated_json AS GeneratedJson,
                       validation_status AS ValidationStatus, validation_errors_json::text AS ValidationErrorsJson,
                       repair_attempt_count AS RepairAttemptCount, prompt_tokens AS PromptTokens,
                       completion_tokens AS CompletionTokens, total_tokens AS TotalTokens,
                       status AS Status, error_code AS ErrorCode, error_message AS ErrorMessage,
                       created_at AS CreatedAt,
                       EXTRACT(EPOCH FROM (completed_at - created_at)) AS LatencySeconds
                  FROM settings.service_prompt_generations
                 WHERE service_id=@serviceId
                 ORDER BY created_at DESC
                 LIMIT 50;
                """,
                new { serviceId },
                cancellationToken: ct));
        return rows.Select(MapGeneration).ToList();
    }

    public async Task<Guid> SaveDraftAsync(
        Guid serviceId,
        ServicePromptTrainingSaveRequest request,
        Guid? createdBy,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var assistantId = await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                SELECT id FROM settings.service_prompt_assistants WHERE service_id=@serviceId;
                """,
                new { serviceId },
                tx,
                cancellationToken: ct));
        var versionNo = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COALESCE(max(version_no), 0) + 1
                  FROM settings.service_prompt_training_versions
                 WHERE service_prompt_assistant_id=@assistantId;
                """,
                new { assistantId },
                tx,
                cancellationToken: ct));
        var id = await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO settings.service_prompt_training_versions
                    (id, service_prompt_assistant_id, version_no, version_name, status,
                     template_file_name, template_json, description_file_name, description_content, created_by)
                VALUES
                    (gen_random_uuid(), @assistantId, @versionNo, @versionName, 'DRAFT',
                     @templateFileName, CAST(@templateJson AS jsonb), @descriptionFileName, @descriptionContent, @createdBy)
                RETURNING id;
                """,
                new
                {
                    assistantId,
                    versionNo,
                    versionName = request.VersionName.Trim(),
                    templateFileName = request.TemplateFileName.Trim(),
                    templateJson = request.TemplateJson,
                    descriptionFileName = request.DescriptionFileName.Trim(),
                    descriptionContent = request.DescriptionContent,
                    createdBy
                },
                tx,
                cancellationToken: ct));
        tx.Commit();
        return id;
    }

    public async Task PublishAsync(Guid serviceId, Guid versionId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var assistantId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT id FROM settings.service_prompt_assistants WHERE service_id=@serviceId LIMIT 1;",
                new { serviceId },
                tx,
                cancellationToken: ct));
        if (assistantId is null)
        {
            throw new ServicePromptDomainException("assistant_not_found", "Prompt Assistant was not found for this service.");
        }

        var version = await conn.QuerySingleOrDefaultAsync<ServicePromptPublishCandidate>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_prompt_assistant_id AS AssistantId, status AS Status
                  FROM settings.service_prompt_training_versions
                 WHERE id=@versionId
                 LIMIT 1;
                """,
                new { versionId },
                tx,
                cancellationToken: ct));
        if (version is null)
        {
            throw new ServicePromptDomainException("version_not_found", "Training version was not found.");
        }

        if (version.AssistantId != assistantId.Value)
        {
            throw new ServicePromptDomainException("version_service_mismatch", "Training version does not belong to this service.");
        }

        if (!string.Equals(version.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ServicePromptDomainException("version_not_draft", "Only a draft training version can be published.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE settings.service_prompt_training_versions
               SET status='ARCHIVED'
             WHERE service_prompt_assistant_id=@assistantId AND status='PUBLISHED' AND id<>@versionId;
            """,
            new { assistantId, versionId },
            tx,
            cancellationToken: ct));

        var publishedRows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE settings.service_prompt_training_versions
               SET status='PUBLISHED', published_at=now()
             WHERE id=@versionId AND service_prompt_assistant_id=@assistantId AND status='DRAFT';
            """,
            new { assistantId, versionId },
            tx,
            cancellationToken: ct));
        if (publishedRows != 1)
        {
            throw new ServicePromptDomainException("publish_failed", "Training version could not be published.");
        }

        var assistantRows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE settings.service_prompt_assistants
               SET active_training_version_id=@versionId, updated_at=now()
             WHERE id=@assistantId AND service_id=@serviceId;
            """,
            new { assistantId, serviceId, versionId },
            tx,
            cancellationToken: ct));
        if (assistantRows != 1)
        {
            throw new ServicePromptDomainException("assistant_update_failed", "Prompt Assistant active version could not be updated.");
        }

        tx.Commit();
    }

    public async Task ArchiveAsync(Guid serviceId, Guid versionId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE settings.service_prompt_training_versions v
               SET status='ARCHIVED'
              FROM settings.service_prompt_assistants a
             WHERE v.id=@versionId AND v.service_prompt_assistant_id=a.id
               AND a.service_id=@serviceId AND v.status='DRAFT';
            """,
            new { serviceId, versionId },
            cancellationToken: ct));
    }

    public async Task<ServicePromptTrainingVersionDto?> GetPublishedAsync(Guid assistantId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ServicePromptTrainingVersionDto>(
            new CommandDefinition(
                """
                SELECT id AS Id, service_prompt_assistant_id AS ServicePromptAssistantId,
                       version_no AS VersionNo, version_name AS VersionName, status AS Status,
                       template_file_name AS TemplateFileName, template_json::text AS TemplateJson,
                       description_file_name AS DescriptionFileName, description_content AS DescriptionContent,
                       created_by AS CreatedBy, created_at AS CreatedAt, published_at AS PublishedAt
                  FROM settings.service_prompt_training_versions
                 WHERE service_prompt_assistant_id=@assistantId AND status='PUBLISHED'
                 ORDER BY published_at DESC NULLS LAST
                 LIMIT 1;
                """,
                new { assistantId },
                cancellationToken: ct));
    }

    public async Task SaveGenerationAsync(
        ServicePromptGenerationPersistence model,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO settings.service_prompt_generations
                (id, service_id, service_prompt_assistant_id, training_version_id, user_id, customer_id,
                 provider_code, model_code, user_input, request_snapshot_sanitized, raw_response_sanitized,
                 generated_json, validation_status, validation_errors_json, repair_attempt_count,
                 prompt_tokens, completion_tokens, total_tokens, status, error_code, error_message,
                 created_at, completed_at)
            VALUES
                (@Id, @ServiceId, @AssistantId, @TrainingVersionId, @UserId, @CustomerId,
                 @ProviderCode, @ModelCode, @UserInput, @RequestSnapshot, @RawResponse,
                 @GeneratedJson, @ValidationStatus, CAST(@ValidationErrors AS jsonb), @RepairAttempts,
                 @PromptTokens, @CompletionTokens, @TotalTokens, @Status, @ErrorCode, @ErrorMessage,
                 @CreatedAt, @CompletedAt);
            """,
            model,
            cancellationToken: ct));
    }

    private static ServicePromptGenerationDto MapGeneration(ServicePromptGenerationRow row)
        => new()
        {
            Id = row.Id,
            ServiceId = row.ServiceId,
            ServicePromptAssistantId = row.ServicePromptAssistantId,
            TrainingVersionId = row.TrainingVersionId,
            UserInput = row.UserInput,
            ProviderCode = row.ProviderCode,
            ModelCode = row.ModelCode,
            GeneratedJson = row.GeneratedJson,
            ValidationPassed = string.Equals(row.ValidationStatus, "PASS", StringComparison.OrdinalIgnoreCase),
            ValidationErrors = ParseErrors(row.ValidationErrorsJson),
            RepairAttemptCount = row.RepairAttemptCount,
            PromptTokens = row.PromptTokens,
            CompletionTokens = row.CompletionTokens,
            TotalTokens = row.TotalTokens,
            Status = Enum.TryParse<ServicePromptGenerationStatus>(row.Status, true, out var status)
                ? status
                : ServicePromptGenerationStatus.ProviderFailed,
            ErrorCode = row.ErrorCode,
            ErrorMessage = row.ErrorMessage,
            CreatedAt = row.CreatedAt,
            Latency = row.LatencySeconds is null ? null : TimeSpan.FromSeconds((double)row.LatencySeconds)
        };

    private static IReadOnlyList<ServicePromptValidationError> ParseErrors(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<ServicePromptValidationError>>(json, ServicePromptJson.Options) ?? [];

    private sealed class ServicePromptGenerationRow
    {
        public Guid Id { get; set; }
        public Guid ServiceId { get; set; }
        public Guid ServicePromptAssistantId { get; set; }
        public Guid TrainingVersionId { get; set; }
        public string UserInput { get; set; } = string.Empty;
        public string ProviderCode { get; set; } = string.Empty;
        public string ModelCode { get; set; } = string.Empty;
        public string? GeneratedJson { get; set; }
        public string ValidationStatus { get; set; } = string.Empty;
        public string? ValidationErrorsJson { get; set; }
        public int RepairAttemptCount { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal? LatencySeconds { get; set; }
    }

    private sealed class ServicePromptPublishCandidate
    {
        public Guid Id { get; set; }
        public Guid AssistantId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

public sealed class ServicePromptGenerationPersistence
{
    public Guid Id { get; init; }
    public Guid ServiceId { get; init; }
    public Guid AssistantId { get; init; }
    public Guid TrainingVersionId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? CustomerId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ModelCode { get; init; } = string.Empty;
    public string UserInput { get; init; } = string.Empty;
    public string RequestSnapshot { get; init; } = "{}";
    public string? RawResponse { get; init; }
    public string? GeneratedJson { get; init; }
    public string ValidationStatus { get; init; } = "FAIL";
    public string ValidationErrors { get; init; } = "[]";
    public int RepairAttempts { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    public string Status { get; init; } = "ProviderFailed";
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
