using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public VideoRenderRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<VideoProjectDto> CreateProjectAsync(VideoProjectCreateRequest request, Guid? userId, Guid? customerId, string storageRoot, string publicBase, string jobFolder, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var sceneSeconds = Math.Max(1, request.SceneSeconds);
        var totalSeconds = Math.Max(sceneSeconds, request.TotalSeconds);
        var sceneCount = Math.Max(1, (int)Math.Ceiling(totalSeconds / (double)sceneSeconds));

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
            var duration = index == sceneCount
                ? Math.Max(1, totalSeconds - sceneSeconds * (sceneCount - 1))
                : sceneSeconds;
            var scenePrompt = BuildScenePrompt(request.Prompt, index, sceneCount, duration, request.ThinkScenes);

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
                    title = $"Scene {index:00}",
                    duration,
                    scenePrompt,
                    imagePrompt = $"Static preview for scene {index}. {scenePrompt}",
                    videoPrompt = $"Vertical 9:16 video, {duration} seconds. {scenePrompt}",
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

    public async Task<VideoProjectDto?> GetProjectAsync(long projectId, CancellationToken ct = default)
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

    public async Task<VideoProjectSceneDto?> GetSceneAsync(long sceneId, CancellationToken ct = default)
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

    public async Task AddProjectEventAsync(long projectId, string eventType, string level, string message, object? data = null, CancellationToken ct = default)
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

    public async Task UpdateProjectAsync(long projectId, string status, string? finalVideoUrl = null, string? finalVideoPath = null, string? errorMessage = null, CancellationToken ct = default)
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

    public async Task UpdateSceneAsync(long sceneId, string status, string? imageUrl = null, string? imagePath = null, string? videoUrl = null, string? videoPath = null, string? errorMessage = null, string? title = null, string? scenePrompt = null, string? imagePrompt = null, string? videoPrompt = null, CancellationToken ct = default)
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

    public async Task<List<VideoProjectSceneDto>> ReplaceScenesAsync(long projectId, IEnumerable<VideoProjectSceneDto> scenes, CancellationToken ct = default)
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

    private static string BuildScenePrompt(string originalPrompt, int sceneIndex, int sceneCount, int durationSeconds, bool thinkScenes)
    {
        var text = string.IsNullOrWhiteSpace(originalPrompt) ? "TodoX AI video" : originalPrompt.Trim();
        if (!thinkScenes)
        {
            return text;
        }

        return $"Scene {sceneIndex}/{sceneCount}, duration {durationSeconds}s. Based on: {text}. Make the scene clear, visual, vertical 9:16, with distinct action and camera movement.";
    }
}
