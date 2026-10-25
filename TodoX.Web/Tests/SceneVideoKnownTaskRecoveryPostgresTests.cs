using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KnownTaskRecoveryPostgresFactAttribute : FactAttribute
{
    public KnownTaskRecoveryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOX_RECOVERY_SIM_URL")))
            Skip = "Requires isolated PostgreSQL opt-in and the local Postman known-task simulation URL.";
    }
}

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    [KnownTaskRecoveryPostgresFact]
    public async Task Project196EightConfirmedTasksResumeAfterLocalTimeoutWithoutNewSubmits()
    {
        var cs = Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")!;
        ValidateIsolatedConnection(cs);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN('pg_catalog','information_schema');"));
        var owned = false;
        try
        {
            await conn.ExecuteAsync(FixtureSql);
            owned = true;
            await conn.ExecuteAsync("""
                CREATE TABLE video_render.video_project_events(id bigserial,tenant_id uuid,project_id bigint,
                  event_type text,level text,message text,data_json jsonb,created_at timestamptz DEFAULT now());
                INSERT INTO billing_fixture VALUES(100);
                ALTER TABLE video_render.video_projects ADD core_job_id uuid, ADD title text, ADD original_prompt text,
                  ADD active_prompt_generation_id uuid, ADD total_seconds integer, ADD scene_seconds integer, ADD scene_count integer,
                  ADD think_scenes boolean, ADD character_id bigint, ADD uploaded_character_url text, ADD source_image_url text,
                  ADD storage_root text, ADD public_base text, ADD job_folder text, ADD final_video_path text, ADD error_message text,
                  ADD created_at timestamptz, ADD updated_at timestamptz;
                ALTER TABLE video_render.video_project_scenes ADD scene_index integer, ADD duration_seconds integer,
                  ADD voice_enabled boolean, ADD speaker_key text, ADD voice_text text, ADD voice_instruction text,
                  ADD selected_audio_version_id uuid, ADD created_at timestamptz;
                CREATE SCHEMA billing;
                CREATE TABLE billing.token_usage_logs(id uuid,tenant_id uuid,user_id uuid,customer_id uuid,provider_code text,
                  model_code text,operation text,quantity integer,unit text,token_cost numeric,charged boolean,
                  reference_type text,reference_id uuid,endpoint_code text,status text,created_at timestamptz);
                CREATE TABLE billing.ai_image_billing_records(tenant_id uuid,logical_request_id text,render_job_id text,
                  feature_code text,capability_code text,provider_task_id text,status text);
                """);
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-forward.sql")));
            var tenant = Guid.NewGuid();
            var actor = Guid.NewGuid();
            await conn.ExecuteAsync("INSERT INTO video_render.video_projects(id,tenant_id,user_id,status) VALUES(196,@tenant,@actor,'rendering');", new { tenant, actor });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString(),
                ["VideoRender:KnownTaskRecoveryMaxPolls"] = "2", ["VideoRender:KnownTaskRecoveryWindowMinutes"] = "60"
            }).Build();
            var factory = new TodoXConnectionFactory(config);
            var jobs = new RenderJobService(factory, new TenantContext(factory, config), config, NullLogger<RenderJobService>.Instance);
            var repo = new VideoRenderRepository(factory, new TenantContext(factory, config), NullLogger<VideoRenderRepository>.Instance);
            var ids = new List<Guid>();
            foreach (var scene in Enumerable.Range(865, 8))
            {
                var job = Guid.NewGuid(); var version = Guid.NewGuid(); ids.Add(job);
                await conn.ExecuteAsync("""
                    INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status) VALUES(@scene,196,@tenant,'video_rendering');
                    INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,provider_video_id_base,
                        attempt_count,max_attempts,error_code,error_message,retry_after,created_at,started_at)
                    VALUES(@job,@tenant,@actor,'render_scene_video','pending_reconciliation',
                        jsonb_build_object('projectId',196,'sceneId',@scene,'logicalRequestId',@logical,'operationGeneration',1,
                            'providerPollStartedAt',now()-interval '2 hours','providerPollCount',30),
                        @task,@base,1,1,'RenderJobPendingReconciliationException',
                        'Known provider task exceeded local polling timeout; operator reconciliation required.',NULL,now()-interval '2 hours',now()-interval '2 hours');
                    INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status,
                        provider_task_id,provider_video_id_base,submitted_at,storage_key)
                    VALUES(@version,196,@scene,@tenant,1,@logical,@job,'pending_reconciliation',@task,@base,now()-interval '2 hours',@storage);
                    INSERT INTO video_render.scene_video_operation_heads(tenant_id,project_id,scene_id,generation,active_render_job_id,active_video_version_id)
                    VALUES(@tenant,196,@scene,1,@job,@version);
                    INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token,submit_state,provider_task_id,provider_video_id_base)
                    VALUES(@tenant,@scene,@logical,@job,@token,'CONFIRMED_PROVIDER_TASK',@task,@base);
                    UPDATE video_render.video_project_scenes SET scene_index=@scene-864,duration_seconds=8 WHERE id=@scene;
                    UPDATE video_render.scene_video_versions SET provider_code='79ai',provider_capability_id=1,
                      actual_model='veo_omni',duration_seconds=8,render_config_json='{"provider":"79ai","capability":"rvideo_scene_video_generation","model":"veo_omni","mode":"flash","providerDurationSeconds":8,"providerResolution":"720p"}'::jsonb
                      WHERE id=@version;
                    UPDATE render.render_jobs SET input_json=input_json||'{"providerId":1,"providerCapabilityId":1,"providerCode":"79ai",
                      "capabilityCode":"rvideo_scene_video_generation","modelName":"veo_omni","requestedMode":"flash","durationSeconds":8,
                      "aspectRatio":"9:16","resolution":"720p","useSharedReferenceImage":true,"sourceImageUrl":"https://example.com/source.png",
                      "videoPrompt":"Animate the scene","imagePrompt":"image","customerPointRate":0}'::jsonb WHERE id=@job;
                    """, new { tenant, actor, scene, job, version, logical = $"project196-scene{scene}", task = $"existing-task-{scene}",
                        @base = $"existing-base-{scene}", token = Guid.NewGuid(), storage = SceneMediaStorageKeys.SceneVideoOutput(tenant, 196, scene, version) });
            }
            var intentSnapshot = await conn.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(i) ORDER BY scene_id)::text FROM render.scene_video_submit_intents i;");
            Assert.Equal(8, (await repo.ListPersistentSceneVideoReconciliationJobsAsync()).Count);
            var first = ids[0];
            await conn.ExecuteAsync("UPDATE render.render_jobs SET input_json=input_json||'{\"providerRecoveryPollCount\":2}'::jsonb WHERE id=@first;", new { first });
            Assert.False(await jobs.SchedulePersistentProviderPollAsync(first, TimeSpan.FromSeconds(1), "RECOVERY", "Budget exhausted"));
            await conn.ExecuteAsync("UPDATE render.render_jobs SET input_json=(input_json-'providerRecoveryPollCount')||jsonb_build_object('providerRecoveryStartedAt',now()-interval '2 hours') WHERE id=@first;", new { first });
            Assert.False(await jobs.SchedulePersistentProviderPollAsync(first, TimeSpan.FromSeconds(1), "RECOVERY", "Window expired"));
            await conn.ExecuteAsync("UPDATE render.render_jobs SET input_json=input_json-'providerRecoveryStartedAt',lock_until=now()+interval '1 minute' WHERE id=@first;", new { first });
            Assert.False(await jobs.SchedulePersistentProviderPollAsync(first, TimeSpan.FromSeconds(1), "RECOVERY", "Live lease"));
            await conn.ExecuteAsync("UPDATE render.render_jobs SET lock_until=NULL WHERE id=@first; UPDATE render.scene_video_submit_intents SET submit_state='AMBIGUOUS_SUBMIT' WHERE render_job_id=@first;", new { first });
            Assert.False(await jobs.SchedulePersistentProviderPollAsync(first, TimeSpan.FromSeconds(1), "RECOVERY", "Unconfirmed identity"));
            await conn.ExecuteAsync("UPDATE render.scene_video_submit_intents SET submit_state='CONFIRMED_PROVIDER_TASK' WHERE render_job_id=@first;", new { first });
            foreach (var id in ids)
            {
                var scheduled = await Task.WhenAll(
                    jobs.SchedulePersistentProviderPollAsync(id, TimeSpan.FromSeconds(1), "RECOVERY", "Poll existing task"),
                    jobs.SchedulePersistentProviderPollAsync(id, TimeSpan.FromSeconds(1), "RECOVERY", "Poll existing task"));
                Assert.Single(scheduled, result => result);
                Assert.Equal("queued", await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@id;", new { id }));
                Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT (input_json->>'providerRecoveryPollCount')::int FROM render.render_jobs WHERE id=@id;", new { id }));
            }
            Assert.Equal(intentSnapshot, await conn.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(i) ORDER BY scene_id)::text FROM render.scene_video_submit_intents i;"));
            await ExerciseKnownTaskHttpRecoveryAsync(conn, factory, config, jobs, repo, ids);
            Assert.Equal(intentSnapshot, await conn.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(i) ORDER BY scene_id)::text FROM render.scene_video_submit_intents i;"));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));
        }
        finally
        {
            if (owned) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP SCHEMA billing CASCADE; DROP TABLE billing_fixture;");
        }
    }
}
