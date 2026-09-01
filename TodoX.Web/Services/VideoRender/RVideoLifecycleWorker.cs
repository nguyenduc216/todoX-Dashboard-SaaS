using Dapper;
using System.Text.Json;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public sealed class RVideoLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RVideoLifecycleWorker> _logger;

    public RVideoLifecycleWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RVideoLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("RVIDEO lifecycle worker is disabled because RenderQueue:Enabled=false.");
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Clamp(_configuration.GetValue("RVideo:LifecycleIntervalSeconds", 10), 3, 120));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
                await tenant.EnsureLoadedAsync(stoppingToken);
                var factory = scope.ServiceProvider.GetRequiredService<TodoXConnectionFactory>();
                var settings = await ListAutoSettingsAsync(factory, tenant, stoppingToken);
                var repository = scope.ServiceProvider.GetRequiredService<VideoRenderRepository>();
                var rvideoJobs = scope.ServiceProvider.GetRequiredService<IRVideoJobService>();
                var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
                var audioAutoChain = scope.ServiceProvider.GetRequiredService<IRVideoSceneAudioAutoChainService>();
                var finalizer = scope.ServiceProvider.GetRequiredService<IRVideoSceneMediaFinalizerService>();
                var autoChain = scope.ServiceProvider.GetRequiredService<IRVideoSceneVideoAutoChainService>();
                var catalog = scope.ServiceProvider.GetRequiredService<IAiStudioCatalogService>();
                var versions = scope.ServiceProvider.GetRequiredService<ISceneMediaVersioningService>();
                var finalization = scope.ServiceProvider.GetRequiredService<IRVideoProjectFinalizationService>();
                foreach (var setting in settings)
                {
                    await EvaluateProjectAsync(setting, repository, rvideoJobs, jobs, audioAutoChain, finalizer, autoChain, versions, finalization, factory, tenant, catalog, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RVIDEO lifecycle evaluation failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task EvaluateProjectAsync(RVideoJobSettingsDto setting, VideoRenderRepository repo, IRVideoJobService rvideoJobs, IRenderJobService jobs, IRVideoSceneAudioAutoChainService audioAutoChain, IRVideoSceneMediaFinalizerService finalizer, IRVideoSceneVideoAutoChainService autoChain, ISceneMediaVersioningService versions, IRVideoProjectFinalizationService finalization, TodoXConnectionFactory factory, TenantContext tenant, IAiStudioCatalogService catalog, CancellationToken ct)
    {
        var project = await repo.GetProjectAsync(setting.ProjectId, ct);
        if (project is null || project.Scenes.Count == 0) return;
        try
        {
            RVideoRules.ValidateAutoProject(project, setting);
            var settingsRequest = RVideoRules.ToRequest(setting);
            RVideoRules.ValidateActiveVoice(
                await catalog.GetVoiceByCodeAsync(setting.VoiceCatalogCode ?? string.Empty, activeOnly: true, ct),
                settingsRequest);
            RVideoRules.ValidateActiveMusic(
                await catalog.GetMusicByCodeAsync(setting.MusicCatalogCode ?? string.Empty, activeOnly: true, ct),
                settingsRequest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RVVIDEO_AUTO_VALIDATION_FAILED projectId={ProjectId}", setting.ProjectId);
            await repo.AddProjectEventAsync(setting.ProjectId, "RVIDEO_AUTO_VALIDATION_FAILED", "error",
                "RVIDEO AUTO validation failed; automatic rendering is paused until configuration is corrected.",
                new { projectId = setting.ProjectId, error = ex.Message, setting.VoiceCatalogCode, setting.MusicCatalogCode }, ct);
            return;
        }

        await repo.HydrateSceneVoiceMetadataAsync(project, setting, ct);
        project = await repo.GetProjectAsync(setting.ProjectId, ct);
        if (project is null || project.Scenes.Count == 0) return;

        var renderSettings = RVideoRules.ResolveRenderSettings(project.OriginalPrompt);
        var activeSceneIds = await LoadActiveSceneIdsAsync(factory, tenant, project.Id, ct);
        var usesSharedReferenceImage = setting.UseReferenceImageForAllScenes;
        var sceneStates = project.Scenes
            .Select(scene => RVideoSceneLifecycleClassifier.Classify(
                scene,
                project.Events,
                activeSceneIds.ImageSceneIds.Contains(scene.Id),
                activeSceneIds.VideoSceneIds.Contains(scene.Id),
                usesSharedReferenceImage: usesSharedReferenceImage))
            .ToList();
        var libraryAudioPending = false;
        if (RVideoRules.ResolveVoiceMode(setting) == RVideoVoiceModes.Library)
        {
            foreach (var scene in project.Scenes.Where(scene => RVideoRules.RequiresExternalVoice(scene, setting)))
            {
                var selectedVideo = await versions.GetSelectedVideoVersionAsync(scene.Id, ct);
                var selectedAudio = await versions.GetSelectedAudioVersionAsync(scene.Id, ct);
                if (!RVideoRules.IsSceneFinalReady(scene, setting, selectedVideo, selectedAudio))
                {
                    libraryAudioPending = true;
                    break;
                }
            }
        }
        var decision = RVideoRules.Evaluate(setting.ExecutionMode, sceneStates, !string.IsNullOrWhiteSpace(project.FinalVideoUrl), libraryAudioPending);
        var settingsRepo = new RVideoJobSettingsRepository(factory, tenant, catalog);
        if (!string.Equals(setting.CurrentStage, decision.Stage, StringComparison.OrdinalIgnoreCase))
        {
            await settingsRepo.SetStageAsync(setting.ProjectId, decision.Stage, ct);
        }
        await rvideoJobs.SyncLifecycleAsync(project.Id, decision.Stage,
            decision.Stage == RVideoStages.Video && libraryAudioPending && project.Status == VideoProjectStatuses.Draft
                ? VideoProjectStatuses.Rendering
                : project.Status, ct);

        var userId = project.UserId ?? Guid.Empty;
        var isAuto = string.Equals(setting.ExecutionMode, RVideoExecutionModes.Auto, StringComparison.OrdinalIgnoreCase);
        var imageSceneIds = isAuto && !usesSharedReferenceImage
            ? sceneStates
                .Where(RVideoRules.NeedsImageWork)
                .Select(x => x.SceneId)
                .ToArray()
            : Array.Empty<long>();
        if (isAuto
            && !sceneStates.Any(x => x.ImageFailedTerminal && !x.ImageRetryRequested)
            && imageSceneIds.Length > 0)
        {
            RVideoSceneImageReferenceSelection reference;
            try
            {
                reference = RVideoSceneImageReferenceSelection.Resolve(setting);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "RVIDEO_IMAGE_REFERENCE_VALIDATION_FAILED projectId={ProjectId}", project.Id);
                await repo.AddProjectEventAsync(project.Id, "SCENE_IMAGE_REFERENCE_VALIDATION_FAILED", "error",
                    "RVIDEO scene image reference is unavailable.",
                    new { projectId = project.Id, error = ex.Message, setting.CharacterMode, setting.SkipCharacter }, ct);
                return;
            }

            var imageInput = new SceneImageBatchInput
            {
                ProjectId = project.Id,
                CapabilityCode = SceneImageRenderContext.RVideoCapabilityCode,
                ReferenceSource = reference.Source,
                AspectRatio = renderSettings.AspectRatio,
                CharacterId = reference.CharacterId,
                CharacterReferenceObjectKey = reference.ObjectKey,
                CharacterReferenceUrl = reference.Url,
                UserId = userId,
                CustomerId = project.CustomerId,
                OnlyMissingOrFailed = true,
                SceneIds = imageSceneIds
            };
            await jobs.EnqueueForProjectIfNoneActiveAsync(new RenderJobCreateModel
            {
                JobType = SceneImageBatchRenderHandler.JobTypeName,
                UserId = userId,
                CustomerId = project.CustomerId,
                Input = imageInput,
                Prompt = new { projectId = project.Id, source = "rvideo_auto_lifecycle", stage = "image" },
                LogCode = $"video-image-{project.Id}",
                ProviderCode = SceneImageBatchRenderHandler.RoutingProviderCode,
                ModelCode = SceneImageBatchRenderHandler.RoutingModelCode,
                MaxAttempts = 1,
                PointCostEstimate = 0,
                PointStatus = RenderPointStatuses.NotRequired
            }, project.Id, ct);
        }

        var readyScenes = isAuto
            ? sceneStates
                .Where(x => x.IsImageReady || x.UsesSharedReferenceImage)
                .Select(x => x.SceneId)
                .Except(activeSceneIds.VideoSceneIds)
                .ToArray()
            : Array.Empty<long>();
        foreach (var sceneId in project.Scenes
                     .Where(scene => RVideoRules.RequiresExternalVoice(scene, setting))
                     .Select(scene => scene.Id))
        {
            try
            {
                await audioAutoChain.TryEnqueueSceneAudioAsync(project.Id, sceneId, "RVIDEO_LIFECYCLE", ct);
                await finalizer.TryFinalizeSceneMediaAsync(project.Id, sceneId, "RVIDEO_LIFECYCLE", ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RVIDEO_SCENE_AUDIO_LIFECYCLE_FAILED projectId={ProjectId} sceneId={SceneId}",
                    project.Id,
                    sceneId);
                await repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_AUTO_ENQUEUE_FAILED", "error",
                    "Scene audio lifecycle evaluation failed; other scenes will continue.",
                    new { projectId = project.Id, sceneId, reason = "scene_lifecycle_exception", error = ex.Message }, ct);
            }
        }
        foreach (var sceneId in readyScenes)
        {
            await autoChain.TryEnqueueSceneVideoAsync(project.Id, sceneId, "RVIDEO_LIFECYCLE", ct);
        }

        await finalization.TryEnqueueFinalMergeAsync(project.Id, RVideoProjectFinalizationContracts.TriggerAuto, ct);

        if (imageSceneIds.Length == 0 && readyScenes.Length == 0)
        {
            _logger.LogInformation(
                "RVIDEO_IMAGE_REVIEW_HOLD projectId={ProjectId} imagesReady={ImagesReady} videoPending={VideoPending}",
                project.Id, sceneStates.Count(x => x.IsImageReady), decision.ShouldQueueVideo);
        }
    }

    public static string BuildMergeLogicalRequestKey(long projectId)
        => RVideoProjectFinalizationService.BuildMergeLogicalRequestKey(projectId);

    private static async Task<(HashSet<long> ImageSceneIds, HashSet<long> VideoSceneIds)> LoadActiveSceneIdsAsync(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        long projectId,
        CancellationToken ct)
    {
        using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ActiveRenderJobRow>(
            """
            SELECT job_type AS JobType, input_json::text AS InputJson
              FROM render.render_jobs
             WHERE tenant_id=@tenant
               AND job_type = ANY(@jobTypes)
               AND status = ANY(@statuses)
               AND input_json->>'projectId' = CAST(@projectId AS text);
            """,
            new
            {
                tenant = tenant.TenantId,
                projectId,
                jobTypes = new[]
                {
                    SceneImageBatchRenderHandler.JobTypeName,
                    SceneVideoRenderHandler.JobTypeName
                },
                statuses = new[]
                {
                    RenderJobStatuses.Queued,
                    RenderJobStatuses.Preparing,
                    RenderJobStatuses.Rendering,
                    RenderJobStatuses.PostProcessing,
                    RenderJobStatuses.PendingReconciliation
                }
            });

        var imageSceneIds = new HashSet<long>();
        var videoSceneIds = new HashSet<long>();
        foreach (var row in rows)
        {
            var target = row.JobType == SceneImageBatchRenderHandler.JobTypeName
                ? imageSceneIds
                : videoSceneIds;
            var parsed = ReadSceneIds(row.InputJson);
            if (parsed.Count == 0)
            {
                target.Add(-1);
                continue;
            }

            target.UnionWith(parsed);
        }

        if (imageSceneIds.Remove(-1))
        {
            imageSceneIds.UnionWith(await ListProjectSceneIdsAsync(conn, tenant.TenantId, projectId));
        }
        if (videoSceneIds.Remove(-1))
        {
            videoSceneIds.UnionWith(await ListProjectSceneIdsAsync(conn, tenant.TenantId, projectId));
        }

        return (imageSceneIds, videoSceneIds);
    }

    private static async Task<HashSet<long>> ListProjectSceneIdsAsync(
        System.Data.IDbConnection conn,
        Guid tenantId,
        long projectId)
    {
        var ids = await conn.QueryAsync<long>(
            """
            SELECT id
              FROM video_render.video_project_scenes
             WHERE project_id=@projectId AND tenant_id=@tenant;
            """,
            new { projectId, tenant = tenantId });
        return ids.ToHashSet();
    }

    private static HashSet<long> ReadSceneIds(string json)
    {
        var ids = new HashSet<long>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("sceneIds", out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var id))
                    ids.Add(id);
            }
        }
        catch (JsonException)
        {
        }

        return ids;
    }

    private sealed class ActiveRenderJobRow
    {
        public string JobType { get; set; } = string.Empty;
        public string InputJson { get; set; } = "{}";
    }

    private static async Task<IReadOnlyList<RVideoJobSettingsDto>> ListAutoSettingsAsync(TodoXConnectionFactory factory, TenantContext tenant, CancellationToken ct)
    {
        using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RVideoJobSettingsDto>(
            """
            SELECT project_id AS ProjectId, execution_mode AS ExecutionMode, current_stage AS CurrentStage,
                   skip_character AS SkipCharacter, use_reference_image_for_all_scenes AS UseReferenceImageForAllScenes, character_mode AS CharacterMode, selected_character_id AS SelectedCharacterId,
                   character_snapshot_json::text AS CharacterSnapshotJson, voice_mode AS VoiceMode,
                   voice_catalog_code AS VoiceCatalogCode, voice_snapshot_json::text AS VoiceSnapshotJson,
                   default_tts_rate AS DefaultTtsRate, music_catalog_code AS MusicCatalogCode,
                   music_snapshot_json::text AS MusicSnapshotJson, music_volume AS MusicVolume,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM video_render.rvideo_job_settings
             WHERE tenant_id=@tenant
               AND execution_mode IN ('AUTO', 'MANUAL');
            """, new { tenant = tenant.TenantId });
        return rows.ToList();
    }

}
