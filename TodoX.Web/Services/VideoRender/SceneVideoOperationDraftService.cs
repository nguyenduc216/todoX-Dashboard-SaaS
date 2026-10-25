using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed record SceneVideoSupersedeAcknowledgement(
    long ProjectId, long SceneId, Guid OldJobId, Guid OldVersionId,
    string OldLogicalRequestId, long ExpectedGeneration,
    string OldJobSnapshotHash, string OldVersionSnapshotHash,
    Guid IdempotencyKey, bool RiskAcknowledged, string Reason);

public sealed record SceneVideoOperationDraft(
    Guid TransitionId, Guid JobId, Guid VersionId, long ProposedGeneration,
    string LogicalRequestId, bool Replayed)
{
    // A prepared draft is NOT a fence, an active generation, or authorization to submit.
    public bool SafeRerenderReady => false;
}

/// <summary>
/// Two resumable atomic stages: prepare an unclaimable draft, then CAS the durable head and queue it.
/// Historical outcomes/identities are never rewritten; the activated transition is the fencing proof.
/// </summary>
public sealed class SceneVideoOperationDraftService(TodoXConnectionFactory factory, TenantContext tenant)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Validate(SceneVideoSupersedeAcknowledgement a, CurrentUserSession actor)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsAuthenticated || actor.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated actor required.");
        if (!a.RiskAcknowledged || a.IdempotencyKey == Guid.Empty || a.ProjectId <= 0 || a.SceneId <= 0
            || a.OldJobId == Guid.Empty || a.OldVersionId == Guid.Empty || a.ExpectedGeneration < 0
            || a.ExpectedGeneration == long.MaxValue || string.IsNullOrWhiteSpace(a.OldLogicalRequestId)
            || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 1000
            || !IsMd5(a.OldJobSnapshotHash) || !IsMd5(a.OldVersionSnapshotHash))
            throw new ArgumentException("Exact acknowledgement, row snapshots, idempotency key and risk consent required.");
    }

    public static string RequestHash(SceneVideoSupersedeAcknowledgement a, Guid actorId, string inputJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { acknowledgement = a, actorId, inputJson }, JsonOptions))));

    public async Task<SceneVideoOperationDraft> PrepareAsync(SceneVideoSupersedeAcknowledgement a,
        CurrentUserSession actor, SceneVideoRenderWorkItemInput proposedInput, CancellationToken ct = default)
    {
        Validate(a, actor);
        ArgumentNullException.ThrowIfNull(proposedInput);
        // Snapshot before awaiting: a caller cannot mutate the object after its idempotency hash is computed.
        var inputJson = JsonSerializer.Serialize(proposedInput, JsonOptions);
        var input = JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(inputJson, JsonOptions)!;
        Guid? sourceId = input.SelectedSourceImageVersionId;
        if (input.ProjectId != a.ProjectId || input.SceneId != a.SceneId || string.IsNullOrWhiteSpace(input.VideoPrompt)
            || (sourceId is null && string.IsNullOrWhiteSpace(input.SourceImageUrl) && string.IsNullOrWhiteSpace(input.SourceImageObjectKey)
                && string.IsNullOrWhiteSpace(input.SharedReferenceImageUrl) && input.SharedReferenceImageMediaId is null))
            throw new ArgumentException("Draft requires matching scene, video prompt and a usable source image.");
        var hashInput = JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(inputJson, JsonOptions)!;
        hashInput.TrustedPayerContext = null; // Rebuilt server-side; a timestamp must not break transport replay.
        var requestHash = RequestHash(a, actor.UserId, JsonSerializer.Serialize(hashInput, JsonOptions));
        await tenant.EnsureLoadedAsync(ct);
        using var conn = await factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s';", transaction: tx);
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0));",
            new { key = RenderJobService.BuildSceneJobLockName(SceneVideoRenderHandler.JobTypeName, a.SceneId) }, tx);
        var args = new { tenant = tenant.TenantId, project = a.ProjectId, scene = a.SceneId,
            actor = actor.UserId, customer = actor.CustomerId,
            manager = actor.IsRoot || actor.Can("video.render.manage") || actor.Can("render.video.manage") || actor.Can("ai.video.version.manage") };
        var allowed = await conn.QuerySingleOrDefaultAsync<long?>("""
            SELECT s.id FROM video_render.video_project_scenes s
            JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
            WHERE s.tenant_id=@tenant AND s.id=@scene AND p.id=@project
              AND (@manager OR p.user_id=@actor OR (@customer IS NOT NULL AND p.customer_id=@customer))
            FOR UPDATE OF s,p;
            """, args, tx);
        if (allowed is null) throw new UnauthorizedAccessException("Scene ownership changed or actor has no access.");

        var replay = await conn.QuerySingleOrDefaultAsync<DraftRow>("""
            SELECT id AS TransitionId,new_job_id AS JobId,new_version_id AS VersionId,
                   proposed_generation AS ProposedGeneration,new_logical_request_id AS LogicalRequestId,
                   request_hash AS RequestHash, actor_id AS ActorId, transition_state AS State
            FROM video_render.scene_video_operation_transitions
            WHERE tenant_id=@tenant AND scene_id=@scene AND idempotency_key=@key;
            """, new { tenant = tenant.TenantId, scene = a.SceneId, key = a.IdempotencyKey }, tx);
        if (replay is not null)
        {
            if (replay.ActorId != actor.UserId || replay.RequestHash != requestHash)
                throw new InvalidOperationException("Idempotency key belongs to a different acknowledgement or actor.");
            if (replay.State == "ACTIVATED")
            {
                tx.Commit();
                return new(replay.TransitionId, replay.JobId, replay.VersionId, replay.ProposedGeneration, replay.LogicalRequestId, true);
            }
            var intact = await conn.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM render.render_jobs j JOIN video_render.scene_video_versions v ON v.render_job_id=j.id
                    WHERE j.id=@job AND j.tenant_id=@tenant AND j.status='draft' AND v.id=@version
                      AND v.tenant_id=@tenant AND v.scene_id=@scene AND v.project_id=@project
                      AND v.logical_request_id=@logical AND v.status='queued' AND NOT v.is_selected);
                """, new { job = replay.JobId, version = replay.VersionId, tenant = tenant.TenantId,
                    scene = a.SceneId, project = a.ProjectId, logical = replay.LogicalRequestId }, tx);
            if (!intact) throw new InvalidOperationException("Prepared draft was changed; replay refused.");
            tx.Commit();
            return new(replay.TransitionId, replay.JobId, replay.VersionId, replay.ProposedGeneration, replay.LogicalRequestId, true);
        }

        // A crash leaves a known, unclaimable PREPARED draft. A fresh consent may replace it ONLY if its
        // acknowledged old snapshot actually changed. Same-snapshot concurrent sessions must conflict, not create two drafts.
        var stalePreparation = await conn.QuerySingleOrDefaultAsync<StalePreparation>("""
            SELECT id AS Id,new_job_id AS JobId,md5(old_job_snapshot::text) AS JobHash,md5(old_version_snapshot::text) AS VersionHash
            FROM video_render.scene_video_operation_transitions WHERE tenant_id=@tenant AND scene_id=@scene
              AND old_generation=@generation AND transition_state='PREPARED' FOR UPDATE;
            """, new { tenant=tenant.TenantId,scene=a.SceneId,generation=a.ExpectedGeneration },tx);
        if (stalePreparation is not null)
        {
            if (string.Equals(stalePreparation.JobHash,a.OldJobSnapshotHash,StringComparison.OrdinalIgnoreCase)
                && string.Equals(stalePreparation.VersionHash,a.OldVersionSnapshotHash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Another consent already prepared this generation. Refresh instead of creating a second operation.");
            var cancelled = await conn.ExecuteAsync("""
                UPDATE render.render_jobs SET status='cancelled' WHERE id=@job AND tenant_id=@tenant AND status='draft'
                  AND provider_task_id IS NULL AND provider_video_id_base IS NULL
                  AND NOT EXISTS(SELECT 1 FROM render.scene_video_submit_intents WHERE tenant_id=@tenant AND render_job_id=@job);
                """,new {job=stalePreparation.JobId,tenant=tenant.TenantId},tx);
            if(cancelled!=1) throw new InvalidOperationException("Prepared operation is no longer safely abandonable.");
            await conn.ExecuteAsync("UPDATE video_render.scene_video_operation_transitions SET transition_state='ABANDONED',abandoned_at=now(),abandonment_reason='Fresh user consent after acknowledged snapshot drift; unclaimable draft retained' WHERE id=@id;",new{id=stalePreparation.Id},tx);
        }

        // Lock jobs before their owned versions, consistent with guarded enqueue.
        var oldJobJson = await conn.QuerySingleAsync<string>("""
            SELECT to_jsonb(j)::text FROM render.render_jobs j
            WHERE j.id=@job AND j.tenant_id=@tenant AND j.job_type='render_scene_video'
              AND COALESCE(j.input_json->>'projectId',j.input_json->>'project_id')=@project
              AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=@scene
              AND md5(to_jsonb(j)::text)=@hash FOR UPDATE;
            """, new { job = a.OldJobId, tenant = tenant.TenantId, project = a.ProjectId.ToString(),
                scene = a.SceneId.ToString(), hash = a.OldJobSnapshotHash.ToLowerInvariant() }, tx);
        var oldVersionJson = await conn.QuerySingleAsync<string>("""
            SELECT to_jsonb(v)::text FROM video_render.scene_video_versions v
            WHERE v.id=@version AND v.render_job_id=@job AND v.tenant_id=@tenant AND v.scene_id=@scene
              AND v.project_id=@project AND v.logical_request_id=@logical AND md5(to_jsonb(v)::text)=@hash FOR UPDATE;
            """, new { version = a.OldVersionId, job = a.OldJobId, tenant = tenant.TenantId, scene = a.SceneId,
                project = a.ProjectId, logical = a.OldLogicalRequestId, hash = a.OldVersionSnapshotHash.ToLowerInvariant() }, tx);
        var source = await conn.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant
                AND project_id=@project AND scene_id=@scene AND status='completed' FOR SHARE);
            """, new { id = sourceId, tenant = tenant.TenantId, project = a.ProjectId, scene = a.SceneId }, tx);
        if (sourceId is not null && !source) throw new InvalidOperationException("Source image is not a completed version of this scene.");
        var competing = await conn.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM render.render_jobs j WHERE j.tenant_id=@tenant AND j.job_type='render_scene_video'
                AND NOT video_render.scene_video_job_is_superseded(j.tenant_id,j.id)
                AND j.id<>@job AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=@scene
                AND j.status NOT IN('completed','failed','cancelled'))
              OR EXISTS(SELECT 1 FROM video_render.scene_video_versions v WHERE v.tenant_id=@tenant AND v.scene_id=@sceneId
                AND NOT video_render.scene_video_job_is_superseded(v.tenant_id,v.render_job_id)
                AND NOT EXISTS(SELECT 1 FROM video_render.scene_video_operation_transitions abandoned WHERE abandoned.new_version_id=v.id AND abandoned.tenant_id=v.tenant_id AND abandoned.transition_state='ABANDONED')
                AND v.id<>@version AND v.status NOT IN('completed','failed') AND v.render_job_id IS DISTINCT FROM @job);
            """, new { tenant = tenant.TenantId, job = a.OldJobId, version = a.OldVersionId,
                scene = a.SceneId.ToString(), sceneId = a.SceneId }, tx);
        if (competing) throw new InvalidOperationException("Unacknowledged competing operation exists.");
        if (a.ExpectedGeneration == 0)
            await conn.ExecuteAsync("""
                INSERT INTO video_render.scene_video_operation_heads
                    (tenant_id,project_id,scene_id,generation,active_render_job_id,active_video_version_id)
                VALUES(@tenant,@project,@scene,0,@job,@version) ON CONFLICT(tenant_id,scene_id) DO NOTHING;
                """, new { tenant = tenant.TenantId, project = a.ProjectId, scene = a.SceneId, job = a.OldJobId, version = a.OldVersionId }, tx);
        var expectedHead = await conn.QuerySingleOrDefaultAsync<long?>("""
            SELECT generation FROM video_render.scene_video_operation_heads WHERE tenant_id=@tenant AND scene_id=@scene
                AND project_id=@project AND generation=@generation AND active_render_job_id=@job AND active_video_version_id=@version
            FOR UPDATE;
            """, new { tenant = tenant.TenantId, project = a.ProjectId, scene = a.SceneId,
                generation = a.ExpectedGeneration, job = a.OldJobId, version = a.OldVersionId }, tx);
        if (expectedHead is null) throw new InvalidOperationException("Acknowledged generation is stale.");

        var transitionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        // FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-RVIDEO-PRICING-SNAPSHOT-002.
        // Recheck one quoted scene/image under existing tenant/scene locks; no historical operation is updated.
        if (input.ConfirmedOperationSettings is { } confirmed)
        {
            var saved = await conn.QuerySingleAsync<VideoProjectSceneDto>("""
                SELECT scene_prompt AS ScenePrompt, image_prompt AS ImagePrompt, video_prompt AS VideoPrompt,
                       duration_seconds AS DurationSeconds, voice_text AS VoiceText, voice_instruction AS VoiceInstruction,
                       static_image_url AS StaticImageUrl, static_image_path AS StaticImagePath
                FROM video_render.video_project_scenes WHERE id=@scene AND project_id=@project AND tenant_id=@tenant;
                """, args, tx);
            if (RVideoSceneVideoOperationSettings.CaptureSceneConfig(saved) != confirmed.SceneConfigJson)
                throw new InvalidOperationException("RVIDEO_QUOTE_CHANGED");
            if (!input.UseSharedReferenceImage)
            {
                var selected = await conn.QuerySingleOrDefaultAsync<Guid?>("""
                    SELECT id FROM video_render.scene_image_versions
                    WHERE scene_id=@scene AND project_id=@project AND tenant_id=@tenant AND is_selected=true LIMIT 1;
                    """, args, tx);
                if (selected != confirmed.SelectedImageVersionId)
                    throw new InvalidOperationException("RVIDEO_QUOTE_CHANGED");
            }
        }

        var generation = checked(a.ExpectedGeneration + 1);
        var logical = $"scene-video-operation-{jobId:N}-scene-{a.SceneId}";
        input.ProjectId = a.ProjectId;
        input.SceneId = a.SceneId;
        input.ParentJobId = jobId;
        input.LogicalRequestId = logical;
        input.ExistingSceneVideoVersionId = versionId;
        input.ReuseExistingSceneVideoVersion = true;
        input.UserId = actor.UserId;
        input.CustomerId = actor.CustomerId;
        input.BillingIntent = PointBillingIntent.UserRerender;
        input.BillingOperationId = jobId;
        input.OperationGeneration = generation;
        await conn.ExecuteAsync("""
            INSERT INTO render.render_jobs(id,tenant_id,user_id,customer_id,job_type,status,priority,
                input_json,prompt_json,reference_json,log_code,point_cost_estimate,point_status,
                provider_code,model_code,max_attempts,queued_at,created_at)
            VALUES(@job,@tenant,@actor,@customer,'render_scene_video','draft',0,CAST(@input AS jsonb),
                '{}'::jsonb,'[]'::jsonb,@log,0,'pending',@provider,@model,1,now(),now());
            """, new { job = jobId, tenant = tenant.TenantId, actor = actor.UserId, customer = actor.CustomerId,
                input = JsonSerializer.Serialize(input, JsonOptions), log = $"video-{a.ProjectId}",
                provider = input.ProviderCode, model = input.ModelName }, tx);
        var storageKey = SceneMediaStorageKeys.SceneVideoOutput(tenant.TenantId, a.ProjectId, a.SceneId, versionId);
        await conn.ExecuteAsync("""
            INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,source_image_version_id,tenant_id,
                customer_id,created_by,version_number,logical_request_id,render_job_id,image_prompt_snapshot,video_prompt_snapshot,
                scene_snapshot_json,render_config_json,storage_key,status,provider_code,requested_model,actual_model,
                provider_capability_id,created_at,updated_at)
            SELECT @version,@project,@scene,@source,@tenant,@customer,@actor,COALESCE(max(version_number),0)+1,
                @logical,@job,@imagePrompt,@videoPrompt,'{}'::jsonb,CAST(@config AS jsonb),@storage,'queued',@provider,@model,@model,
                @capability,now(),now()
            FROM video_render.scene_video_versions WHERE tenant_id=@tenant AND project_id=@project AND scene_id=@scene;
            """, new { version = versionId, project = a.ProjectId, scene = a.SceneId, source = sourceId, tenant = tenant.TenantId,
                customer = actor.CustomerId, actor = actor.UserId, logical, job = jobId, imagePrompt = input.ImagePrompt,
                videoPrompt = input.VideoPrompt, config = JsonSerializer.Serialize(PreparedRenderConfig(input,generation,transitionId), JsonOptions),
                storage = storageKey, provider = input.ProviderCode, model = input.ModelName, capability = input.ProviderCapabilityId }, tx);
        await conn.ExecuteAsync("""
            INSERT INTO video_render.scene_video_operation_transitions(id,tenant_id,project_id,scene_id,old_generation,
                proposed_generation,old_job_id,old_version_id,old_logical_request_id,new_job_id,new_version_id,new_logical_request_id,
                actor_id,idempotency_key,request_hash,risk_acknowledged,reason,old_job_snapshot,old_version_snapshot)
            VALUES(@id,@tenant,@project,@scene,@oldGeneration,@generation,@oldJob,@oldVersion,@oldLogical,@newJob,@newVersion,
                @newLogical,@actor,@key,@hash,true,@reason,CAST(@oldJobJson AS jsonb),CAST(@oldVersionJson AS jsonb));
            """, new { id = transitionId, tenant = tenant.TenantId, project = a.ProjectId, scene = a.SceneId,
                oldGeneration = a.ExpectedGeneration, generation, oldJob = a.OldJobId, oldVersion = a.OldVersionId,
                oldLogical = a.OldLogicalRequestId, newJob = jobId, newVersion = versionId, newLogical = logical, actor = actor.UserId,
                key = a.IdempotencyKey, hash = requestHash, reason = a.Reason, oldJobJson, oldVersionJson }, tx);
        ct.ThrowIfCancellationRequested();
        // Deliberately NO UPDATE old job/version, active head, selected pointer, scene status, billing or queued activation.
        tx.Commit();
        return new(transitionId, jobId, versionId, generation, logical, false);
    }

    public static object PreparedRenderConfig(SceneVideoRenderWorkItemInput input,long generation,Guid transitionId)
        => new { operationGeneration=generation,transitionId,attemptIndex=0,provider=input.ProviderCode,
            capability=input.CapabilityCode,model=input.ModelName,mode=input.RequestedMode,
            aspectRatio=input.AspectRatio,selectedSourceImageVersionId=input.SelectedSourceImageVersionId,
            customerPointRate=input.CustomerPointRate,strictRequestedSettings=input.StrictRequestedSettings,
            confirmedOperationSettings=input.ConfirmedOperationSettings,
            requestedModel=input.RequestedModelCode,requestedMode=input.RequestedMode,
            requestedDurationSeconds=input.DurationSeconds,providerDurationSeconds=input.DurationSeconds,
            requestedResolution=input.Resolution,providerResolution=input.Resolution.Trim().ToLowerInvariant() };

    private static bool IsMd5(string? value)
        => value is { Length: 32 } && value.All(Uri.IsHexDigit);

    public async Task<SceneVideoSupersedeAcknowledgement> ReadAcknowledgementAsync(long projectId, long sceneId,
        CurrentUserSession actor, Guid idempotencyKey, CancellationToken ct = default)
    {
        if (!actor.IsAuthenticated || actor.UserId == Guid.Empty) throw new UnauthorizedAccessException();
        await tenant.EnsureLoadedAsync(ct);
        using var conn = await factory.OpenAsync(ct);
        var row = await conn.QuerySingleAsync<AckRow>("""
            SELECT v.id AS VersionId,j.id AS JobId,v.logical_request_id AS Logical,
                   COALESCE(h.generation,0) AS Generation,md5(to_jsonb(j)::text) AS JobHash,md5(to_jsonb(v)::text) AS VersionHash
            FROM video_render.video_project_scenes s JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
            LEFT JOIN video_render.scene_video_operation_heads h ON h.tenant_id=s.tenant_id AND h.scene_id=s.id
            JOIN LATERAL(SELECT v.* FROM video_render.scene_video_versions v JOIN render.render_jobs j ON j.id=v.render_job_id AND j.tenant_id=v.tenant_id
              WHERE v.scene_id=s.id AND v.tenant_id=s.tenant_id AND j.status<>'draft'
                AND (h.active_render_job_id IS NULL OR (j.id=h.active_render_job_id AND v.id=h.active_video_version_id))
              ORDER BY CASE WHEN j.status='pending_reconciliation' THEN 0 ELSE 1 END,v.version_number DESC LIMIT 1) v ON true
            JOIN render.render_jobs j ON j.id=v.render_job_id AND j.tenant_id=v.tenant_id
            WHERE s.id=@scene AND s.project_id=@project AND s.tenant_id=@tenant
              AND (@manager OR p.user_id=@actor OR (@customer IS NOT NULL AND p.customer_id=@customer));
            """, new { scene = sceneId, project = projectId, tenant = tenant.TenantId, actor = actor.UserId, customer = actor.CustomerId,
                manager = actor.IsRoot || actor.Can("video.render.manage") || actor.Can("render.video.manage") || actor.Can("ai.video.version.manage") });
        return new(projectId, sceneId, row.JobId, row.VersionId, row.Logical, row.Generation,
            row.JobHash, row.VersionHash, idempotencyKey, false, "User explicitly requested a new video operation; previous provider outcome remains UNKNOWN.");
    }

    // Resume after a lost response before rebuilding inputs/pricing from a now-changed scene.
    // The same key always names the original exact consent; it never creates a different operation.
    public async Task<Guid?> TryResumeAsync(SceneVideoSupersedeAcknowledgement a, CurrentUserSession actor, CancellationToken ct=default)
    {
        Validate(a,actor);
        await tenant.EnsureLoadedAsync(ct);
        using var conn=await factory.OpenAsync(ct);
        var transition=await conn.QuerySingleOrDefaultAsync<Guid?>("""
            SELECT id FROM video_render.scene_video_operation_transitions
            WHERE tenant_id=@tenant AND project_id=@project AND scene_id=@scene AND idempotency_key=@key
              AND actor_id=@actor AND old_job_id=@job AND old_version_id=@version
              AND old_generation=@generation AND old_logical_request_id=@logical AND risk_acknowledged
              AND md5(old_job_snapshot::text)=@jobHash AND md5(old_version_snapshot::text)=@versionHash
              AND reason=@reason AND transition_state IN('PREPARED','ACTIVATED');
            """,new{tenant=tenant.TenantId,project=a.ProjectId,scene=a.SceneId,key=a.IdempotencyKey,actor=actor.UserId,
                job=a.OldJobId,version=a.OldVersionId,generation=a.ExpectedGeneration,logical=a.OldLogicalRequestId,
                jobHash=a.OldJobSnapshotHash.ToLowerInvariant(),versionHash=a.OldVersionSnapshotHash.ToLowerInvariant(),reason=a.Reason});
        return transition is Guid id ? await ActivateAsync(a,actor,id,ct) : null;
    }

    public async Task<Guid> ActivateAsync(SceneVideoSupersedeAcknowledgement a, CurrentUserSession actor,
        Guid transitionId, CancellationToken ct = default)
    {
        Validate(a, actor);
        await tenant.EnsureLoadedAsync(ct);
        using var conn = await factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; SELECT pg_advisory_xact_lock(hashtextextended(@key,0));",
            new { key = RenderJobService.BuildSceneJobLockName(SceneVideoRenderHandler.JobTypeName, a.SceneId) }, tx);
        var accessible = await conn.QuerySingleOrDefaultAsync<long?>("""
            SELECT s.id FROM video_render.video_project_scenes s JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
            WHERE s.id=@scene AND s.project_id=@project AND s.tenant_id=@tenant
             AND (@manager OR p.user_id=@actor OR (@customer IS NOT NULL AND p.customer_id=@customer)) FOR UPDATE OF s,p;
            """, new { scene = a.SceneId, project = a.ProjectId, tenant = tenant.TenantId, actor = actor.UserId, customer = actor.CustomerId,
                manager = actor.IsRoot || actor.Can("video.render.manage") || actor.Can("render.video.manage") || actor.Can("ai.video.version.manage") }, tx);
        if (accessible is null) throw new UnauthorizedAccessException();
        var row = await conn.QuerySingleAsync<DraftRow>("""
            SELECT id AS TransitionId,new_job_id AS JobId,new_version_id AS VersionId,proposed_generation AS ProposedGeneration,
                   new_logical_request_id AS LogicalRequestId,actor_id AS ActorId,transition_state AS State
            FROM video_render.scene_video_operation_transitions WHERE id=@id AND tenant_id=@tenant AND scene_id=@scene AND project_id=@project
              AND actor_id=@actor AND idempotency_key=@key AND old_job_id=@oldJob AND old_version_id=@oldVersion
              AND old_generation=@generation AND old_logical_request_id=@logical AND risk_acknowledged
              AND md5(old_job_snapshot::text)=@jobHash AND md5(old_version_snapshot::text)=@versionHash AND reason=@reason FOR UPDATE;
            """, new { id = transitionId, tenant = tenant.TenantId, scene = a.SceneId, project = a.ProjectId, actor = actor.UserId,
                key = a.IdempotencyKey, oldJob = a.OldJobId, oldVersion = a.OldVersionId, generation = a.ExpectedGeneration, logical = a.OldLogicalRequestId, jobHash = a.OldJobSnapshotHash.ToLowerInvariant(),
                versionHash = a.OldVersionSnapshotHash.ToLowerInvariant(), reason = a.Reason }, tx);
        if (row.State == "ACTIVATED") { tx.Commit(); return row.JobId; } // Never reactivate a replayed old operation.
        var currentHead = await conn.QuerySingleAsync<long>("""
            SELECT generation FROM video_render.scene_video_operation_heads WHERE tenant_id=@tenant AND scene_id=@scene
              AND generation=@generation AND active_render_job_id=@oldJob AND active_video_version_id=@oldVersion FOR UPDATE;
            """, new { tenant = tenant.TenantId, scene = a.SceneId, generation = a.ExpectedGeneration, oldJob = a.OldJobId, oldVersion = a.OldVersionId }, tx);
        var valid = await conn.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM render.render_jobs j JOIN video_render.scene_video_versions v ON v.render_job_id=j.id AND v.tenant_id=j.tenant_id
              WHERE j.id=@job AND j.tenant_id=@tenant AND j.job_type='render_scene_video' AND j.status='draft'
                AND j.input_json->>'operationGeneration'=@generation AND j.input_json->>'logicalRequestId'=@logical
                AND j.input_json->>'projectId'=@projectText AND j.input_json->>'sceneId'=@sceneText
                AND v.id=@version AND v.scene_id=@scene AND v.project_id=@project AND v.logical_request_id=@logical
                AND v.status='queued' AND NOT v.is_selected)
              AND EXISTS(SELECT 1 FROM render.render_jobs j WHERE j.id=@oldJob AND j.tenant_id=@tenant AND md5(to_jsonb(j)::text)=@jobHash)
              AND EXISTS(SELECT 1 FROM video_render.scene_video_versions v WHERE v.id=@oldVersion AND v.tenant_id=@tenant AND md5(to_jsonb(v)::text)=@versionHash);
            """, new { job = row.JobId, tenant = tenant.TenantId, generation = row.ProposedGeneration.ToString(), logical = row.LogicalRequestId,
                version = row.VersionId, scene = a.SceneId, project = a.ProjectId, projectText = a.ProjectId.ToString(), sceneText = a.SceneId.ToString(), oldJob = a.OldJobId, oldVersion = a.OldVersionId,
                jobHash = a.OldJobSnapshotHash.ToLowerInvariant(), versionHash = a.OldVersionSnapshotHash.ToLowerInvariant() }, tx);
        if (!valid) throw new InvalidOperationException("Draft ownership or acknowledged snapshot changed; activation refused.");
        var affected = await conn.ExecuteAsync("""
            UPDATE video_render.scene_video_operation_heads SET generation=@generation,active_render_job_id=@job,
              active_video_version_id=@version,updated_at=now() WHERE tenant_id=@tenant AND scene_id=@scene AND generation=@oldGeneration;
            """, new { generation = row.ProposedGeneration, job = row.JobId, version = row.VersionId,
                tenant = tenant.TenantId, scene = a.SceneId, oldGeneration = currentHead }, tx);
        if (affected != 1) throw new InvalidOperationException("Generation CAS refused.");
        await conn.ExecuteAsync("UPDATE video_render.scene_video_operation_transitions SET transition_state='ACTIVATED',activated_at=now() WHERE id=@id AND transition_state='PREPARED';",
            new { id = transitionId }, tx);
        affected = await conn.ExecuteAsync("UPDATE render.render_jobs SET status='queued',queued_at=now() WHERE id=@job AND tenant_id=@tenant AND status='draft';",
            new { job = row.JobId, tenant = tenant.TenantId }, tx);
        if (affected != 1) throw new InvalidOperationException("Draft activation CAS refused.");
        await conn.ExecuteAsync("SELECT set_config('todox.video_job',@job,true);", new { job = row.JobId.ToString() }, tx);
        affected = await conn.ExecuteAsync("""
            UPDATE video_render.video_project_scenes SET status='video_queued' WHERE tenant_id=@tenant AND id=@scene
              AND EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads h WHERE h.tenant_id=@tenant AND h.scene_id=@scene
                AND h.generation=@generation AND h.active_render_job_id=@job);
            """, new { tenant = tenant.TenantId, scene = a.SceneId, generation = row.ProposedGeneration, job = row.JobId }, tx);
        if (affected != 1) throw new InvalidOperationException("Scene generation activation CAS refused.");
        await conn.ExecuteAsync("UPDATE video_render.video_projects SET status='rendering' WHERE tenant_id=@tenant AND id=@project;",
            new { tenant = tenant.TenantId, project = a.ProjectId }, tx);
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return row.JobId;
    }

    private sealed class DraftRow
    {
        public Guid TransitionId { get; set; }
        public Guid JobId { get; set; }
        public Guid VersionId { get; set; }
        public long ProposedGeneration { get; set; }
        public string LogicalRequestId { get; set; } = "";
        public string RequestHash { get; set; } = "";
        public Guid ActorId { get; set; }
        public string State { get; set; } = "";
    }

    private sealed record StalePreparation(Guid Id, Guid JobId, string JobHash, string VersionHash);

    private sealed class AckRow
    {
        public Guid JobId { get; set; } public Guid VersionId { get; set; }
        public string Logical { get; set; } = ""; public long Generation { get; set; }
        public string JobHash { get; set; } = ""; public string VersionHash { get; set; } = "";
    }
}
