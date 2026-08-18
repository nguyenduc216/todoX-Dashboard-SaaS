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
                var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
                var catalog = scope.ServiceProvider.GetRequiredService<IAiStudioCatalogService>();
                foreach (var setting in settings)
                {
                    await EvaluateProjectAsync(setting, repository, jobs, factory, tenant, catalog, stoppingToken);
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

    private async Task EvaluateProjectAsync(RVideoJobSettingsDto setting, VideoRenderRepository repo, IRenderJobService jobs, TodoXConnectionFactory factory, TenantContext tenant, IAiStudioCatalogService catalog, CancellationToken ct)
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
            return;
        }

        var renderSettings = RVideoRules.ResolveRenderSettings(project.OriginalPrompt);
        var decision = RVideoRules.Evaluate(setting.ExecutionMode, project.Scenes.Select(x => x.Status).ToList(), !string.IsNullOrWhiteSpace(project.FinalVideoUrl));
        var settingsRepo = new RVideoJobSettingsRepository(factory, tenant, catalog);
        if (!string.Equals(setting.CurrentStage, decision.Stage, StringComparison.OrdinalIgnoreCase))
        {
            await settingsRepo.SetStageAsync(setting.ProjectId, decision.Stage, ct);
        }

        var userId = project.UserId ?? Guid.Empty;
        if (project.Scenes.All(x => x.Status == VideoSceneStatuses.Draft))
        {
            var imageInput = new SceneImageBatchInput
            {
                ProjectId = project.Id,
                AspectRatio = renderSettings.AspectRatio,
                CharacterReferenceObjectKey = ReadSnapshotString(setting.CharacterSnapshotJson, "storageKey"),
                CharacterReferenceUrl = ReadSnapshotString(setting.CharacterSnapshotJson, "fileUrl", "masterImageUrl"),
                UserId = userId,
                CustomerId = project.CustomerId,
                OnlyMissingOrFailed = true
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
        else if (decision.ShouldQueueVideo)
        {
            var sceneIds = project.Scenes.Where(x => x.Status == VideoSceneStatuses.ImageReady).Select(x => x.Id).ToArray();
            if (sceneIds.Length == 0) return;
            await jobs.EnqueueForProjectIfNoneActiveAsync(new RenderJobCreateModel
            {
                JobType = SceneVideoRenderHandler.JobTypeName,
                UserId = userId,
                CustomerId = project.CustomerId,
                Input = new SceneVideoRenderInput
                {
                    ProjectId = project.Id,
                    SceneIds = sceneIds,
                    AspectRatio = renderSettings.AspectRatio,
                    Resolution = renderSettings.Resolution,
                    UserId = userId,
                    CustomerId = project.CustomerId
                },
                Prompt = new { projectId = project.Id, source = "rvideo_auto_lifecycle" },
                LogCode = $"video-{project.Id}",
                ProviderCode = SceneVideoRenderHandler.RoutingProviderCode,
                ModelCode = SceneVideoRenderHandler.RoutingModelCode,
                MaxAttempts = 1
            }, project.Id, ct);
        }
        else if (decision.ShouldFinalize)
        {
            await jobs.EnqueueForProjectIfNoneActiveAsync(new RenderJobCreateModel
            {
                JobType = VideoRenderMergeHandler.JobTypeName,
                UserId = userId,
                CustomerId = project.CustomerId,
                Input = new { projectId = project.Id },
                Prompt = new { projectId = project.Id, source = "rvideo_auto_lifecycle" },
                LogCode = $"video-{project.Id}",
                ProviderCode = "internal_merge",
                ModelCode = "ffmpeg_concat",
                MaxAttempts = 1
            }, project.Id, ct);
        }
    }

    private static async Task<IReadOnlyList<RVideoJobSettingsDto>> ListAutoSettingsAsync(TodoXConnectionFactory factory, TenantContext tenant, CancellationToken ct)
    {
        using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RVideoJobSettingsDto>(
            """
            SELECT project_id AS ProjectId, execution_mode AS ExecutionMode, current_stage AS CurrentStage,
                   skip_character AS SkipCharacter, character_mode AS CharacterMode, selected_character_id AS SelectedCharacterId,
                   character_snapshot_json::text AS CharacterSnapshotJson, voice_mode AS VoiceMode,
                   voice_catalog_code AS VoiceCatalogCode, voice_snapshot_json::text AS VoiceSnapshotJson,
                   default_tts_rate AS DefaultTtsRate, music_catalog_code AS MusicCatalogCode,
                   music_snapshot_json::text AS MusicSnapshotJson, music_volume AS MusicVolume,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM video_render.rvideo_job_settings
             WHERE tenant_id=@tenant AND execution_mode='AUTO';
            """, new { tenant = tenant.TenantId });
        return rows.ToList();
    }

    private static string? ReadSnapshotString(string? json, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }
}
