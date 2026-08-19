using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoJobService
{
    Task<RVideoJobCreatedResult> CreateDraftAsync(RVideoJobCreateRequest request, CurrentUserSession user, string storageRoot, string publicBase, string jobFolder, CancellationToken ct = default);
    Task<RVideoJobView?> GetByJobIdAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task<long?> ResolveProjectIdAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task SyncLifecycleAsync(long projectId, string stage, string projectStatus, CancellationToken ct = default);
}

public sealed class RVideoJobCreateRequest
{
    public Guid ServiceId { get; init; }
    public string ServiceCode { get; init; } = string.Empty;
    public string Title { get; init; } = "RVIDEO";
    public string Prompt { get; init; } = string.Empty;
    public string AspectRatio { get; init; } = "9:16";
    public string Resolution { get; init; } = "720p";
    public int TotalSeconds { get; init; } = 16;
    public int SceneSeconds { get; init; } = 8;
    public bool ThinkScenes { get; init; }
    public RVideoJobSettingsRequest Settings { get; init; } = new();
}

public sealed record RVideoJobCreatedResult(Guid JobId, long ProjectId, string Status, string Route);

public sealed class RVideoJobView
{
    public required CoreJobView CoreJob { get; init; }
    public required VideoProjectDto Project { get; init; }
    public RVideoJobSettingsDto? Settings { get; init; }
}

public sealed class RVideoJobService : IRVideoJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ICoreServiceCatalogService _catalog;
    private readonly VideoRenderRepository _projects;
    private readonly RVideoJobSettingsRepository _settings;

    public RVideoJobService(TodoXConnectionFactory factory, TenantContext tenant, ICoreServiceCatalogService catalog, VideoRenderRepository projects, RVideoJobSettingsRepository settings)
    {
        _factory = factory;
        _tenant = tenant;
        _catalog = catalog;
        _projects = projects;
        _settings = settings;
    }

    public async Task<RVideoJobCreatedResult> CreateDraftAsync(RVideoJobCreateRequest request, CurrentUserSession user, string storageRoot, string publicBase, string jobFolder, CancellationToken ct = default)
    {
        EnsureCustomer(user);
        if (request.ServiceId == Guid.Empty || string.IsNullOrWhiteSpace(request.ServiceCode))
            throw new InvalidOperationException("Dịch vụ RVIDEO chưa được xác định.");
        var service = await _catalog.GetByCodeAsync(request.ServiceCode, ct)
            ?? throw new InvalidOperationException("Dịch vụ RVIDEO không tồn tại.");
        if (service.Id != request.ServiceId || !service.Enabled || !string.Equals(service.ServiceType, TodoXServiceEngineTypes.RVideo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dịch vụ RVIDEO không hợp lệ.");
        RVideoRules.ValidateSettings(request.Settings);
        await _tenant.EnsureLoadedAsync(ct);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));", new { lockName = $"rvideo-draft:{_tenant.TenantId}:{user.CustomerId}:{request.ServiceId}" }, tx);

        var jobId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new
        {
            engine = "RVIDEO", serviceId = service.Id, serviceCode = service.ServiceCode,
            title = request.Title, description = request.Prompt, prompt = request.Prompt,
            aspectRatio = request.AspectRatio, resolution = request.Resolution,
            executionMode = request.Settings.ExecutionMode, character = request.Settings.CharacterSnapshot,
            voice = new { request.Settings.VoiceMode, request.Settings.VoiceCatalogCode, request.Settings.DefaultTtsRate },
            music = new { request.Settings.MusicCatalogCode, request.Settings.MusicVolume },
            request.TotalSeconds, request.SceneSeconds, request.ThinkScenes
        }, JsonOptions);
        await conn.ExecuteAsync("""
            INSERT INTO render.render_jobs
                (id, tenant_id, customer_id, user_id, service_id, job_type, operation_type, source_type,
                 status, current_step, progress_percent, priority, input_json, prompt_json, reference_json,
                 output_json, options, point_cost_estimate, point_cost_charged, point_status, max_attempts, queued_at, created_at)
            VALUES (@jobId,@tenant,@customer,@user,@serviceId,@jobType,@operationType,'dashboard','draft','info',0,100,
                    CAST(@snapshot AS jsonb),CAST(@prompt AS jsonb),'[]'::jsonb,'[]'::jsonb,'{}'::jsonb,0,0,'not_required',1,now(),now());
            """, new { jobId, tenant = _tenant.TenantId, customer = user.CustomerId, user = user.UserId, serviceId = service.Id,
                jobType = RenderJobTypes.CoreService, operationType = service.ServiceType, snapshot, prompt = JsonSerializer.Serialize(new { text = request.Prompt }, JsonOptions) }, tx);

        var projectId = await conn.QuerySingleAsync<long>("""
            INSERT INTO video_render.video_projects
                (tenant_id,user_id,customer_id,core_job_id,title,original_prompt,total_seconds,scene_seconds,scene_count,
                 think_scenes,character_id,storage_root,public_base,job_folder,status,created_at,updated_at)
            VALUES (@tenant,@user,@customer,@jobId,@title,@prompt,@total,@sceneSeconds,0,@think,NULL,@storageRoot,@publicBase,@jobFolder,'draft',now(),now())
            RETURNING id;
            """, new { tenant = _tenant.TenantId, user = user.UserId, customer = user.CustomerId, jobId, title = request.Title,
                prompt = request.Prompt, total = Math.Max(1, request.TotalSeconds), sceneSeconds = Math.Max(1, request.SceneSeconds),
                think = request.ThinkScenes, storageRoot, publicBase, jobFolder }, tx);
        await conn.ExecuteAsync("UPDATE render.render_jobs SET input_json=jsonb_set(input_json,'{projectId}',to_jsonb(@projectId::text),true),updated_at=now() WHERE id=@jobId;", new { projectId, jobId }, tx);
        await conn.ExecuteAsync("""
            INSERT INTO video_render.rvideo_job_settings
                (project_id,tenant_id,execution_mode,current_stage,skip_character,character_mode,selected_character_id,
                 character_snapshot_json,voice_mode,voice_catalog_code,voice_snapshot_json,default_tts_rate,music_catalog_code,music_snapshot_json,music_volume,created_at,updated_at)
            VALUES (@projectId,@tenant,@executionMode,'INFO',@skip,@characterMode,@selected,CAST(@character AS jsonb),@voice,@voiceCode,CAST(@voiceSnapshot AS jsonb),@rate,@musicCode,CAST(@musicSnapshot AS jsonb),@volume,now(),now());
            """, new { projectId, tenant = _tenant.TenantId, executionMode = request.Settings.ExecutionMode, skip = request.Settings.SkipCharacter,
                characterMode = request.Settings.CharacterMode, selected = request.Settings.SelectedCharacterId,
                character = JsonSerializer.Serialize(request.Settings.CharacterSnapshot ?? new { }, JsonOptions), voice = request.Settings.VoiceMode,
                voiceCode = request.Settings.VoiceCatalogCode, voiceSnapshot = JsonSerializer.Serialize(request.Settings.VoiceSnapshot ?? new { }, JsonOptions),
                rate = request.Settings.DefaultTtsRate, musicCode = request.Settings.MusicCatalogCode,
                musicSnapshot = JsonSerializer.Serialize(request.Settings.MusicSnapshot ?? new { }, JsonOptions), volume = request.Settings.MusicVolume }, tx);
        await conn.ExecuteAsync("INSERT INTO render.render_job_events(job_id,tenant_id,event_type,level,message,data_json,created_at) VALUES(@jobId,@tenant,'CORE_JOB_CREATED','info','RVIDEO draft created without billing.','{}'::jsonb,now());", new { jobId, tenant = _tenant.TenantId }, tx);
        tx.Commit();
        return new(jobId, projectId, RenderJobStatuses.Draft, $"/jobs/rvideo/{jobId}");
    }

    public async Task<RVideoJobView?> GetByJobIdAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureCustomer(user);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CoreJobRow>("""
            SELECT r.id AS Id,r.service_id AS ServiceId,r.customer_id AS CustomerId,r.user_id AS UserId,r.status AS Status,
                   r.source_type AS SourceType,r.operation_type AS OperationType,r.logical_request_id AS LogicalRequestId,
                   r.current_step AS CurrentStep,r.progress_percent AS ProgressPercent,r.point_cost_estimate AS PointCostEstimate,
                   r.point_cost_charged AS PointCostCharged,r.point_status AS PointStatus,r.input_json::text AS InputJson,
                   r.output_json::text AS OutputJson,r.error_code AS ErrorCode,r.error_message AS ErrorMessage,r.created_at AS CreatedAt,r.updated_at AS UpdatedAt,r.completed_at AS CompletedAt,
                   s.service_code AS ServiceCode FROM render.render_jobs r JOIN catalog.services s ON s.id=r.service_id
             WHERE r.id=@jobId AND r.tenant_id=@tenant AND r.customer_id=@customer AND r.job_type=@jobType AND r.input_json->>'engine'='RVIDEO';
            """, new { jobId, tenant = _tenant.TenantId, customer = user.CustomerId, jobType = RenderJobTypes.CoreService });
        if (row is null) return null;
        var projectId = await conn.QuerySingleOrDefaultAsync<long?>("SELECT id FROM video_render.video_projects WHERE core_job_id=@jobId AND tenant_id=@tenant;", new { jobId, tenant = _tenant.TenantId });
        if (projectId is null) return null;
        var project = await _projects.GetProjectAsync(projectId.Value, user, ct);
        if (project is null) return null;
        var settings = await _settings.GetAsync(projectId.Value, ct);
        return new() { CoreJob = CoreRowMapper.Map(row), Project = project, Settings = settings };
    }

    public async Task<long?> ResolveProjectIdAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var view = await GetByJobIdAsync(jobId, user, ct);
        return view?.Project.Id;
    }

    public async Task SyncLifecycleAsync(long projectId, string stage, string projectStatus, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var jobId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT core_job_id FROM video_render.video_projects WHERE id=@projectId AND tenant_id=@tenant;",
            new { projectId, tenant = _tenant.TenantId });
        if (jobId is null) return;
        var (status, progress) = projectStatus switch
        {
            VideoProjectStatuses.Completed => (RenderJobStatuses.Completed, 100),
            VideoProjectStatuses.Failed => (RenderJobStatuses.Failed, 100),
            _ => (RenderJobStatuses.Rendering, stage == RVideoStages.Result ? 90 : stage == RVideoStages.Video ? 60 : stage == RVideoStages.Image ? 25 : 5)
        };
        await conn.ExecuteAsync("""
            UPDATE render.render_jobs
               SET status=@status,current_step=@stage,progress_percent=GREATEST(progress_percent,@progress),updated_at=now()
             WHERE id=@jobId AND tenant_id=@tenant AND job_type=@jobType
               AND status NOT IN ('completed','failed','cancelled');
            """, new { jobId, tenant = _tenant.TenantId, jobType = RenderJobTypes.CoreService, status, stage, progress });
    }

    private static void EnsureCustomer(CurrentUserSession user)
    { if (user is not { IsAuthenticated: true, IsCustomer: true } || user.CustomerId is null) throw new UnauthorizedAccessException("Customer authentication is required."); }

    private sealed class CoreJobRow : CoreRowMapper.Row
    { public Guid? ServiceId { get; set; } public Guid? CustomerId { get; set; } public Guid? UserId { get; set; } public string ServiceCode { get; set; } = string.Empty; }
    private static class CoreRowMapper
    {
        internal abstract class Row { public Guid Id { get; set; } public string Status { get; set; } = "draft"; public string SourceType { get; set; } = "dashboard"; public string? OperationType { get; set; } public string? LogicalRequestId { get; set; } public string? CurrentStep { get; set; } public int ProgressPercent { get; set; } public decimal PointCostEstimate { get; set; } public decimal PointCostCharged { get; set; } public string PointStatus { get; set; } = "not_required"; public string InputJson { get; set; } = "{}"; public string OutputJson { get; set; } = "[]"; public string? ErrorCode { get; set; } public string? ErrorMessage { get; set; } public DateTime CreatedAt { get; set; } public DateTime? UpdatedAt { get; set; } public DateTime? CompletedAt { get; set; } }
        internal static CoreJobView Map(CoreJobRow r) { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(r.OutputJson) ? "[]" : r.OutputJson); return new(r.Id,r.ServiceId,r.ServiceCode,r.CustomerId,r.UserId,r.Status,r.SourceType,r.OperationType,r.LogicalRequestId,r.CurrentStep,r.ProgressPercent,r.PointCostEstimate,r.PointCostCharged,r.PointStatus,null,d.RootElement.Clone(),r.ErrorCode,r.ErrorMessage,r.CreatedAt,r.UpdatedAt,r.CompletedAt); }
    }
}
