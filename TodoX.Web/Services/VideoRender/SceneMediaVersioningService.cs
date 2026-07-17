using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public static class SceneMediaVersioningFlags
{
    public const string SceneImages = "features.scene_render_versioning";
    public const string SceneVideos = "features.scene_video_versioning";
    public const string FinalVideos = "features.final_video_versioning";
}

public sealed record SceneImageVersionCreateRequest(
    long ProjectId,
    long SceneId,
    Guid? UserId,
    Guid? CustomerId,
    Guid? RenderJobId,
    string LogicalRequestId,
    string? ImagePromptSnapshot,
    string? CompiledImagePromptSnapshot,
    string? VideoPromptSnapshot,
    string? NegativePromptSnapshot,
    object SceneSnapshot,
    object ReferenceSnapshot,
    object RenderConfigSnapshot);

public sealed record SceneImageVersionCompleteRequest(
    string? ImageUrl,
    string? ObjectKey,
    string? ProviderCode,
    string? ModelName,
    long? ProviderCapabilityId,
    string? ProviderTaskId,
    Guid? ResultMediaId,
    string? BillingLogicalRequestId,
    decimal? EstimatedUsd,
    decimal? ActualUsd,
    decimal ChargedPoints,
    decimal RefundedPoints,
    string? ProviderUsageJson,
    string? MimeType,
    string? CostSource);

public sealed record SceneVideoVersionCreateRequest(
    long ProjectId,
    long SceneId,
    Guid? SourceImageVersionId,
    Guid? UserId,
    Guid? CustomerId,
    Guid? RenderJobId,
    string LogicalRequestId,
    string? ImagePromptSnapshot,
    string? VideoPromptSnapshot,
    object SceneSnapshot,
    object RenderConfigSnapshot);

public sealed record SceneVideoVersionCompleteRequest(
    string? VideoUrl,
    string? VideoPath,
    string? PosterUrl,
    decimal? DurationSeconds,
    string? MimeType,
    string? ProviderCode = null,
    string? ModelName = null,
    long? ProviderCapabilityId = null,
    string? ProviderTaskId = null,
    string? BillingLogicalRequestId = null,
    decimal? EstimatedUsd = null,
    decimal? ActualUsd = null,
    decimal ChargedPoints = 0,
    decimal RefundedPoints = 0,
    string? CostSource = null,
    string? AspectRatio = null);

public sealed record FinalVideoVersionCreateRequest(
    long ProjectId,
    Guid? UserId,
    Guid? CustomerId,
    Guid? RenderJobId,
    string LogicalRequestId,
    object CompositionConfigSnapshot,
    object TransitionConfigSnapshot,
    object AudioConfigSnapshot,
    object SubtitleConfigSnapshot);

public sealed record FinalVideoVersionCompleteRequest(
    string? VideoUrl,
    string? VideoPath,
    string? PosterUrl,
    decimal? DurationSeconds,
    string? MimeType);

public sealed class SceneImageVersionDto
{
    public Guid Id { get; set; }
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int VersionNumber { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? StorageKey { get; set; }
    public string? PublicUrl { get; set; }
    public string? SourceFilePath { get; set; }
    public Guid? ResultMediaId { get; set; }
    public string? ImagePromptSnapshot { get; set; }
    public string? CompiledImagePromptSnapshot { get; set; }
    public string? VideoPromptSnapshot { get; set; }
    public string? ProviderCode { get; set; }
    public string? ModelName { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? BillingLogicalRequestId { get; set; }
    public decimal? EstimatedUsd { get; set; }
    public decimal? ActualUsd { get; set; }
    public decimal ChargedPoints { get; set; }
    public decimal RefundedPoints { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SceneVideoVersionDto
{
    public Guid Id { get; set; }
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public Guid? SourceImageVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? StorageKey { get; set; }
    public string? PublicUrl { get; set; }
    public string? SourceFilePath { get; set; }
    public string? ImagePromptSnapshot { get; set; }
    public string? VideoPromptSnapshot { get; set; }
    public string? ProviderCode { get; set; }
    public string? ModelName { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public string? ProviderTaskId { get; set; }
    public decimal? DurationSeconds { get; set; }
    public string? AspectRatio { get; set; }
    public string? BillingLogicalRequestId { get; set; }
    public decimal? EstimatedUsd { get; set; }
    public decimal? ActualUsd { get; set; }
    public decimal ChargedPoints { get; set; }
    public decimal RefundedPoints { get; set; }
    public string? CostSource { get; set; }
    public string? PosterUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinalVideoVersionDto
{
    public Guid Id { get; set; }
    public long ProjectId { get; set; }
    public int VersionNumber { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? StorageKey { get; set; }
    public string? PublicUrl { get; set; }
    public string? SourceFilePath { get; set; }
    public decimal? DurationSeconds { get; set; }
    public string? PosterUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinalVideoVersionItemDto
{
    public Guid Id { get; set; }
    public Guid FinalVideoVersionId { get; set; }
    public long SceneId { get; set; }
    public Guid SceneVideoVersionId { get; set; }
    public int ItemOrder { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }
    public string? StorageKey { get; set; }
    public string? PublicUrl { get; set; }
    public Guid? SourceImageVersionId { get; set; }
}

public interface ISceneMediaVersioningService
{
    Task<bool> IsEnabledAsync(string settingKey, CancellationToken ct = default);
    Task<SceneImageVersionDto> CreateQueuedImageVersionAsync(SceneImageVersionCreateRequest request, CancellationToken ct = default);
    Task CompleteImageVersionAsync(Guid versionId, SceneImageVersionCompleteRequest request, CancellationToken ct = default);
    Task FailImageVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default);
    Task<SceneImageVersionDto?> GetSelectedImageVersionAsync(long sceneId, CancellationToken ct = default);
    Task<IReadOnlyList<SceneImageVersionDto>> ListImageVersionsAsync(long sceneId, int skip = 0, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<SceneImageVersionDto>> ListImageVersionsAsync(long sceneId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default);
    Task SelectImageVersionAsync(long sceneId, Guid versionId, Guid? selectedBy, CancellationToken ct = default);
    Task SelectImageVersionAsync(long sceneId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
    Task<SceneVideoVersionDto> CreateQueuedSceneVideoVersionAsync(SceneVideoVersionCreateRequest request, CancellationToken ct = default);
    Task CompleteSceneVideoVersionAsync(Guid versionId, SceneVideoVersionCompleteRequest request, CancellationToken ct = default);
    Task FailSceneVideoVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default);
    Task<IReadOnlyList<SceneVideoVersionDto>> ListSceneVideoVersionsAsync(long sceneId, int skip = 0, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<SceneVideoVersionDto>> ListSceneVideoVersionsAsync(long sceneId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default);
    Task SelectSceneVideoVersionAsync(long sceneId, Guid versionId, Guid? selectedBy, CancellationToken ct = default);
    Task SelectSceneVideoVersionAsync(long sceneId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
    Task MarkSceneVideoVersionSubmittedAsync(Guid versionId, string? providerCode, string? modelName, long? providerCapabilityId, string providerTaskId, CancellationToken ct = default);
    Task<string?> GetSceneVideoProviderTaskIdAsync(Guid versionId, CancellationToken ct = default);
    Task MarkSceneVideoPendingReconciliationAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default);
    Task<FinalVideoVersionDto> CreateQueuedFinalVideoVersionAsync(FinalVideoVersionCreateRequest request, CancellationToken ct = default);
    Task CompleteFinalVideoVersionAsync(Guid versionId, FinalVideoVersionCompleteRequest request, CancellationToken ct = default);
    Task FailFinalVideoVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default);
    Task<IReadOnlyList<FinalVideoVersionDto>> ListFinalVideoVersionsAsync(long projectId, int skip = 0, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<FinalVideoVersionDto>> ListFinalVideoVersionsAsync(long projectId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<FinalVideoVersionItemDto>> ListFinalVideoVersionItemsAsync(Guid finalVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<FinalVideoVersionItemDto>> ListFinalVideoVersionItemsAsync(Guid finalVersionId, CurrentUserSession user, CancellationToken ct = default);
    Task SelectFinalVideoVersionAsync(long projectId, Guid versionId, Guid? selectedBy, CancellationToken ct = default);
    Task SelectFinalVideoVersionAsync(long projectId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class SceneMediaVersioningService : ISceneMediaVersioningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public SceneMediaVersioningService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<bool> IsEnabledAsync(string settingKey, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var value = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT setting_value
              FROM system.app_settings
             WHERE setting_key=@settingKey AND is_active=true
             LIMIT 1;
            """,
            new { settingKey });

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SceneImageVersionDto> CreateQueuedImageVersionAsync(SceneImageVersionCreateRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await LockSceneAsync(conn, tx, request.ProjectId, request.SceneId, _tenant.TenantId);
        var existing = await conn.QuerySingleOrDefaultAsync<SceneImageVersionDto>(
            SelectImageVersionSql + " WHERE logical_request_id=@logicalRequestId AND tenant_id=@tenant;",
            new { request.LogicalRequestId, tenant = _tenant.TenantId }, tx);
        if (existing is not null)
        {
            if (request.RenderJobId is not null)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE video_render.scene_image_versions
                       SET render_job_id=COALESCE(render_job_id, @renderJobId),
                           updated_at=now()
                     WHERE id=@id AND tenant_id=@tenant;
                    """,
                    new { existing.Id, request.RenderJobId, tenant = _tenant.TenantId }, tx);
            }
            tx.Commit();
            return existing;
        }

        var versionNumber = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(max(version_number), 0) + 1 FROM video_render.scene_image_versions WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { request.SceneId, request.ProjectId, tenant = _tenant.TenantId }, tx);
        var id = Guid.NewGuid();
        var storageKey = SceneMediaStorageKeys.SceneImageOutput(_tenant.TenantId, request.ProjectId, request.SceneId, id, "png");

        await conn.ExecuteAsync(
            """
            INSERT INTO video_render.scene_image_versions
                (id, project_id, scene_id, tenant_id, customer_id, created_by, version_number,
                 logical_request_id, render_job_id, image_prompt_snapshot, compiled_image_prompt_snapshot, video_prompt_snapshot,
                 negative_prompt_snapshot, scene_snapshot_json, reference_snapshot_json, render_config_json,
                 storage_key, status, created_at, updated_at)
            VALUES
                (@id, @projectId, @sceneId, @tenant, @customer, @user, @versionNumber,
                 @logicalRequestId, @renderJobId, @imagePrompt, @compiledImagePrompt, @videoPrompt,
                 @negativePrompt, CAST(@sceneSnapshot AS jsonb), CAST(@referenceSnapshot AS jsonb),
                 CAST(@renderConfig AS jsonb), @storageKey, 'queued', now(), now());
            """,
            new
            {
                id,
                request.ProjectId,
                request.SceneId,
                tenant = _tenant.TenantId,
                customer = request.CustomerId,
                user = request.UserId,
                versionNumber,
                logicalRequestId = request.LogicalRequestId,
                request.RenderJobId,
                imagePrompt = request.ImagePromptSnapshot,
                compiledImagePrompt = request.CompiledImagePromptSnapshot,
                videoPrompt = request.VideoPromptSnapshot,
                negativePrompt = request.NegativePromptSnapshot,
                sceneSnapshot = ToJson(request.SceneSnapshot),
                referenceSnapshot = ToJson(request.ReferenceSnapshot),
                renderConfig = ToJson(request.RenderConfigSnapshot),
                storageKey
            }, tx);

        tx.Commit();
        return new SceneImageVersionDto
        {
            Id = id,
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            VersionNumber = versionNumber,
            LogicalRequestId = request.LogicalRequestId,
            Status = "queued",
            StorageKey = storageKey
        };
    }

    public async Task CompleteImageVersionAsync(Guid versionId, SceneImageVersionCompleteRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var version = await conn.QuerySingleAsync<SceneImageVersionDto>(
            SelectImageVersionSql + " WHERE id=@versionId AND tenant_id=@tenant FOR UPDATE;",
            new { versionId, tenant = _tenant.TenantId }, tx);
        var storageKey = request.ObjectKey ?? version.StorageKey;

        await conn.ExecuteAsync(
            "UPDATE video_render.scene_image_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { version.SceneId, version.ProjectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_image_versions
               SET status='completed',
                   provider_code=@providerCode,
                   requested_model=COALESCE(requested_model, @modelName),
                   actual_model=@modelName,
                   provider_capability_id=@providerCapabilityId,
                   provider_task_id=@providerTaskId,
                   result_media_id=@resultMediaId,
                   storage_key=@storageKey,
                   source_file_path=@objectKey,
                   public_url=@imageUrl,
                   mime_type=@mimeType,
                   billing_logical_request_id=@billingLogicalRequestId,
                   estimated_usd=@estimatedUsd,
                   actual_usd=@actualUsd,
                   charged_points=@chargedPoints,
                   refunded_points=@refundedPoints,
                   provider_usage_json=CAST(@providerUsageJson AS jsonb),
                   cost_source=@costSource,
                   is_selected=true,
                   selected_at=now(),
                   selected_by=created_by,
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new
            {
                versionId,
                tenant = _tenant.TenantId,
                request.ProviderCode,
                modelName = request.ModelName,
                request.ProviderCapabilityId,
                request.ProviderTaskId,
                request.ResultMediaId,
                storageKey,
                objectKey = request.ObjectKey,
                request.ImageUrl,
                request.MimeType,
                request.BillingLogicalRequestId,
                request.EstimatedUsd,
                request.ActualUsd,
                request.ChargedPoints,
                request.RefundedPoints,
                request.ProviderUsageJson,
                request.CostSource
            }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.video_project_scenes
               SET selected_image_version_id=@versionId,
                   static_image_url=COALESCE(@imageUrl, static_image_url),
                   static_image_path=COALESCE(@objectKey, static_image_path),
                   status=@status,
                   error_message=NULL,
                   updated_at=now()
             WHERE id=@sceneId AND tenant_id=@tenant;
            """,
            new { versionId, sceneId = version.SceneId, tenant = _tenant.TenantId, imageUrl = request.ImageUrl, objectKey = request.ObjectKey, status = VideoSceneStatuses.ImageReady }, tx);

        tx.Commit();
    }

    public async Task FailImageVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        await UpdateVersionFailureAsync("video_render.scene_image_versions", versionId, errorCode, errorMessage, ct);
    }

    public async Task<SceneImageVersionDto?> GetSelectedImageVersionAsync(long sceneId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SceneImageVersionDto>(
            SelectImageVersionSql + " WHERE scene_id=@sceneId AND tenant_id=@tenant AND is_selected=true LIMIT 1;",
            new { sceneId, tenant = _tenant.TenantId });
    }

    public async Task<IReadOnlyList<SceneImageVersionDto>> ListImageVersionsAsync(long sceneId, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<SceneImageVersionDto>(
            SelectImageVersionSql +
            """
             WHERE scene_id=@sceneId AND tenant_id=@tenant
             ORDER BY version_number DESC
             OFFSET @skip LIMIT @take;
            """,
            new { sceneId, tenant = _tenant.TenantId, skip = Math.Max(0, skip), take = Math.Clamp(take, 1, 100) });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SceneImageVersionDto>> ListImageVersionsAsync(long sceneId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await EnsureSceneAccessAsync(sceneId, user, ct);
        return await ListImageVersionsAsync(sceneId, skip, take, ct);
    }

    public async Task SelectImageVersionAsync(long sceneId, Guid versionId, Guid? selectedBy, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var version = await conn.QuerySingleAsync<SceneImageVersionDto>(
            SelectImageVersionSql +
            """
             WHERE id=@versionId AND scene_id=@sceneId AND tenant_id=@tenant AND status='completed'
             FOR UPDATE;
            """,
            new { versionId, sceneId, tenant = _tenant.TenantId }, tx);

        await conn.ExecuteAsync(
            "UPDATE video_render.scene_image_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { sceneId, projectId = version.ProjectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_image_versions
               SET is_selected=true, selected_at=now(), selected_by=@selectedBy, updated_at=now()
             WHERE id=@versionId;
            UPDATE video_render.video_project_scenes
               SET selected_image_version_id=@versionId,
                   static_image_url=COALESCE(@publicUrl, static_image_url),
                   static_image_path=COALESCE(@sourceFilePath, static_image_path),
                   image_prompt=COALESCE(@imagePromptSnapshot, image_prompt),
                   video_prompt=COALESCE(@videoPromptSnapshot, video_prompt),
                   updated_at=now()
             WHERE id=@sceneId AND tenant_id=@tenant;
            INSERT INTO video_render.video_project_events
                (project_id, tenant_id, event_type, level, message, data_json, created_at)
            VALUES
                (@projectId, @tenant, 'SCENE_IMAGE_VERSION_SELECTED', 'info', 'Scene image version selected.', CAST(@data AS jsonb), now());
            """,
            new
            {
                versionId,
                sceneId,
                projectId = version.ProjectId,
                tenant = _tenant.TenantId,
                selectedBy,
                publicUrl = version.PublicUrl,
                sourceFilePath = version.SourceFilePath,
                version.ImagePromptSnapshot,
                version.VideoPromptSnapshot,
                data = ToJson(new { sceneId, versionId, selectedBy, selectedAt = DateTimeOffset.UtcNow })
            }, tx);
        tx.Commit();
    }

    public async Task SelectImageVersionAsync(long sceneId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        await EnsureSceneAccessAsync(sceneId, user, ct);
        await SelectImageVersionAsync(sceneId, versionId, user.UserId, ct);
    }

    public async Task<SceneVideoVersionDto> CreateQueuedSceneVideoVersionAsync(SceneVideoVersionCreateRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await LockSceneAsync(conn, tx, request.ProjectId, request.SceneId, _tenant.TenantId);
        var existing = await conn.QuerySingleOrDefaultAsync<SceneVideoVersionDto>(
            SelectSceneVideoVersionSql + " WHERE logical_request_id=@logicalRequestId AND tenant_id=@tenant;",
            new { request.LogicalRequestId, tenant = _tenant.TenantId }, tx);
        if (existing is not null)
        {
            if (request.RenderJobId is not null)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE video_render.scene_video_versions
                       SET render_job_id=COALESCE(render_job_id, @renderJobId),
                           updated_at=now()
                     WHERE id=@id AND tenant_id=@tenant;
                    """,
                    new { existing.Id, request.RenderJobId, tenant = _tenant.TenantId }, tx);
            }
            tx.Commit();
            return existing;
        }

        var versionNumber = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(max(version_number), 0) + 1 FROM video_render.scene_video_versions WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { request.SceneId, request.ProjectId, tenant = _tenant.TenantId }, tx);
        var id = Guid.NewGuid();
        var storageKey = SceneMediaStorageKeys.SceneVideoOutput(_tenant.TenantId, request.ProjectId, request.SceneId, id);

        await conn.ExecuteAsync(
            """
            INSERT INTO video_render.scene_video_versions
                (id, project_id, scene_id, source_image_version_id, tenant_id, customer_id, created_by,
                 version_number, logical_request_id, render_job_id, image_prompt_snapshot, video_prompt_snapshot,
                 scene_snapshot_json, render_config_json, storage_key, status, created_at, updated_at)
            VALUES
                (@id, @projectId, @sceneId, @sourceImageVersionId, @tenant, @customer, @user,
                 @versionNumber, @logicalRequestId, @renderJobId, @imagePrompt, @videoPrompt,
                 CAST(@sceneSnapshot AS jsonb), CAST(@renderConfig AS jsonb), @storageKey, 'queued', now(), now());
            """,
            new
            {
                id,
                request.ProjectId,
                request.SceneId,
                request.SourceImageVersionId,
                tenant = _tenant.TenantId,
                customer = request.CustomerId,
                user = request.UserId,
                versionNumber,
                logicalRequestId = request.LogicalRequestId,
                request.RenderJobId,
                imagePrompt = request.ImagePromptSnapshot,
                videoPrompt = request.VideoPromptSnapshot,
                sceneSnapshot = ToJson(request.SceneSnapshot),
                renderConfig = ToJson(request.RenderConfigSnapshot),
                storageKey
            }, tx);

        tx.Commit();
        return new SceneVideoVersionDto
        {
            Id = id,
            ProjectId = request.ProjectId,
            SceneId = request.SceneId,
            SourceImageVersionId = request.SourceImageVersionId,
            VersionNumber = versionNumber,
            LogicalRequestId = request.LogicalRequestId,
            Status = "queued",
            StorageKey = storageKey
        };
    }

    public async Task CompleteSceneVideoVersionAsync(Guid versionId, SceneVideoVersionCompleteRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var version = await conn.QuerySingleAsync<SceneVideoVersionDto>(
            SelectSceneVideoVersionSql + " WHERE id=@versionId AND tenant_id=@tenant FOR UPDATE;",
            new { versionId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            "UPDATE video_render.scene_video_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { version.SceneId, version.ProjectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_video_versions
               SET status='completed',
                   provider_code=COALESCE(@providerCode, provider_code),
                   provider_capability_id=COALESCE(@providerCapabilityId, provider_capability_id),
                   requested_model=COALESCE(requested_model, @modelName),
                   actual_model=COALESCE(@modelName, actual_model),
                   provider_task_id=COALESCE(@providerTaskId, provider_task_id),
                   public_url=@videoUrl,
                   source_file_path=@videoPath,
                   poster_url=@posterUrl,
                   duration_seconds=@durationSeconds,
                   aspect_ratio=COALESCE(@aspectRatio, aspect_ratio),
                   mime_type=@mimeType,
                   billing_logical_request_id=COALESCE(@billingLogicalRequestId, billing_logical_request_id),
                   estimated_usd=COALESCE(@estimatedUsd, estimated_usd),
                   actual_usd=@actualUsd,
                   charged_points=@chargedPoints,
                   refunded_points=@refundedPoints,
                   cost_source=COALESCE(@costSource, cost_source),
                   is_selected=true,
                   selected_at=now(),
                   selected_by=created_by,
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new
            {
                versionId,
                tenant = _tenant.TenantId,
                request.ProviderCode,
                modelName = request.ModelName,
                request.ProviderCapabilityId,
                request.ProviderTaskId,
                request.VideoUrl,
                request.VideoPath,
                request.PosterUrl,
                request.DurationSeconds,
                request.AspectRatio,
                request.MimeType,
                request.BillingLogicalRequestId,
                request.EstimatedUsd,
                request.ActualUsd,
                request.ChargedPoints,
                request.RefundedPoints,
                request.CostSource
            }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.video_project_scenes
               SET selected_video_version_id=@versionId,
                   scene_video_url=COALESCE(@videoUrl, scene_video_url),
                   scene_video_path=COALESCE(@videoPath, scene_video_path),
                   status=@status,
                   error_message=NULL,
                   updated_at=now()
             WHERE id=@sceneId AND tenant_id=@tenant;
            """,
            new { versionId, sceneId = version.SceneId, tenant = _tenant.TenantId, request.VideoUrl, request.VideoPath, status = VideoSceneStatuses.VideoReady }, tx);
        tx.Commit();
    }

    public async Task FailSceneVideoVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        await UpdateVersionFailureAsync("video_render.scene_video_versions", versionId, errorCode, errorMessage, ct);
    }

    public async Task<IReadOnlyList<SceneVideoVersionDto>> ListSceneVideoVersionsAsync(long sceneId, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<SceneVideoVersionDto>(
            SelectSceneVideoVersionSql +
            """
             WHERE scene_id=@sceneId AND tenant_id=@tenant
             ORDER BY version_number DESC
             OFFSET @skip LIMIT @take;
            """,
            new { sceneId, tenant = _tenant.TenantId, skip = Math.Max(0, skip), take = Math.Clamp(take, 1, 100) });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SceneVideoVersionDto>> ListSceneVideoVersionsAsync(long sceneId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await EnsureSceneAccessAsync(sceneId, user, ct);
        return await ListSceneVideoVersionsAsync(sceneId, skip, take, ct);
    }

    public async Task SelectSceneVideoVersionAsync(long sceneId, Guid versionId, Guid? selectedBy, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var version = await conn.QuerySingleAsync<SceneVideoVersionDto>(
            SelectSceneVideoVersionSql +
            """
             WHERE id=@versionId AND scene_id=@sceneId AND tenant_id=@tenant AND status='completed'
             FOR UPDATE;
            """,
            new { versionId, sceneId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            "UPDATE video_render.scene_video_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;",
            new { sceneId, projectId = version.ProjectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_video_versions
               SET is_selected=true, selected_at=now(), selected_by=@selectedBy, updated_at=now()
             WHERE id=@versionId;
            UPDATE video_render.video_project_scenes
               SET selected_video_version_id=@versionId,
                   scene_video_url=COALESCE(@publicUrl, scene_video_url),
                   scene_video_path=COALESCE(@sourceFilePath, scene_video_path),
                   updated_at=now()
             WHERE id=@sceneId AND tenant_id=@tenant;
            """,
            new { versionId, sceneId, tenant = _tenant.TenantId, selectedBy, publicUrl = version.PublicUrl, sourceFilePath = version.SourceFilePath }, tx);
        tx.Commit();
    }

    public async Task SelectSceneVideoVersionAsync(long sceneId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        await EnsureSceneAccessAsync(sceneId, user, ct);
        await SelectSceneVideoVersionAsync(sceneId, versionId, user.UserId, ct);
    }

    public async Task MarkSceneVideoVersionSubmittedAsync(Guid versionId, string? providerCode, string? modelName, long? providerCapabilityId, string providerTaskId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_video_versions
               SET status='submitted',
                   provider_code=COALESCE(@providerCode, provider_code),
                   provider_capability_id=COALESCE(@providerCapabilityId, provider_capability_id),
                   requested_model=COALESCE(requested_model, @modelName),
                   actual_model=COALESCE(@modelName, actual_model),
                   provider_task_id=@providerTaskId,
                   submitted_at=COALESCE(submitted_at, now()),
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new
            {
                versionId,
                tenant = _tenant.TenantId,
                providerCode,
                modelName,
                providerCapabilityId,
                providerTaskId
            });
    }

    public async Task<string?> GetSceneVideoProviderTaskIdAsync(Guid versionId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(
            """
            SELECT provider_task_id
              FROM video_render.scene_video_versions
             WHERE id=@versionId AND tenant_id=@tenant
             LIMIT 1;
            """,
            new { versionId, tenant = _tenant.TenantId });
    }

    public async Task MarkSceneVideoPendingReconciliationAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.scene_video_versions
               SET status='pending_reconciliation',
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new { versionId, tenant = _tenant.TenantId, errorCode, errorMessage });
    }

    public async Task<FinalVideoVersionDto> CreateQueuedFinalVideoVersionAsync(FinalVideoVersionCreateRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            "SELECT id FROM video_render.video_projects WHERE id=@projectId AND tenant_id=@tenant FOR UPDATE;",
            new { request.ProjectId, tenant = _tenant.TenantId }, tx);
        var existing = await conn.QuerySingleOrDefaultAsync<FinalVideoVersionDto>(
            SelectFinalVideoVersionSql + " WHERE logical_request_id=@logicalRequestId AND tenant_id=@tenant;",
            new { request.LogicalRequestId, tenant = _tenant.TenantId }, tx);
        if (existing is not null)
        {
            if (request.RenderJobId is not null)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE video_render.final_video_versions
                       SET render_job_id=COALESCE(render_job_id, @renderJobId),
                           updated_at=now()
                     WHERE id=@id AND tenant_id=@tenant;
                    """,
                    new { existing.Id, request.RenderJobId, tenant = _tenant.TenantId }, tx);
            }
            tx.Commit();
            return existing;
        }

        var selectedVideos = (await conn.QueryAsync<SelectedSceneVideoRow>(
            """
            SELECT s.id AS SceneId, s.scene_index AS SceneIndex, s.selected_video_version_id AS SceneVideoVersionId
              FROM video_render.video_project_scenes s
             WHERE s.project_id=@projectId AND s.tenant_id=@tenant
             ORDER BY s.scene_index;
            """,
            new { request.ProjectId, tenant = _tenant.TenantId }, tx)).ToList();
        if (selectedVideos.Count == 0 || selectedVideos.Any(x => x.SceneVideoVersionId is null))
        {
            throw new InvalidOperationException("Project is missing selected scene video versions.");
        }

        var versionNumber = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(max(version_number), 0) + 1 FROM video_render.final_video_versions WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { request.ProjectId, tenant = _tenant.TenantId }, tx);
        var id = Guid.NewGuid();
        var storageKey = SceneMediaStorageKeys.FinalVideoOutput(_tenant.TenantId, request.ProjectId, id);

        await conn.ExecuteAsync(
            """
            INSERT INTO video_render.final_video_versions
                (id, project_id, tenant_id, customer_id, created_by, version_number, logical_request_id,
                 render_job_id, composition_config_json, transition_config_json, audio_config_json,
                 subtitle_config_json, storage_key, status, created_at, updated_at)
            VALUES
                (@id, @projectId, @tenant, @customer, @user, @versionNumber, @logicalRequestId,
                 @renderJobId, CAST(@composition AS jsonb), CAST(@transition AS jsonb), CAST(@audio AS jsonb),
                 CAST(@subtitle AS jsonb), @storageKey, 'queued', now(), now());
            """,
            new
            {
                id,
                request.ProjectId,
                tenant = _tenant.TenantId,
                customer = request.CustomerId,
                user = request.UserId,
                versionNumber,
                logicalRequestId = request.LogicalRequestId,
                request.RenderJobId,
                composition = ToJson(request.CompositionConfigSnapshot),
                transition = ToJson(request.TransitionConfigSnapshot),
                audio = ToJson(request.AudioConfigSnapshot),
                subtitle = ToJson(request.SubtitleConfigSnapshot),
                storageKey
            }, tx);

        foreach (var item in selectedVideos)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.final_video_version_items
                    (final_video_version_id, scene_id, scene_video_version_id, item_order, config_json)
                VALUES
                    (@finalVersionId, @sceneId, @sceneVideoVersionId, @itemOrder, '{}'::jsonb);
                """,
                new { finalVersionId = id, item.SceneId, item.SceneVideoVersionId, itemOrder = item.SceneIndex }, tx);
        }

        tx.Commit();
        return new FinalVideoVersionDto
        {
            Id = id,
            ProjectId = request.ProjectId,
            VersionNumber = versionNumber,
            LogicalRequestId = request.LogicalRequestId,
            Status = "queued",
            StorageKey = storageKey
        };
    }

    public async Task CompleteFinalVideoVersionAsync(Guid versionId, FinalVideoVersionCompleteRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var version = await conn.QuerySingleAsync<FinalVideoVersionDto>(
            SelectFinalVideoVersionSql + " WHERE id=@versionId AND tenant_id=@tenant FOR UPDATE;",
            new { versionId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            "UPDATE video_render.final_video_versions SET is_selected=false WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { version.ProjectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.final_video_versions
               SET status='completed',
                   public_url=@videoUrl,
                   source_file_path=@videoPath,
                   poster_url=@posterUrl,
                   duration_seconds=@durationSeconds,
                   mime_type=@mimeType,
                   is_selected=true,
                   selected_at=now(),
                   selected_by=created_by,
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new { versionId, tenant = _tenant.TenantId, request.VideoUrl, request.VideoPath, request.PosterUrl, request.DurationSeconds, request.MimeType }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.video_projects
               SET selected_final_video_version_id=@versionId,
                   final_video_url=COALESCE(@videoUrl, final_video_url),
                   final_video_path=COALESCE(@videoPath, final_video_path),
                   status=@status,
                   error_message=NULL,
                   updated_at=now()
             WHERE id=@projectId AND tenant_id=@tenant;
            """,
            new { versionId, projectId = version.ProjectId, tenant = _tenant.TenantId, request.VideoUrl, request.VideoPath, status = VideoProjectStatuses.Completed }, tx);
        tx.Commit();
    }

    public async Task FailFinalVideoVersionAsync(Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        await UpdateVersionFailureAsync("video_render.final_video_versions", versionId, errorCode, errorMessage, ct);
    }

    public async Task<IReadOnlyList<FinalVideoVersionDto>> ListFinalVideoVersionsAsync(long projectId, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<FinalVideoVersionDto>(
            SelectFinalVideoVersionSql +
            """
             WHERE project_id=@projectId AND tenant_id=@tenant
             ORDER BY version_number DESC
             OFFSET @skip LIMIT @take;
            """,
            new { projectId, tenant = _tenant.TenantId, skip = Math.Max(0, skip), take = Math.Clamp(take, 1, 100) });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<FinalVideoVersionDto>> ListFinalVideoVersionsAsync(long projectId, CurrentUserSession user, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        await EnsureProjectAccessAsync(projectId, user, ct);
        return await ListFinalVideoVersionsAsync(projectId, skip, take, ct);
    }

    public async Task<IReadOnlyList<FinalVideoVersionItemDto>> ListFinalVideoVersionItemsAsync(Guid finalVersionId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<FinalVideoVersionItemDto>(
            """
            SELECT i.id AS Id,
                   i.final_video_version_id AS FinalVideoVersionId,
                   i.scene_id AS SceneId,
                   i.scene_video_version_id AS SceneVideoVersionId,
                   i.item_order AS ItemOrder,
                   v.status AS Status,
                   v.source_file_path AS SourceFilePath,
                   v.storage_key AS StorageKey,
                   v.public_url AS PublicUrl,
                   v.source_image_version_id AS SourceImageVersionId
              FROM video_render.final_video_version_items i
              JOIN video_render.final_video_versions f ON f.id=i.final_video_version_id
              JOIN video_render.scene_video_versions v ON v.id=i.scene_video_version_id
             WHERE i.final_video_version_id=@finalVersionId
               AND f.tenant_id=@tenant
               AND v.tenant_id=@tenant
               AND v.project_id=f.project_id
               AND v.scene_id=i.scene_id
             ORDER BY i.item_order;
            """,
            new { finalVersionId, tenant = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<FinalVideoVersionItemDto>> ListFinalVideoVersionItemsAsync(Guid finalVersionId, CurrentUserSession user, CancellationToken ct = default)
    {
        await EnsureFinalVersionAccessAsync(finalVersionId, user, ct);
        return await ListFinalVideoVersionItemsAsync(finalVersionId, ct);
    }

    public async Task SelectFinalVideoVersionAsync(long projectId, Guid versionId, Guid? selectedBy, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var version = await conn.QuerySingleAsync<FinalVideoVersionDto>(
            SelectFinalVideoVersionSql +
            """
             WHERE id=@versionId AND project_id=@projectId AND tenant_id=@tenant AND status='completed'
             FOR UPDATE;
            """,
            new { versionId, projectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            "UPDATE video_render.final_video_versions SET is_selected=false WHERE project_id=@projectId AND tenant_id=@tenant;",
            new { projectId, tenant = _tenant.TenantId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE video_render.final_video_versions
               SET is_selected=true, selected_at=now(), selected_by=@selectedBy, updated_at=now()
             WHERE id=@versionId;
            UPDATE video_render.video_projects
               SET selected_final_video_version_id=@versionId,
                   final_video_url=COALESCE(@publicUrl, final_video_url),
                   final_video_path=COALESCE(@sourceFilePath, final_video_path),
                   updated_at=now()
             WHERE id=@projectId AND tenant_id=@tenant;
            """,
            new { versionId, projectId, tenant = _tenant.TenantId, selectedBy, publicUrl = version.PublicUrl, sourceFilePath = version.SourceFilePath }, tx);
        tx.Commit();
    }

    public async Task SelectFinalVideoVersionAsync(long projectId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        await EnsureProjectAccessAsync(projectId, user, ct);
        await SelectFinalVideoVersionAsync(projectId, versionId, user.UserId, ct);
    }

    private async Task EnsureSceneAccessAsync(long sceneId, CurrentUserSession user, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var allowed = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS(
                SELECT 1
                  FROM video_render.video_project_scenes s
                  JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
                 WHERE s.id=@sceneId
                   AND s.tenant_id=@tenant
                   AND (
                        @canCrossCustomer
                        OR p.user_id=@userId
                        OR (@customerId IS NOT NULL AND p.customer_id IS NOT DISTINCT FROM @customerId)
                   )
            );
            """,
            new
            {
                sceneId,
                tenant = _tenant.TenantId,
                userId = user.UserId,
                customerId = user.CustomerId,
                canCrossCustomer = CanCrossCustomer(user)
            });
        if (!allowed)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập lịch sử media của scene này.");
        }
    }

    private async Task EnsureProjectAccessAsync(long projectId, CurrentUserSession user, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var allowed = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS(
                SELECT 1
                  FROM video_render.video_projects p
                 WHERE p.id=@projectId
                   AND p.tenant_id=@tenant
                   AND (
                        @canCrossCustomer
                        OR p.user_id=@userId
                        OR (@customerId IS NOT NULL AND p.customer_id IS NOT DISTINCT FROM @customerId)
                   )
            );
            """,
            new
            {
                projectId,
                tenant = _tenant.TenantId,
                userId = user.UserId,
                customerId = user.CustomerId,
                canCrossCustomer = CanCrossCustomer(user)
            });
        if (!allowed)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập lịch sử media của project này.");
        }
    }

    private async Task EnsureFinalVersionAccessAsync(Guid finalVersionId, CurrentUserSession user, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var projectId = await conn.ExecuteScalarAsync<long?>(
            """
            SELECT project_id
              FROM video_render.final_video_versions
             WHERE id=@finalVersionId AND tenant_id=@tenant;
            """,
            new { finalVersionId, tenant = _tenant.TenantId });
        if (projectId is null)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập phiên bản final video này.");
        }

        await EnsureProjectAccessAsync(projectId.Value, user, ct);
    }

    private static bool CanCrossCustomer(CurrentUserSession user)
        => user.IsRoot
           || user.Can("video.render.manage")
           || user.Can("render.video.manage")
           || user.Can("ai.video.version.manage");

    private async Task UpdateVersionFailureAsync(string tableName, Guid versionId, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE {tableName}
               SET status='failed',
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   updated_at=now()
             WHERE id=@versionId AND tenant_id=@tenant;
            """,
            new { versionId, tenant = _tenant.TenantId, errorCode, errorMessage });
    }

    private static async Task<SceneLockRow> LockSceneAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, long projectId, long sceneId, Guid tenantId)
    {
        return await conn.QuerySingleAsync<SceneLockRow>(
            """
            SELECT id AS SceneId, project_id AS ProjectId
              FROM video_render.video_project_scenes
             WHERE id=@sceneId AND project_id=@projectId AND tenant_id=@tenantId
             FOR UPDATE;
            """,
            new { sceneId, projectId, tenantId }, tx);
    }

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private const string SelectImageVersionSql =
        """
        SELECT id AS Id, project_id AS ProjectId, scene_id AS SceneId, version_number AS VersionNumber,
               logical_request_id AS LogicalRequestId, status AS Status, is_selected AS IsSelected, storage_key AS StorageKey,
               public_url AS PublicUrl, source_file_path AS SourceFilePath, result_media_id AS ResultMediaId,
               image_prompt_snapshot AS ImagePromptSnapshot, compiled_image_prompt_snapshot AS CompiledImagePromptSnapshot,
               video_prompt_snapshot AS VideoPromptSnapshot, provider_code AS ProviderCode,
               actual_model AS ModelName, provider_capability_id AS ProviderCapabilityId,
               provider_task_id AS ProviderTaskId, billing_logical_request_id AS BillingLogicalRequestId,
               estimated_usd AS EstimatedUsd, actual_usd AS ActualUsd, charged_points AS ChargedPoints,
               refunded_points AS RefundedPoints, error_message AS ErrorMessage, created_at AS CreatedAt
          FROM video_render.scene_image_versions
        """;

    private const string SelectSceneVideoVersionSql =
        """
        SELECT id AS Id, project_id AS ProjectId, scene_id AS SceneId, source_image_version_id AS SourceImageVersionId,
               version_number AS VersionNumber, logical_request_id AS LogicalRequestId, status AS Status,
               is_selected AS IsSelected, storage_key AS StorageKey, public_url AS PublicUrl, source_file_path AS SourceFilePath,
               image_prompt_snapshot AS ImagePromptSnapshot, video_prompt_snapshot AS VideoPromptSnapshot,
               provider_code AS ProviderCode, actual_model AS ModelName, provider_capability_id AS ProviderCapabilityId,
               provider_task_id AS ProviderTaskId, duration_seconds AS DurationSeconds, aspect_ratio AS AspectRatio,
               billing_logical_request_id AS BillingLogicalRequestId, estimated_usd AS EstimatedUsd, actual_usd AS ActualUsd,
               charged_points AS ChargedPoints, refunded_points AS RefundedPoints, cost_source AS CostSource,
               poster_url AS PosterUrl, error_message AS ErrorMessage, created_at AS CreatedAt
          FROM video_render.scene_video_versions
        """;

    private const string SelectFinalVideoVersionSql =
        """
        SELECT id AS Id, project_id AS ProjectId, version_number AS VersionNumber,
               logical_request_id AS LogicalRequestId, status AS Status, is_selected AS IsSelected,
               storage_key AS StorageKey, public_url AS PublicUrl, source_file_path AS SourceFilePath,
               duration_seconds AS DurationSeconds, poster_url AS PosterUrl, error_message AS ErrorMessage,
               created_at AS CreatedAt
          FROM video_render.final_video_versions
        """;

    private sealed class SceneLockRow
    {
        public long SceneId { get; init; }
        public long ProjectId { get; init; }
    }

    private sealed class SelectedSceneVideoRow
    {
        public long SceneId { get; init; }
        public int SceneIndex { get; init; }
        public Guid? SceneVideoVersionId { get; init; }
    }
}

public static class SceneMediaStorageKeys
{
    public static string SceneImageOutput(Guid tenantId, long projectId, long sceneId, Guid imageVersionId, string extension)
        => $"render-projects/{tenantId:N}/{projectId}/scenes/{sceneId}/images/{imageVersionId:N}/output/scene-image.{NormalizeExtension(extension)}";

    public static string SceneVideoOutput(Guid tenantId, long projectId, long sceneId, Guid videoVersionId)
        => $"render-projects/{tenantId:N}/{projectId}/scenes/{sceneId}/videos/{videoVersionId:N}/output/scene-video.mp4";

    public static string FinalVideoOutput(Guid tenantId, long projectId, Guid finalVideoVersionId)
        => $"render-projects/{tenantId:N}/{projectId}/final-videos/{finalVideoVersionId:N}/output/final-video.mp4";

    public static string FinalManifest(Guid tenantId, long projectId, Guid finalVideoVersionId)
        => $"render-projects/{tenantId:N}/{projectId}/final-videos/{finalVideoVersionId:N}/manifests/composition.json";

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? "bin" : value;
    }
}
