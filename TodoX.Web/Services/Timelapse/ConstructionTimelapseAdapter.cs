using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public sealed record ConstructionTimelapseExecution(
    Guid LegacyJobId,
    TimelapseJobSnapshot Snapshot);

public sealed record ConstructionTimelapseMappedRequest(
    string ProfileCode,
    int SceneCount,
    string VideoMode,
    string Ratio,
    string Title,
    bool RequireVideoConfirmation,
    Guid OriginalImageMediaId);

public interface IConstructionTimelapseExecutionBridge
{
    Task<ConstructionTimelapseExecution> StartAsync(
        CoreJobDispatchContext context,
        CancellationToken ct = default);
}

public sealed class ConstructionTimelapseAdapter : ICoreJobExecutionAdapter
{
    public const string ConstructionServiceCode = "CONSTRUCTION_VIDEO";
    public const string AdapterCode = "construction_timelapse";
    public const string ExecutionSystem = "todox";

    private readonly IConstructionTimelapseExecutionBridge _bridge;

    public ConstructionTimelapseAdapter(IConstructionTimelapseExecutionBridge bridge)
    {
        _bridge = bridge;
    }

    public string ServiceCode => ConstructionServiceCode;

    public async Task<CoreExecutionResult> DispatchAsync(
        CoreJobDispatchContext context,
        CancellationToken ct = default)
    {
        if (!string.Equals(context.ServiceCode, ServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Construction Timelapse adapter cannot handle service '{context.ServiceCode}'.");
        }

        var execution = await _bridge.StartAsync(context, ct);
        return CoreExecutionResult.Deferred(
            ExecutionSystem,
            execution.LegacyJobId.ToString(),
            AdapterCode,
            "Construction Timelapse execution started.",
            JsonSerializer.SerializeToElement(new
            {
                core_job_id = context.CoreJobId,
                legacy_job_id = execution.LegacyJobId,
                legacy_job_uuid = execution.LegacyJobId,
                service_code = ServiceCode
            }));
    }
}

public sealed class ConstructionTimelapseExecutionBridge : IConstructionTimelapseExecutionBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ITimelapseProfileRepository _profiles;
    private readonly ITimelapseWorkflowService _workflow;
    private readonly ITimelapseCoreLifecycleBridge _coreLifecycle;

    public ConstructionTimelapseExecutionBridge(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ITimelapseProfileRepository profiles,
        ITimelapseWorkflowService workflow,
        ITimelapseCoreLifecycleBridge coreLifecycle)
    {
        _factory = factory;
        _tenant = tenant;
        _profiles = profiles;
        _workflow = workflow;
        _coreLifecycle = coreLifecycle;
    }

    public async Task<ConstructionTimelapseExecution> StartAsync(
        CoreJobDispatchContext context,
        CancellationToken ct = default)
    {
        var request = MapRequest(context);
        if (context.RequestContext.CustomerId is not Guid customerId)
        {
            throw new InvalidOperationException(
                "Construction Timelapse requires a resolved customer identity.");
        }

        var profile = await _profiles.GetEnabledProfileByCategoryAsync(
            request.ProfileCode,
            TimelapseServiceCatalog.ConstructionCategory,
            ct)
            ?? throw new InvalidOperationException(
                "TIMELAPSE_PROFILE_SERVICE_MISMATCH: Construction Timelapse profile is invalid, disabled, or outside the construction category.");

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var media = await RequireOwnedMediaAsync(
            conn,
            request.OriginalImageMediaId,
            customerId,
            context.RequestContext.UserId,
            ct);

        TimelapseJobSnapshot snapshot;
        Guid legacyJobId;
        using (var tx = conn.BeginTransaction())
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = $"construction-timelapse-core:{context.CoreJobId:N}" },
                tx,
                cancellationToken: ct));

            var existing = await conn.QuerySingleOrDefaultAsync<LegacyJobRow>(new CommandDefinition(
                """
                SELECT id AS Id,
                       input_json::text AS InputJson
                  FROM render.render_jobs
                 WHERE tenant_id=@tenant
                   AND job_type=@jobType
                   AND input_json->>'coreJobId'=@coreJobId
                 ORDER BY created_at
                 LIMIT 1;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    jobType = RenderJobTypes.Timelapse,
                    coreJobId = context.CoreJobId.ToString()
                },
                tx,
                cancellationToken: ct));

            if (existing is not null)
            {
                legacyJobId = existing.Id;
                snapshot = DeserializeSnapshot(existing.InputJson);
                tx.Commit();
            }
            else
            {
                legacyJobId = Guid.NewGuid();
                snapshot = BuildSnapshot(context, request, profile, media);
                var inputJson = JsonSerializer.Serialize(snapshot, JsonOptions);
                var promptJson = context.Prompt?.GetRawText() ?? "{}";
                var referenceJson = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        role = "original_image",
                        mediaId = media.Id,
                        media.ObjectKey,
                        url = media.PublicUrl,
                        media.MimeType
                    }
                }, JsonOptions);
                var optionsJson = JsonSerializer.Serialize(new
                {
                    core_bridge = new
                    {
                        core_job_id = context.CoreJobId,
                        service_code = ConstructionTimelapseAdapter.ConstructionServiceCode,
                        adapter = ConstructionTimelapseAdapter.AdapterCode
                    }
                }, JsonOptions);

                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO render.render_jobs
                        (id, tenant_id, customer_id, user_id, service_id,
                         logical_request_id, job_type, operation_type, source_type,
                         status, current_step, progress_percent, priority,
                         input_json, prompt_json, reference_json, output_json, options,
                         point_cost_estimate, point_cost_charged, point_status,
                         max_attempts, queued_at, created_at)
                    VALUES
                        (@id, @tenant, @customerId, @userId, @serviceId,
                         @logicalRequestId, @jobType, @operationType, @sourceType,
                         @status, 'draft', 0, 100,
                         CAST(@inputJson AS jsonb), CAST(@promptJson AS jsonb), CAST(@referenceJson AS jsonb),
                         '[]'::jsonb, CAST(@optionsJson AS jsonb),
                         0, 0, @pointStatus,
                         1, now(), now());

                    INSERT INTO render.render_job_events
                        (job_id, tenant_id, event_type, level, message, data_json, created_at)
                    VALUES
                        (@id, @tenant, 'TIMELAPSE_CORE_BRIDGE_CREATED', 'info',
                         'Legacy Timelapse execution created for canonical Core job.',
                         jsonb_build_object('core_job_id', @coreJobId, 'service_code', @serviceCode),
                         now());
                    """,
                    new
                    {
                        id = legacyJobId,
                        tenant = _tenant.TenantId,
                        customerId,
                        userId = context.RequestContext.UserId,
                        serviceId = context.ServiceId,
                        logicalRequestId = $"core-legacy:{context.CoreJobId:N}",
                        jobType = RenderJobTypes.Timelapse,
                        operationType = TodoXServiceEngineTypes.Timelapse,
                        sourceType = context.RequestContext.NormalizedChannel,
                        status = TimelapseParentStatuses.Draft,
                        inputJson,
                        promptJson,
                        referenceJson,
                        optionsJson,
                        pointStatus = RenderPointStatuses.NotRequired,
                        coreJobId = context.CoreJobId,
                        serviceCode = ConstructionTimelapseAdapter.ConstructionServiceCode
                    },
                    tx,
                    cancellationToken: ct));
                tx.Commit();
            }
        }

        await _workflow.StartOrResumeAsync(
            legacyJobId,
            snapshot,
            BuildLegacyCustomerSession(context.RequestContext),
            ct);
        await _coreLifecycle.ReportProgressAsync(
            snapshot,
            "image_generation",
            20,
            "Construction Timelapse image generation started.",
            ct);
        return new ConstructionTimelapseExecution(legacyJobId, snapshot);
    }

    internal static ConstructionTimelapseMappedRequest MapRequest(CoreJobDispatchContext context)
    {
        if (!string.Equals(
                context.ServiceCode,
                ConstructionTimelapseAdapter.ConstructionServiceCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported Construction Timelapse service code.");
        }

        var input = RequireObject(context.Input);
        var profileCode = ReadString(input, "profileCode", "profile_code", "category", "categoryCode", "category_code");
        var sceneCount = ReadInt(input, "sceneCount", "scene_count");
        var videoMode = NormalizeVideoMode(
            ReadString(input, "videoMode", "video_mode", "qualityTier", "quality_tier", "quality"));
        var ratio = NormalizeRatio(ReadString(input, "ratio", "aspectRatio", "aspect_ratio"));
        var duration = ReadInt(input, "durationSeconds", "duration_seconds", "sceneDurationSeconds", "scene_duration_seconds");
        var mediaId = ReadGuid(
            input,
            "originalImageMediaId",
            "original_image_media_id",
            "sourceImageMediaId",
            "source_image_media_id",
            "sourceImage",
            "source_image",
            "imageMediaId",
            "image_media_id",
            "mediaId",
            "media_id")
            ?? ReadReferenceMediaId(context.References)
            ?? throw new InvalidOperationException(
                "Construction Timelapse requires an existing original image media id.");

        if (string.IsNullOrWhiteSpace(profileCode))
        {
            throw new InvalidOperationException("Construction Timelapse profileCode is required.");
        }

        if (sceneCount is null || !TimelapseRequestRules.AllowedSceneCounts.Contains(sceneCount.Value))
        {
            throw new InvalidOperationException("Construction Timelapse sceneCount must be 3, 4, 5 or 6.");
        }

        if (duration.HasValue && duration.Value != TimelapseRequestRules.RuntimeClipDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Construction Timelapse scene duration must remain {TimelapseRequestRules.RuntimeClipDurationSeconds} seconds.");
        }

        var title = ReadString(input, "title", "name");
        var confirmation = ReadBool(input, "requireVideoConfirmation", "require_video_confirmation") ?? false;
        return new ConstructionTimelapseMappedRequest(
            profileCode.Trim(),
            sceneCount.Value,
            videoMode,
            ratio,
            string.IsNullOrWhiteSpace(title) ? "Video Timelapse" : title.Trim(),
            confirmation,
            mediaId);
    }

    private TimelapseJobSnapshot BuildSnapshot(
        CoreJobDispatchContext context,
        ConstructionTimelapseMappedRequest request,
        TimelapseProfileDto profile,
        LegacyMediaRow media)
        => new()
        {
            CoreJobId = context.CoreJobId,
            ServiceId = context.ServiceId,
            ServiceCode = ConstructionTimelapseAdapter.ConstructionServiceCode,
            ServiceName = "Xây dựng & Công trình",
            ServiceCategory = TimelapseServiceCatalog.ConstructionCategory,
            ProfileCode = profile.ProfileCode,
            ProfileName = profile.ProfileName,
            SceneCount = request.SceneCount,
            ProgressMapping = TimelapseRequestRules.GetProgressMapping(request.SceneCount),
            VideoMode = request.VideoMode,
            Ratio = request.Ratio,
            Title = request.Title,
            RequireVideoConfirmation = request.RequireVideoConfirmation,
            OriginalImage = new TimelapseOriginalImageSnapshot
            {
                MediaId = media.Id,
                ObjectKey = media.ObjectKey,
                PublicUrl = media.PublicUrl,
                MimeType = media.MimeType
            }
        };

    private async Task<LegacyMediaRow> RequireOwnedMediaAsync(
        IDbConnection conn,
        Guid mediaId,
        Guid customerId,
        Guid? userId,
        CancellationToken ct)
    {
        var media = await conn.QuerySingleOrDefaultAsync<LegacyMediaRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   object_key AS ObjectKey,
                   COALESCE(public_url, file_url) AS PublicUrl,
                   mime_type AS MimeType
              FROM media.media_files
             WHERE id=@mediaId
               AND tenant_id=@tenant
               AND is_active=true
               AND (
                    customer_id=@customerId
                    OR (customer_id IS NULL AND @userId IS NOT NULL AND user_id=@userId)
               )
             LIMIT 1;
            """,
            new
            {
                mediaId,
                tenant = _tenant.TenantId,
                customerId,
                userId
            },
            cancellationToken: ct));

        if (media is null)
        {
            throw new UnauthorizedAccessException(
                "Construction Timelapse original image is missing or outside the caller scope.");
        }

        if (string.IsNullOrWhiteSpace(media.PublicUrl)
            || string.IsNullOrWhiteSpace(media.MimeType)
            || !media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Construction Timelapse original media must be an active image with a public URL.");
        }

        return media;
    }

    private static CurrentUserSession BuildLegacyCustomerSession(CoreRequestContext context)
        => new()
        {
            UserId = context.UserId ?? Guid.Empty,
            CustomerId = context.CustomerId,
            Role = TodoXUserRole.CustomerUser,
            IsAuthenticated = true,
            DisplayName = $"Core {context.NormalizedChannel}"
        };

    private static JsonElement RequireObject(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException("Construction Timelapse input must be a JSON object.");

    private static string NormalizeVideoMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "standard" or TimelapseRequestRules.FastMode => TimelapseRequestRules.FastMode,
            "premium" or TimelapseRequestRules.ProfessionalMode => TimelapseRequestRules.ProfessionalMode,
            _ => throw new InvalidOperationException(
                "Construction Timelapse videoMode must be fast/professional or standard/premium.")
        };

    private static string NormalizeRatio(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "16:9" or "16_9" => TimelapseRequestRules.LandscapeRatio,
            "9:16" or "9_16" => TimelapseRequestRules.PortraitRatio,
            _ => throw new InvalidOperationException("Construction Timelapse ratio must be 16:9 or 9:16.")
        };

    private static string? ReadString(JsonElement input, params string[] names)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement input, params string[] names)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && int.TryParse(property.Value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement input, params string[] names)
    {
        foreach (var property in input.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.Value.GetBoolean();
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && bool.TryParse(property.Value.GetString(), out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static Guid? ReadGuid(JsonElement input, params string[] names)
    {
        var value = ReadString(input, names);
        return Guid.TryParse(value, out var result) ? result : null;
    }

    private static Guid? ReadReferenceMediaId(JsonElement? references)
    {
        if (references is null || references.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var items = references.Value.ValueKind == JsonValueKind.Array
            ? references.Value.EnumerateArray()
            : new[] { references.Value }.AsEnumerable();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = ReadString(item, "role", "type")?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(role)
                && role is not ("original_image" or "input_image" or "image"))
            {
                continue;
            }

            var mediaId = ReadGuid(item, "mediaId", "media_id", "id");
            if (mediaId.HasValue)
            {
                return mediaId;
            }
        }

        return null;
    }

    private static TimelapseJobSnapshot DeserializeSnapshot(string json)
        => JsonSerializer.Deserialize<TimelapseJobSnapshot>(json, JsonOptions)
           ?? throw new InvalidOperationException("Existing Construction Timelapse snapshot is invalid.");

    private sealed class LegacyJobRow
    {
        public Guid Id { get; init; }
        public string InputJson { get; init; } = "{}";
    }

    private sealed class LegacyMediaRow
    {
        public Guid Id { get; init; }
        public string? ObjectKey { get; init; }
        public string? PublicUrl { get; init; }
        public string? MimeType { get; init; }
    }
}
