using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SupersedePostgresFactAttribute : FactAttribute
{
    public SupersedePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")))
            Skip = "Needs an EMPTY isolated loopback PostgreSQL database named todox_supersede_test_<32 hex>. No production fallback.";
    }
}

public sealed class SupersedePostgresTheoryAttribute : TheoryAttribute
{
    public SupersedePostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")))
            Skip = "Requires explicit isolated loopback PostgreSQL opt-in; never falls back to application database.";
    }
}

public sealed class SceneVideoOperationDraftPostgresTests
{
    [SupersedePostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicDraftOwnsNewVersionReplaysConcurrentClickAndRollsBackFailedAudit(bool existingProviderTask)
    {
        var cs = Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")!;
        ValidateIsolatedConnection(cs); // BEFORE opening a connection. Never uses application settings/secrets.
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var tables = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN('pg_catalog','information_schema');");
        Assert.Equal(0, tables); // Refuse existing data, including an old test run. No clean/reset fallback.
        var ownedSchemas = false;
        try
        {
            await conn.ExecuteAsync(FixtureSql);
            ownedSchemas = true;
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-preflight.sql")));
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-forward.sql")));
            var tenantId = Guid.NewGuid(); var actor = SceneVideoOperationDraftTests.Actor(); actor.IsRoot = true;
            var oldJob = Guid.NewGuid(); var oldVersion = Guid.NewGuid(); var selected = Guid.NewGuid(); var source = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO video_render.video_projects(id,tenant_id,user_id,customer_id) VALUES(1,@tenant,@actor,NULL);
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,selected_video_version_id,status,scene_video_url) VALUES(2,1,@tenant,@selectedPointer,'completed','/old.mp4');
                INSERT INTO video_render.scene_image_versions VALUES(@source,1,2,@tenant,'completed');
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,provider_video_id_base)
                VALUES(@job,@tenant,@actor,'render_scene_video','pending_reconciliation','{"projectId":1,"sceneId":2}',@oldTask,@oldBase);
                INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status,is_selected,provider_task_id,provider_video_id_base)
                VALUES(@version,1,2,@tenant,2,'old-request',@job,'pending_reconciliation',false,@oldTask,@oldBase),
                      (@selected,1,2,@tenant,1,'selected-completed',NULL,'completed',@hasSelected,NULL,NULL);
                INSERT INTO billing_fixture VALUES(100);
                """, new { tenant = tenantId, actor = actor.UserId, source, job = oldJob, version = oldVersion, selected,
                    selectedPointer = existingProviderTask ? (Guid?)selected : null, hasSelected = existingProviderTask,
                    oldTask = existingProviderTask ? "old-task" : null, oldBase = existingProviderTask ? "old-base" : null });
            var jobHash = await conn.ExecuteScalarAsync<string>("SELECT md5(to_jsonb(j)::text) FROM render.render_jobs j WHERE id=@id;", new { id = oldJob }) ?? throw new InvalidOperationException("Old job hash missing");
            var versionHash = await conn.ExecuteScalarAsync<string>("SELECT md5(to_jsonb(v)::text) FROM video_render.scene_video_versions v WHERE id=@id;", new { id = oldVersion }) ?? throw new InvalidOperationException("Old version hash missing");
            var sceneBefore = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;");
            var a = SceneVideoOperationDraftTests.Ack() with { OldJobId = oldJob, OldVersionId = oldVersion,
                OldJobSnapshotHash = jobHash, OldVersionSnapshotHash = versionHash };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenantId.ToString() }).Build();
            var factory = new TodoXConnectionFactory(config);
            SceneVideoOperationDraftService Service() => new(factory, new TenantContext(factory, config));
            SceneVideoRenderWorkItemInput Input() => new() { ProjectId = 1, SceneId = 2, VideoPrompt = "Animate scene",
                ImagePrompt = "image", SelectedSourceImageVersionId = source, ProviderCode = "fake", ModelName = "fake-model" };

            // Fail the final ledger insert AFTER job/version allocation: all writes including head must roll back.
            await conn.ExecuteAsync("ALTER TABLE video_render.scene_video_operation_transitions ADD CONSTRAINT injected_failure CHECK(reason<>'injected-failure');");
            await Assert.ThrowsAsync<PostgresException>(() => Service().PrepareAsync(a with { Reason = "injected-failure" }, actor, Input()));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs;"));
            Assert.Equal(2, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_video_versions;"));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_video_operation_heads;"));

            var requests = await Task.WhenAll(Service().PrepareAsync(a, actor, Input()), Service().PrepareAsync(a, actor, Input()));
            Assert.Equal(requests[0].JobId, requests[1].JobId);
            Assert.Equal(requests[0].VersionId, requests[1].VersionId);
            Assert.Single(requests, r => r.Replayed);
            Assert.All(requests, r => Assert.False(r.SafeRerenderReady));
            var jobId = requests[0].JobId; var versionId = requests[0].VersionId;
            Assert.Equal("draft", await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@id;", new { id = jobId }));
            Assert.Equal(jobId, await conn.ExecuteScalarAsync<Guid>("SELECT render_job_id FROM video_render.scene_video_versions WHERE id=@id;", new { id = versionId }));
            Assert.Equal(3, await conn.ExecuteScalarAsync<int>("SELECT version_number FROM video_render.scene_video_versions WHERE id=@id;", new { id = versionId }));
            Assert.Equal(jobHash, await conn.ExecuteScalarAsync<string>("SELECT md5(to_jsonb(j)::text) FROM render.render_jobs j WHERE id=@id;", new { id = oldJob }));
            Assert.Equal(versionHash, await conn.ExecuteScalarAsync<string>("SELECT md5(to_jsonb(v)::text) FROM video_render.scene_video_versions v WHERE id=@id;", new { id = oldVersion }));
            Assert.Equal(sceneBefore, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;"));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));
            Assert.Equal(oldJob, await conn.ExecuteScalarAsync<Guid>("SELECT active_render_job_id FROM video_render.scene_video_operation_heads;"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PrepareAsync(a with { Reason = "changed-consent" }, actor, Input()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().PrepareAsync(a, new CurrentUserSession
                { IsAuthenticated = true, UserId = Guid.NewGuid() }, Input()));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_video_operation_transitions;"));
            var jobs = new RenderJobService(factory,new TenantContext(factory,config),config,NullLogger<RenderJobService>.Instance);
            Assert.Null(await jobs.ClaimNextByJobTypeAsync("draft-probe",TimeSpan.FromMinutes(1),new[]{RenderJobTypes.RenderSceneVideo}));
            // Atomically publish generation before queued; an uncertain old task remains untouched.
            // Inject a failure AFTER head CAS/job queue: activation must roll all state back.
            await conn.ExecuteAsync("ALTER TABLE video_render.video_project_scenes ADD CONSTRAINT activation_failure CHECK(status<>'video_queued');");
            await Assert.ThrowsAsync<PostgresException>(()=>Service().ActivateAsync(a,actor,requests[0].TransitionId));
            Assert.Equal(0,await conn.ExecuteScalarAsync<int>("SELECT generation FROM video_render.scene_video_operation_heads WHERE scene_id=2;"));
            Assert.Equal("draft",await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@job;",new{job=jobId}));
            Assert.Equal("PREPARED",await conn.ExecuteScalarAsync<string>("SELECT transition_state FROM video_render.scene_video_operation_transitions WHERE id=@id;",new{id=requests[0].TransitionId}));
            await conn.ExecuteAsync("ALTER TABLE video_render.video_project_scenes DROP CONSTRAINT activation_failure;");
            var coordinator = new SceneVideoSubmitCoordinator(factory, new TenantContext(factory, config));
            var sendFence = await coordinator.AcquireSubmitFenceAsync(oldJob,CancellationToken.None);
            var activation = Service().ActivateAsync(a, actor, requests[0].TransitionId);
            try
            {
                var deadline=DateTime.UtcNow.AddSeconds(3);
                var waiting=false;
                while(DateTime.UtcNow<deadline && !activation.IsCompleted)
                {
                    waiting=await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND NOT granted AND database=(SELECT oid FROM pg_database WHERE datname=current_database()));");
                    if(waiting) break;
                    await Task.Delay(20);
                }
                Assert.True(waiting,"Activation must wait for the physical-send fence, not cross an in-flight HTTP boundary.");
                Assert.False(activation.IsCompleted);
            }
            finally { sendFence.Dispose(); }
            var activated = await activation;
            await Assert.ThrowsAsync<PostgresException>(()=>coordinator.AcquireSubmitFenceAsync(oldJob,CancellationToken.None));
            Assert.Equal(jobId, activated);
            Assert.Equal("queued", await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@id;", new { id = jobId }));
            Assert.Equal(jobId, await conn.ExecuteScalarAsync<Guid>("SELECT active_render_job_id FROM video_render.scene_video_operation_heads;"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT generation FROM video_render.scene_video_operation_heads;"));
            Assert.Equal(jobId, await Service().ActivateAsync(a, actor, requests[0].TransitionId));
            Assert.Equal(jobId,await Service().TryResumeAsync(a,actor));
            Assert.Equal(jobHash, await conn.ExecuteScalarAsync<string>("SELECT md5(to_jsonb(j)::text) FROM render.render_jobs j WHERE id=@id;", new { id = oldJob }));
            var sceneAfter = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;");
            Assert.Equal(existingProviderTask ? (Guid?)selected : null, await conn.ExecuteScalarAsync<Guid?>("SELECT selected_video_version_id FROM video_render.video_project_scenes WHERE id=2;"));
            Assert.Equal("/old.mp4",await conn.ExecuteScalarAsync<string>("SELECT scene_video_url FROM video_render.video_project_scenes WHERE id=2;"));

            var workerClaim = await jobs.ClaimNextByJobTypeAsync("supersede-worker",TimeSpan.FromMinutes(1),new[]{RenderJobTypes.RenderSceneVideo});
            Assert.NotNull(workerClaim);
            Assert.Equal(jobId,workerClaim.Id);
            await jobs.MarkSceneVideoClaimRenderingAsync(workerClaim,"supersede-worker");
            await jobs.VerifySceneVideoSubmitClaimAsync(workerClaim,versionId,requests[0].LogicalRequestId);

            // A persisted old claim cannot cross BeginSubmit after the head changes.
            var oldToken = Guid.NewGuid();
            await conn.ExecuteAsync("INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token) VALUES(@tenant,2,'old-request',@job,@token);",
                new { tenant = tenantId, job = oldJob, token = oldToken });
            await Assert.ThrowsAsync<PostgresException>(() => coordinator.BeginSubmitAsync(oldToken));
            await coordinator.TryRecordPreHttpFailureAsync(2,"old-request");
            Assert.Equal("SUBMIT_INTENT_PERSISTED",await conn.ExecuteScalarAsync<string>("SELECT submit_state FROM render.scene_video_submit_intents WHERE owner_token=@token;",new{token=oldToken}));
            await Assert.ThrowsAsync<RenderJobPendingReconciliationException>(() => coordinator.ClaimSubmitAsync(1,2,"old-request",oldJob));
            var claim = await coordinator.ClaimSubmitAsync(1,2,requests[0].LogicalRequestId,jobId);
            Assert.Equal(SceneVideoSubmitClaimOutcome.Claimed, claim.Outcome);
            var beginResults=await Task.WhenAll(coordinator.BeginSubmitAsync(claim.OwnerToken!.Value),coordinator.BeginSubmitAsync(claim.OwnerToken.Value));
            Assert.Single(beginResults,r=>r);
            Assert.Single(beginResults,r=>!r);
            await coordinator.AssertJobMaySubmitAsync(jobId,claim.OwnerToken,CancellationToken.None);
            var fakeProviderCalls = 0;
            Task<(string Task,string Base)> FakeSubmit() { fakeProviderCalls++; return Task.FromResult(("new-task","new-base")); }
            var submitted = await FakeSubmit();
            Assert.True(await coordinator.ConfirmSubmittedAsync(claim.OwnerToken.Value,submitted.Task,submitted.Base));
            Assert.Equal(1,fakeProviderCalls);
            Assert.NotEqual(SceneVideoSubmitClaimOutcome.Claimed,(await coordinator.ClaimSubmitAsync(1,2,requests[0].LogicalRequestId,jobId)).Outcome);

            // Database triggers reject late success, late failure and automatic reactivation.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync("UPDATE video_render.scene_video_versions SET status='completed',is_selected=true WHERE id=@id;",new { id=oldVersion }));
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync("UPDATE video_render.scene_video_versions SET status='failed' WHERE id=@id;",new { id=oldVersion }));
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync("UPDATE render.render_jobs SET status='queued' WHERE id=@id;",new { id=oldJob }));
            using(SceneVideoOperationScope.Enter(oldJob))
            {
                using var oldConnection = await factory.OpenAsync();
                await Assert.ThrowsAsync<PostgresException>(() => oldConnection.ExecuteAsync("UPDATE video_render.video_project_scenes SET status='failed',scene_video_url='/late.mp4' WHERE id=2;"));
                await Assert.ThrowsAsync<PostgresException>(() => oldConnection.ExecuteAsync("UPDATE video_render.scene_video_versions SET public_url='/old-mux.mp4' WHERE id=@id;",new { id=versionId }));
                await Assert.ThrowsAsync<PostgresException>(() => oldConnection.ExecuteAsync("INSERT INTO render.render_jobs(id,tenant_id,job_type,status,input_json) VALUES(@id,@tenant,'render_video_batch','queued','{}');",new { id=Guid.NewGuid(),tenant=tenantId }));
            }
            Assert.Equal(sceneAfter, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;"));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));

            var versionsService = new SceneMediaVersioningService(factory,new TenantContext(factory,config),config);
            var completion = new SceneVideoVersionCompleteRequest("/new.mp4","/new.mp4",null,8,"video/mp4",ProviderTaskId:"new-task");
            using(SceneVideoOperationScope.Enter(oldJob))
            {
                await Assert.ThrowsAsync<PostgresException>(()=>versionsService.CompleteSceneVideoVersionAsync(oldVersion,completion));
                await Assert.ThrowsAsync<PostgresException>(()=>versionsService.FailSceneVideoVersionAsync(oldVersion,"late-failure","late failure"));
            }
            using(SceneVideoOperationScope.Enter(jobId))
            {
                await versionsService.CompleteSceneVideoVersionAsync(versionId,completion);
                await versionsService.CompleteSceneVideoVersionAsync(versionId,completion); // duplicate persistence keeps same selected identity
                await conn.ExecuteAsync("UPDATE render.render_jobs SET status='completed' WHERE id=@job;",new {job=jobId});
            }
            var newScene = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;");
            using(SceneVideoOperationScope.Merge("{\"generations\":{\"2\":0},\"selected\":{\"2\":\"\"}}"))
            {
                using var oldMerge = await factory.OpenAsync();
                var final=Guid.NewGuid();
                await oldMerge.ExecuteAsync("INSERT INTO video_render.final_video_versions(id,project_id,tenant_id,status) VALUES(@id,1,@tenant,'queued');",new{id=final,tenant=tenantId});
                await Assert.ThrowsAsync<PostgresException>(()=>oldMerge.ExecuteAsync("UPDATE video_render.final_video_versions SET status='completed',public_url='/stale.mp4' WHERE id=@id;",new{id=final}));
                await Assert.ThrowsAsync<PostgresException>(() => oldMerge.ExecuteAsync("UPDATE video_render.video_projects SET status='completed',final_video_url='/old-merge.mp4' WHERE id=1;"));
            }
            Assert.Equal(newScene, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM video_render.video_project_scenes s WHERE id=2;"));
            // Ordinary image/scene editing carries no video owner and remains usable.
            using(var ordinary = await factory.OpenAsync())
                await ordinary.ExecuteAsync("UPDATE video_render.video_project_scenes SET status='image_edit' WHERE id=2;");
            Assert.Equal(versionId, await conn.ExecuteScalarAsync<Guid>("SELECT selected_video_version_id FROM video_render.video_project_scenes WHERE id=2;"));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));

            // Real initial-render allocation and DTO reuse still work on a different, unheaded scene.
            var initialJob=Guid.NewGuid(); var initialImage=Guid.NewGuid();
            await conn.ExecuteAsync("INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status) VALUES(3,1,@tenant,'video_queued'); INSERT INTO video_render.scene_image_versions VALUES(@image,1,3,@tenant,'completed'); INSERT INTO render.render_jobs(id,tenant_id,job_type,status,input_json) VALUES(@job,@tenant,'render_scene_video','rendering','{\"projectId\":1,\"sceneId\":3}');",new {tenant=tenantId,image=initialImage,job=initialJob});
            var create = new SceneVideoVersionCreateRequest(1,3,initialImage,actor.UserId,null,initialJob,"initial-logical","image","video",new {},new {});
            using(SceneVideoOperationScope.Enter(initialJob))
            {
                var initialVersion = await versionsService.CreateQueuedSceneVideoVersionAsync(create);
                Assert.Equal(initialJob,initialVersion.RenderJobId);
                Assert.Equal(initialVersion.Id,(await versionsService.CreateQueuedSceneVideoVersionAsync(create)).Id);
                await Assert.ThrowsAsync<RenderJobPendingReconciliationException>(()=>versionsService.CreateQueuedSceneVideoVersionAsync(create with {RenderJobId=Guid.NewGuid()}));
            }
            Assert.Equal(initialJob,await conn.ExecuteScalarAsync<Guid>("SELECT active_render_job_id FROM video_render.scene_video_operation_heads WHERE scene_id=3;"));
            var parent=Guid.NewGuid();
            await conn.ExecuteAsync("INSERT INTO render.render_jobs(id,tenant_id,job_type,status,input_json) VALUES(@job,@tenant,'render_video_batch','rendering','{\"projectId\":1,\"sceneIds\":[3]}');",new{job=parent,tenant=tenantId});
            // Bootstrap generation zero is ordinary batch allocation, not an explicit supersede.
            Assert.True(await conn.ExecuteScalarAsync<bool>("SELECT video_render.scene_video_job_is_current(@tenant,@job);",new{tenant=tenantId,job=parent}));
            using(SceneVideoOperationScope.Enter(parent))
            {
                using var batchConnection=await factory.OpenAsync();
                await batchConnection.ExecuteAsync("UPDATE video_render.video_project_scenes SET status='video_queued' WHERE id=3;");
                var repository=new VideoRenderRepository(factory,new TenantContext(factory,config),NullLogger<VideoRenderRepository>.Instance);
                await repository.UpdateVideoOperationSceneAsync(parent,3,"video_rendering");
            }
            await conn.ExecuteAsync("UPDATE render.render_jobs SET input_json='{\"projectId\":1,\"sceneIds\":[2,3]}' WHERE id=@job;",new{job=parent});
            Assert.False(await conn.ExecuteScalarAsync<bool>("SELECT video_render.scene_video_job_is_current(@tenant,@job);",new{tenant=tenantId,job=parent}));
            // A stale prepared draft can never activate. Fresh exact consent safely abandons ONLY that unsubmitted draft.
            var nextAck=(await Service().ReadAcknowledgementAsync(1,2,actor,Guid.NewGuid())) with {RiskAcknowledged=true};
            var nextDraft=await Service().PrepareAsync(nextAck,actor,Input());
            await conn.ExecuteAsync("UPDATE render.render_jobs SET updated_at=clock_timestamp() WHERE id=@job;",new{job=jobId});
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ActivateAsync(nextAck,actor,nextDraft.TransitionId));
            Assert.Equal(1,await conn.ExecuteScalarAsync<int>("SELECT generation FROM video_render.scene_video_operation_heads WHERE scene_id=2;"));
            var freshAck=(await Service().ReadAcknowledgementAsync(1,2,actor,Guid.NewGuid())) with {RiskAcknowledged=true};
            var freshDraft=await Service().PrepareAsync(freshAck,actor,Input());
            Assert.Equal("cancelled",await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@job;",new{job=nextDraft.JobId}));
            Assert.Equal("ABANDONED",await conn.ExecuteScalarAsync<string>("SELECT transition_state FROM video_render.scene_video_operation_transitions WHERE id=@id;",new{id=nextDraft.TransitionId}));
            await Service().ActivateAsync(freshAck,actor,freshDraft.TransitionId);
            Assert.Equal(2,await conn.ExecuteScalarAsync<int>("SELECT generation FROM video_render.scene_video_operation_heads WHERE scene_id=2;"));
            Assert.Equal(versionId,await conn.ExecuteScalarAsync<Guid>("SELECT selected_video_version_id FROM video_render.video_project_scenes WHERE id=2;"));
            Assert.Equal(100,await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-postcheck.sql")));

            // Rollback after real preparation must refuse to erase history.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(File.ReadAllText(MigrationPath("scene-video-operation-v1-rollback.sql"))));
            await conn.ExecuteAsync("ROLLBACK;");
        }
        finally
        {
            // Only schemas this test CREATED in a previously empty, UUID-named LOOPBACK test database.
            if (ownedSchemas) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP TABLE billing_fixture;");
        }
    }

    [Theory]
    [InlineData("Host=server.example;Database=todox_supersede_test_0123456789abcdef0123456789abcdef;Username=test")]
    [InlineData("Host=127.0.0.1;Database=todo_saas;Username=test")]
    public void DatabaseTestRefusesProductionOrRemoteConnection(string cs)
        => Assert.Throws<InvalidOperationException>(() => ValidateIsolatedConnection(cs));

    private static void ValidateIsolatedConnection(string cs)
    {
        var b = new NpgsqlConnectionStringBuilder(cs);
        if (b.Host is not ("127.0.0.1" or "localhost" or "::1")
            || !Regex.IsMatch(b.Database ?? "", "^todox_supersede_test_[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Only a UUID-named loopback disposable supersede test database is allowed.");
    }

    private static string MigrationPath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", "manual", file))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found"), "database", "manual", file);
    }

    // Minimal contract fixture, not production-schema equivalence or full worker E2E.
    private const string FixtureSql = """
        BEGIN;
        CREATE SCHEMA render;
        CREATE SCHEMA video_render;
        CREATE TABLE video_render.video_projects(id bigint PRIMARY KEY,tenant_id uuid,user_id uuid,customer_id uuid,status text DEFAULT 'draft',final_video_url text);
        CREATE TABLE video_render.final_video_versions(id uuid PRIMARY KEY,project_id bigint,tenant_id uuid,status text,public_url text);
        CREATE TABLE video_render.video_project_scenes(id bigint PRIMARY KEY,project_id bigint,tenant_id uuid,selected_video_version_id uuid,status text,scene_video_url text,scene_video_path text,error_message text,updated_at timestamptz,static_image_url text,static_image_path text,title text,scene_prompt text,image_prompt text,video_prompt text);
        CREATE TABLE video_render.scene_image_versions(id uuid PRIMARY KEY,project_id bigint,scene_id bigint,tenant_id uuid,status text);
        CREATE TABLE render.render_jobs(id uuid PRIMARY KEY,tenant_id uuid,user_id uuid,customer_id uuid,job_type text,status text,priority integer,
          input_json jsonb,prompt_json jsonb,reference_json jsonb,log_code text,point_cost_estimate numeric,point_status text,
          provider_code text,model_code text,max_attempts integer,queued_at timestamptz,created_at timestamptz,
          provider_task_id text,provider_video_id_base text,error_code text,lock_until timestamptz,
          worker_key text,lock_owner text,output_json jsonb,error_message text,cancel_reason text,retry_of_job_id uuid,
          attempt_count integer NOT NULL DEFAULT 0,retry_after timestamptz,point_cost_charged numeric DEFAULT 0,
          started_at timestamptz,completed_at timestamptz,cancelled_at timestamptz,updated_at timestamptz DEFAULT now());
        CREATE TABLE video_render.scene_video_versions(id uuid PRIMARY KEY,project_id bigint,scene_id bigint,
          source_image_version_id uuid REFERENCES video_render.scene_image_versions(id),tenant_id uuid,customer_id uuid,created_by uuid,
          version_number integer,logical_request_id text,render_job_id uuid REFERENCES render.render_jobs(id),image_prompt_snapshot text,video_prompt_snapshot text,
          scene_snapshot_json jsonb,render_config_json jsonb,storage_key text,status text,is_selected boolean NOT NULL DEFAULT false,
          provider_code text,requested_model text,actual_model text,provider_capability_id bigint,created_at timestamptz,updated_at timestamptz,
          provider_task_id text,provider_video_id_base text,public_url text,source_file_path text,result_media_id uuid,
          duration_seconds numeric,aspect_ratio text,billing_logical_request_id text,estimated_usd numeric,actual_usd numeric,
          charged_points numeric DEFAULT 0,refunded_points numeric DEFAULT 0,cost_source text,voice_audio_version_id uuid,
          poster_url text,error_code text,error_message text,submitted_at timestamptz,selected_at timestamptz,selected_by uuid,
          completed_at timestamptz,mime_type text,UNIQUE(tenant_id,scene_id,version_number),UNIQUE(tenant_id,logical_request_id));
        CREATE TABLE render.scene_video_submit_intents(tenant_id uuid,scene_id bigint,logical_request_id text,render_job_id uuid,
          owner_token uuid,submit_state text DEFAULT 'SUBMIT_INTENT_PERSISTED',provider_task_id text,provider_video_id_base text,
          reconciled_at timestamptz,updated_at timestamptz,UNIQUE(tenant_id,scene_id,logical_request_id));
        CREATE TABLE render.render_job_events(id uuid DEFAULT gen_random_uuid(),tenant_id uuid,job_id uuid,event_type text,data_json jsonb,level text,message text,created_at timestamptz DEFAULT now());
        CREATE TABLE billing_fixture(points integer);
        COMMIT;
        """;
}
