using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using Microsoft.Extensions.Logging;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<VideoRenderRepository> _logger;

    public VideoRenderRepository(TodoXConnectionFactory factory, TenantContext tenant, ILogger<VideoRenderRepository> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<VideoProjectDto> CreateProjectAsync(VideoProjectCreateRequest request, Guid? userId, Guid? customerId, string storageRoot, string publicBase, string jobFolder, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var sceneSeconds = Math.Max(1, request.SceneSeconds);
            var customScenes = request.Scenes?.OrderBy(x => x.SceneIndex).ToList() ?? new List<VideoProjectSceneCreateRequest>();
            var useCustomScenes = customScenes.Count > 0;
            var sceneCount = request.CreateEmptyProject ? 0 : useCustomScenes ? customScenes.Count : Math.Max(1, (int)Math.Ceiling(Math.Max(sceneSeconds, request.TotalSeconds) / (double)sceneSeconds));
            var totalSeconds = Math.Max(sceneSeconds, request.TotalSeconds);

            DbDiagnostics.LogFieldLengths(_logger, "video_render_create_project", ("title", request.Title), ("prompt", request.Prompt), ("storageRoot", storageRoot), ("publicBase", publicBase), ("jobFolder", jobFolder));

            var project = await conn.QuerySingleAsync<VideoProjectDto>(
                """
                INSERT INTO video_render.video_projects
                    (tenant_id, user_id, customer_id, title, original_prompt, total_seconds, scene_seconds, scene_count,
                     think_scenes, character_id, uploaded_character_url, storage_root, public_base, job_folder, status, created_at, updated_at)
                VALUES
                    (@tenant, @user, @customer, @title, @prompt, @total, @sceneSeconds, @sceneCount,
                     @think, @character, @uploaded, @storageRoot, @publicBase, @jobFolder, @status, now(), now())
                RETURNING id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId, title AS Title,
                          original_prompt AS OriginalPrompt, total_seconds AS TotalSeconds, scene_seconds AS SceneSeconds,
                          scene_count AS SceneCount, think_scenes AS ThinkScenes, character_id AS CharacterId,
                          uploaded_character_url AS UploadedCharacterUrl, storage_root AS StorageRoot, public_base AS PublicBase,
                          job_folder AS JobFolder, status AS Status, final_video_url AS FinalVideoUrl,
                          final_video_path AS FinalVideoPath, error_message AS ErrorMessage, created_at AS CreatedAt,
                          updated_at AS UpdatedAt;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    user = userId,
                    customer = customerId,
                    title = request.Title,
                    prompt = request.Prompt,
                    total = totalSeconds,
                    sceneSeconds,
                    sceneCount,
                    think = request.ThinkScenes,
                    character = request.CharacterId,
                    uploaded = request.UploadedCharacterUrl,
                    storageRoot,
                    publicBase,
                    jobFolder,
                    status = VideoProjectStatuses.SceneReady
                }, tx);

            for (var index = 1; index <= sceneCount; index++)
            {
                var sceneData = useCustomScenes ? customScenes[index - 1] : null;
                var duration = sceneData?.DurationSeconds > 0
                    ? sceneData.DurationSeconds
                    : index == sceneCount
                        ? Math.Max(1, totalSeconds - sceneSeconds * (sceneCount - 1))
                        : sceneSeconds;
                var aspectRatio = NormalizeAspectRatio(request.AspectRatio);
                var scenePrompt = sceneData?.ScenePrompt ?? BuildScenePrompt(request.Prompt, index, sceneCount, duration, request.ThinkScenes, aspectRatio);

                await conn.ExecuteAsync(
                    """
                    INSERT INTO video_render.video_project_scenes
                        (project_id, tenant_id, scene_index, title, duration_seconds, scene_prompt, image_prompt, video_prompt,
                         static_image_path, static_image_url, scene_video_path, scene_video_url, status, error_message, created_at, updated_at)
                    VALUES
                        (@projectId, @tenant, @sceneIndex, @title, @duration, @scenePrompt, @imagePrompt, @videoPrompt,
                         NULL, NULL, NULL, NULL, @status, NULL, now(), now());
                    """,
                    new
                    {
                        projectId = project.Id,
                        tenant = _tenant.TenantId,
                        sceneIndex = index,
                        title = sceneData?.Title ?? $"Scene {index:00}",
                        duration,
                        scenePrompt,
                        imagePrompt = sceneData?.ImagePrompt ?? $"Static preview for scene {index}. {scenePrompt}",
                        videoPrompt = sceneData?.VideoPrompt ?? $"{AspectRatioLabel(aspectRatio)} video, {duration} seconds. {scenePrompt}",
                        status = VideoSceneStatuses.Draft
                    }, tx);
            }

            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.video_project_events
                    (project_id, tenant_id, event_type, level, message, data_json, created_at)
                VALUES
                    (@projectId, @tenant, 'PROJECT_CREATED', 'info', 'Video project and mock scene plan created.', CAST(@data AS jsonb), now());
                """,
                new
                {
                    projectId = project.Id,
                    tenant = _tenant.TenantId,
                    data = JsonSerializer.Serialize(new { project.Id, sceneCount, totalSeconds, sceneSeconds }, JsonOptions)
                }, tx);

            tx.Commit();
            return project;
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_create_project"))
        {
            throw;
        }
    }

    public async Task<VideoProjectDto?> GetProjectAsync(long projectId, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var project = await conn.QuerySingleOrDefaultAsync<VideoProjectDto>(
                """
                SELECT id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId, title AS Title,
                       original_prompt AS OriginalPrompt, total_seconds AS TotalSeconds, scene_seconds AS SceneSeconds,
                       scene_count AS SceneCount, think_scenes AS ThinkScenes, character_id AS CharacterId,
                       uploaded_character_url AS UploadedCharacterUrl, storage_root AS StorageRoot, public_base AS PublicBase,
                       job_folder AS JobFolder, status AS Status, final_video_url AS FinalVideoUrl,
                       final_video_path AS FinalVideoPath, error_message AS ErrorMessage, created_at AS CreatedAt,
                       updated_at AS UpdatedAt
                  FROM video_render.video_projects
                 WHERE id=@projectId AND tenant_id=@tenant;
                """,
                new { projectId, tenant = _tenant.TenantId });

            if (project is null) return null;

            project.Scenes = (await conn.QueryAsync<VideoProjectSceneDto>(
                """
                SELECT id AS Id, project_id AS ProjectId, tenant_id AS TenantId, scene_index AS SceneIndex,
                       title AS Title, duration_seconds AS DurationSeconds, scene_prompt AS ScenePrompt,
                       image_prompt AS ImagePrompt, video_prompt AS VideoPrompt, static_image_path AS StaticImagePath,
                       static_image_url AS StaticImageUrl, scene_video_path AS SceneVideoPath, scene_video_url AS SceneVideoUrl,
                       status AS Status, error_message AS ErrorMessage, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM video_render.video_project_scenes
                 WHERE project_id=@projectId AND tenant_id=@tenant
                 ORDER BY scene_index;
                """,
                new { projectId, tenant = _tenant.TenantId })).ToList();

            project.Events = (await conn.QueryAsync<VideoProjectEventDto>(
                """
                SELECT id AS Id, project_id AS ProjectId, tenant_id AS TenantId, event_type AS EventType,
                       level AS Level, message AS Message, data_json::text AS DataJson, created_at AS CreatedAt
                  FROM video_render.video_project_events
                 WHERE project_id=@projectId AND tenant_id=@tenant
                 ORDER BY created_at DESC, id DESC;
                """,
                new { projectId, tenant = _tenant.TenantId })).ToList();

            return project;
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_get_project"))
        {
            throw;
        }
    }

    public async Task<VideoProjectDto?> GetProjectAsync(long projectId, CurrentUserSession user, CancellationToken ct = default)
    {
        var project = await GetProjectAsync(projectId, ct);
        if (project is null || CanAccessProject(project, user))
        {
            return project;
        }

        return null;
    }

    public async Task<IReadOnlyList<VideoProjectListItemDto>> ListProjectsAsync(CurrentUserSession user, int skip = 0, int take = 30, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            var rows = await conn.QueryAsync<VideoProjectListItemDto>(
                """
                SELECT p.id AS Id,
                       p.user_id AS UserId,
                       p.customer_id AS CustomerId,
                       p.title AS Title,
                       p.character_id AS CharacterId,
                       c.character_name AS CharacterName,
                       COALESCE(count(s.id), 0)::int AS SceneCount,
                       COALESCE(count(s.id) FILTER (WHERE s.static_image_url IS NOT NULL AND s.static_image_url <> ''), 0)::int AS ImageReadyCount,
                       COALESCE(count(s.id) FILTER (WHERE s.scene_video_url IS NOT NULL AND s.scene_video_url <> ''), 0)::int AS VideoReadyCount,
                       p.status AS Status,
                       (
                         SELECT s2.static_image_url
                           FROM video_render.video_project_scenes s2
                          WHERE s2.project_id = p.id
                            AND s2.tenant_id = p.tenant_id
                            AND s2.static_image_url IS NOT NULL
                            AND s2.static_image_url <> ''
                          ORDER BY s2.updated_at DESC, s2.scene_index
                          LIMIT 1
                       ) AS ThumbnailUrl,
                       p.created_at AS CreatedAt,
                       p.updated_at AS UpdatedAt
                  FROM video_render.video_projects p
                  LEFT JOIN video_render.video_project_scenes s ON s.project_id = p.id AND s.tenant_id = p.tenant_id
                  LEFT JOIN public.todox_ai_character c ON c.id = p.character_id
                 WHERE p.tenant_id = @tenant
                   AND (
                        @canCrossCustomer
                        OR p.user_id = @userId
                        OR (@customerId IS NOT NULL AND p.customer_id IS NOT DISTINCT FROM @customerId)
                   )
                 GROUP BY p.id, c.character_name
                 ORDER BY p.updated_at DESC, p.created_at DESC
                 OFFSET @skip LIMIT @take;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    userId = user.UserId,
                    customerId = user.CustomerId,
                    canCrossCustomer = CanCrossCustomer(user),
                    skip = Math.Max(0, skip),
                    take = Math.Clamp(take, 1, 100)
                });
            return rows.ToList();
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_list_projects"))
        {
            throw;
        }
    }

    public async Task UpdateProjectDraftAsync(long projectId, VideoProjectUpdateRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var project = await conn.QuerySingleOrDefaultAsync<VideoProjectDto>(
                """
                SELECT id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId, title AS Title,
                       original_prompt AS OriginalPrompt, total_seconds AS TotalSeconds, scene_seconds AS SceneSeconds,
                       scene_count AS SceneCount, think_scenes AS ThinkScenes, character_id AS CharacterId,
                       status AS Status, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM video_render.video_projects
                 WHERE id=@projectId AND tenant_id=@tenant
                 FOR UPDATE;
                """,
                new { projectId, tenant = _tenant.TenantId }, tx);
            if (project is null || !CanAccessProject(project, user))
            {
                tx.Rollback();
                throw new UnauthorizedAccessException("Bạn không có quyền lưu project render này.");
            }

            var scenes = request.Scenes.OrderBy(x => x.SceneIndex).ToList();
            await conn.ExecuteAsync(
                """
                UPDATE video_render.video_projects
                   SET title=@title,
                       original_prompt=@prompt,
                       character_id=@characterId,
                       total_seconds=@totalSeconds,
                       scene_seconds=@sceneSeconds,
                       scene_count=@sceneCount,
                       think_scenes=@thinkScenes,
                       updated_at=now()
                 WHERE id=@projectId AND tenant_id=@tenant;
                """,
                new
                {
                    projectId,
                    tenant = _tenant.TenantId,
                    title = request.Title,
                    prompt = request.OriginalPrompt,
                    characterId = request.CharacterId,
                    totalSeconds = Math.Max(1, request.TotalSeconds),
                    sceneSeconds = Math.Max(1, request.SceneSeconds),
                    sceneCount = scenes.Count,
                    thinkScenes = request.ThinkScenes
                }, tx);

            foreach (var scene in scenes)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE video_render.video_project_scenes
                       SET scene_index=@sceneIndex,
                           title=@title,
                           duration_seconds=@durationSeconds,
                           scene_prompt=@scenePrompt,
                           image_prompt=@imagePrompt,
                           video_prompt=@videoPrompt,
                           status=@status,
                           error_message=@errorMessage,
                           updated_at=now()
                     WHERE id=@sceneId
                       AND project_id=@projectId
                       AND tenant_id=@tenant;
                    """,
                    new
                    {
                        sceneId = scene.Id,
                        projectId,
                        tenant = _tenant.TenantId,
                        sceneIndex = scene.SceneIndex,
                        title = scene.Title,
                        durationSeconds = Math.Max(1, scene.DurationSeconds),
                        scenePrompt = scene.ScenePrompt,
                        imagePrompt = scene.ImagePrompt,
                        videoPrompt = scene.VideoPrompt,
                        status = string.IsNullOrWhiteSpace(scene.Status) ? VideoSceneStatuses.Draft : scene.Status,
                        errorMessage = scene.ErrorMessage
                    }, tx);
            }

            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.video_project_events
                    (project_id, tenant_id, event_type, level, message, data_json, created_at)
                VALUES
                    (@projectId, @tenant, 'PROJECT_SAVED', 'info', 'Video project saved from /render-job.', CAST(@data AS jsonb), now());
                """,
                new
                {
                    projectId,
                    tenant = _tenant.TenantId,
                    data = JsonSerializer.Serialize(new
                    {
                        projectId,
                        request.Title,
                        request.CharacterId,
                        request.TotalSeconds,
                        request.SceneSeconds,
                        request.ThinkScenes,
                        sceneCount = scenes.Count
                    }, JsonOptions)
                }, tx);

            tx.Commit();
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_update_project_draft"))
        {
            throw;
        }
    }

    public async Task<VideoProjectSceneDto?> GetSceneAsync(long sceneId, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<VideoProjectSceneDto>(
                """
                SELECT id AS Id, project_id AS ProjectId, tenant_id AS TenantId, scene_index AS SceneIndex,
                       title AS Title, duration_seconds AS DurationSeconds, scene_prompt AS ScenePrompt,
                       image_prompt AS ImagePrompt, video_prompt AS VideoPrompt, static_image_path AS StaticImagePath,
                       static_image_url AS StaticImageUrl, scene_video_path AS SceneVideoPath, scene_video_url AS SceneVideoUrl,
                       status AS Status, error_message AS ErrorMessage, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM video_render.video_project_scenes
                 WHERE id=@sceneId AND tenant_id=@tenant;
                """,
                new { sceneId, tenant = _tenant.TenantId });
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_get_scene"))
        {
            throw;
        }
    }

    public async Task AddProjectEventAsync(long projectId, string eventType, string level, string message, object? data = null, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.video_project_events
                    (project_id, tenant_id, event_type, level, message, data_json, created_at)
                VALUES
                    (@projectId, @tenant, @eventType, @level, @message, CAST(@data AS jsonb), now());
                """,
                new
                {
                    projectId,
                    tenant = _tenant.TenantId,
                    eventType,
                    level,
                    message,
                    data = JsonSerializer.Serialize(data ?? new { }, JsonOptions)
                });
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_add_event"))
        {
            throw;
        }
    }

    public async Task UpdateProjectAsync(long projectId, string status, string? finalVideoUrl = null, string? finalVideoPath = null, string? errorMessage = null, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                UPDATE video_render.video_projects
                   SET status=@status,
                       final_video_url=COALESCE(@url, final_video_url),
                       final_video_path=COALESCE(@path, final_video_path),
                       error_message=@err,
                       updated_at=now()
                 WHERE id=@projectId AND tenant_id=@tenant;
                """,
                new { projectId, tenant = _tenant.TenantId, status, url = finalVideoUrl, path = finalVideoPath, err = errorMessage });
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_update_project"))
        {
            throw;
        }
    }

    public async Task UpdateSceneAsync(long sceneId, string status, string? imageUrl = null, string? imagePath = null, string? videoUrl = null, string? videoPath = null, string? errorMessage = null, string? title = null, string? scenePrompt = null, string? imagePrompt = null, string? videoPrompt = null, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                UPDATE video_render.video_project_scenes
                   SET status=@status,
                       static_image_url=COALESCE(@imageUrl, static_image_url),
                       static_image_path=COALESCE(@imagePath, static_image_path),
                       scene_video_url=COALESCE(@videoUrl, scene_video_url),
                       scene_video_path=COALESCE(@videoPath, scene_video_path),
                       error_message=@errorMessage,
                       title=COALESCE(@title, title),
                       scene_prompt=COALESCE(@scenePrompt, scene_prompt),
                       image_prompt=COALESCE(@imagePrompt, image_prompt),
                       video_prompt=COALESCE(@videoPrompt, video_prompt),
                       updated_at=now()
                 WHERE id=@sceneId AND tenant_id=@tenant;
                """,
                new { sceneId, tenant = _tenant.TenantId, status, imageUrl, imagePath, videoUrl, videoPath, errorMessage, title, scenePrompt, imagePrompt, videoPrompt });
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_update_scene"))
        {
            throw;
        }
    }

    public async Task SaveSceneDraftAsync(VideoProjectSaveSceneDraftRequest request, Guid? selectedBy, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            var scene = await conn.QuerySingleOrDefaultAsync<VideoProjectSceneDto>(
                """
                SELECT id AS Id, project_id AS ProjectId, tenant_id AS TenantId, scene_index AS SceneIndex
                  FROM video_render.video_project_scenes
                 WHERE id=@sceneId AND tenant_id=@tenant
                 FOR UPDATE;
                """,
                new { sceneId = request.SceneId, tenant = _tenant.TenantId }, tx);
            if (scene is null)
            {
                tx.Rollback();
                throw new InvalidOperationException("Không tìm thấy scene render video.");
            }

            if (request.SelectedImageVersionId is Guid imageVersionId)
            {
                var imageVersion = await conn.QuerySingleAsync<SceneVersionProjection>(
                    """
                    SELECT public_url AS PublicUrl, source_file_path AS SourceFilePath, storage_key AS StorageKey
                      FROM video_render.scene_image_versions
                     WHERE id=@versionId AND scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant AND status='completed'
                     FOR UPDATE;
                    """,
                    new { versionId = imageVersionId, sceneId = scene.Id, projectId = scene.ProjectId, tenant = _tenant.TenantId }, tx);
                await conn.ExecuteAsync("UPDATE video_render.scene_image_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;", new { sceneId = scene.Id, projectId = scene.ProjectId, tenant = _tenant.TenantId }, tx);
                await conn.ExecuteAsync(
                    "UPDATE video_render.scene_image_versions SET is_selected=true, selected_at=now(), selected_by=@selectedBy, updated_at=now() WHERE id=@versionId AND tenant_id=@tenant;",
                    new { versionId = imageVersionId, tenant = _tenant.TenantId, selectedBy }, tx);
                request.ImageUrl = imageVersion.PublicUrl ?? request.ImageUrl;
                request.ImagePath = imageVersion.SourceFilePath ?? imageVersion.StorageKey ?? request.ImagePath;
            }

            if (request.SelectedVideoVersionId is Guid videoVersionId)
            {
                var videoVersion = await conn.QuerySingleAsync<SceneVersionProjection>(
                    """
                    SELECT public_url AS PublicUrl, source_file_path AS SourceFilePath, storage_key AS StorageKey
                      FROM video_render.scene_video_versions
                     WHERE id=@versionId AND scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant AND status='completed'
                     FOR UPDATE;
                    """,
                    new { versionId = videoVersionId, sceneId = scene.Id, projectId = scene.ProjectId, tenant = _tenant.TenantId }, tx);
                await conn.ExecuteAsync("UPDATE video_render.scene_video_versions SET is_selected=false WHERE scene_id=@sceneId AND project_id=@projectId AND tenant_id=@tenant;", new { sceneId = scene.Id, projectId = scene.ProjectId, tenant = _tenant.TenantId }, tx);
                await conn.ExecuteAsync(
                    "UPDATE video_render.scene_video_versions SET is_selected=true, selected_at=now(), selected_by=@selectedBy, updated_at=now() WHERE id=@versionId AND tenant_id=@tenant;",
                    new { versionId = videoVersionId, tenant = _tenant.TenantId, selectedBy }, tx);
                request.VideoUrl = videoVersion.PublicUrl ?? request.VideoUrl;
                request.VideoPath = videoVersion.SourceFilePath ?? videoVersion.StorageKey ?? request.VideoPath;
            }

            await conn.ExecuteAsync(
                """
                UPDATE video_render.video_project_scenes
                   SET title=COALESCE(@title, title),
                       scene_prompt=@scenePrompt,
                       image_prompt=@imagePrompt,
                       video_prompt=@videoPrompt,
                       static_image_url=COALESCE(@imageUrl, static_image_url),
                       static_image_path=COALESCE(@imagePath, static_image_path),
                       scene_video_url=COALESCE(@videoUrl, scene_video_url),
                       scene_video_path=COALESCE(@videoPath, scene_video_path),
                       selected_image_version_id=COALESCE(@selectedImageVersionId, selected_image_version_id),
                       selected_video_version_id=COALESCE(@selectedVideoVersionId, selected_video_version_id),
                       status=@status,
                       error_message=@errorMessage,
                       updated_at=now()
                 WHERE id=@sceneId AND tenant_id=@tenant;
                """,
                new
                {
                    sceneId = scene.Id,
                    tenant = _tenant.TenantId,
                    title = request.Title,
                    scenePrompt = request.ScenePrompt,
                    imagePrompt = request.ImagePrompt,
                    videoPrompt = request.VideoPrompt,
                    imageUrl = request.ImageUrl,
                    imagePath = request.ImagePath,
                    videoUrl = request.VideoUrl,
                    videoPath = request.VideoPath,
                    selectedImageVersionId = request.SelectedImageVersionId,
                    selectedVideoVersionId = request.SelectedVideoVersionId,
                    status = string.IsNullOrWhiteSpace(request.Status) ? VideoSceneStatuses.Draft : request.Status,
                    errorMessage = request.ErrorMessage
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.video_project_events
                    (project_id, tenant_id, event_type, level, message, data_json, created_at)
                VALUES
                    (@projectId, @tenant, 'SCENE_SAVED', 'info', 'Video scene draft saved atomically.', CAST(@data AS jsonb), now());
                """,
                new
                {
                    projectId = scene.ProjectId,
                    tenant = _tenant.TenantId,
                    data = JsonSerializer.Serialize(new { sceneId = scene.Id, request.SceneIndex, request.SelectedImageVersionId, request.SelectedVideoVersionId, request.AspectRatio }, JsonOptions)
                }, tx);

            tx.Commit();
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_save_scene_draft"))
        {
            throw;
        }
    }

    public async Task<List<VideoProjectSceneDto>> ReplaceScenesAsync(long projectId, IEnumerable<VideoProjectSceneDto> scenes, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync("DELETE FROM video_render.video_project_scenes WHERE project_id=@projectId AND tenant_id=@tenant;", new { projectId, tenant = _tenant.TenantId }, tx);
            var list = scenes.ToList();
            foreach (var scene in list)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO video_render.video_project_scenes
                        (project_id, tenant_id, scene_index, title, duration_seconds, scene_prompt, image_prompt, video_prompt,
                         static_image_path, static_image_url, scene_video_path, scene_video_url, status, error_message, created_at, updated_at)
                    VALUES
                        (@projectId, @tenant, @sceneIndex, @title, @duration, @scenePrompt, @imagePrompt, @videoPrompt,
                         @staticImagePath, @staticImageUrl, @sceneVideoPath, @sceneVideoUrl, @status, @errorMessage, now(), now());
                    """,
                    new
                    {
                        projectId,
                        tenant = _tenant.TenantId,
                        sceneIndex = scene.SceneIndex,
                        title = scene.Title,
                        duration = scene.DurationSeconds,
                        scenePrompt = scene.ScenePrompt,
                        imagePrompt = scene.ImagePrompt,
                        videoPrompt = scene.VideoPrompt,
                        staticImagePath = scene.StaticImagePath,
                        staticImageUrl = scene.StaticImageUrl,
                        sceneVideoPath = scene.SceneVideoPath,
                        sceneVideoUrl = scene.SceneVideoUrl,
                        status = string.IsNullOrWhiteSpace(scene.Status) ? VideoSceneStatuses.Draft : scene.Status,
                        errorMessage = scene.ErrorMessage
                    }, tx);
            }

            tx.Commit();
            return list;
        }
        catch (Exception ex) when (DbDiagnostics.LogPostgresException(_logger, ex, "video_render_replace_scenes"))
        {
            throw;
        }
    }

    private static string BuildScenePrompt(string originalPrompt, int sceneIndex, int sceneCount, int durationSeconds, bool thinkScenes, string aspectRatio)
    {
        var text = string.IsNullOrWhiteSpace(originalPrompt) ? "TodoX AI video" : originalPrompt.Trim();
        if (!thinkScenes)
        {
            return text;
        }

        return $"Scene {sceneIndex}/{sceneCount}, duration {durationSeconds}s. Based on: {text}. Make the scene clear, visual, {AspectRatioLabel(aspectRatio)}, with distinct action and camera movement.";
    }

    private static string NormalizeAspectRatio(string? aspectRatio)
        => string.Equals(aspectRatio, "16:9", StringComparison.Ordinal) ? "16:9" : "9:16";

    private static string AspectRatioLabel(string aspectRatio)
        => string.Equals(aspectRatio, "16:9", StringComparison.Ordinal) ? "horizontal 16:9" : "vertical 9:16";

    private static bool CanAccessProject(VideoProjectDto project, CurrentUserSession user)
        => CanCrossCustomer(user)
           || project.UserId == user.UserId
           || (user.CustomerId is not null && project.CustomerId == user.CustomerId);

    private static bool CanCrossCustomer(CurrentUserSession user)
        => user.IsRoot
           || user.Can("video.render.manage")
           || user.Can("render.video.manage")
           || user.Can("ai.video.version.manage");

    private sealed class SceneVersionProjection
    {
        public string? PublicUrl { get; init; }
        public string? SourceFilePath { get; init; }
        public string? StorageKey { get; init; }
    }
}
