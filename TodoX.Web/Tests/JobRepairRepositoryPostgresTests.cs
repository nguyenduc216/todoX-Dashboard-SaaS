using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.JobRepair;
using Xunit;

namespace TodoX.Web.Tests;

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    [SupersedePostgresFact]
    public async Task RepairSnapshotsMirrorReportedJobAndRecheckResetsOnlyItsRecoveryBudget()
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
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-forward.sql")));
            await conn.ExecuteAsync("""
                ALTER TABLE video_render.video_projects ADD COLUMN core_job_id uuid;
                ALTER TABLE video_render.video_project_scenes ADD COLUMN scene_index integer;
                ALTER TABLE render.render_jobs ADD COLUMN service_id uuid;
                """);
            var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var core = Guid.NewGuid();
            var failedJob = Guid.NewGuid(); var orphan = Guid.NewGuid(); var timedOutJob = Guid.NewGuid(); var inFlightJob = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,created_at)
                VALUES(@core,@tenant,@actor,'core_service','rendering','{}',now());
                INSERT INTO video_render.video_projects(id,tenant_id,user_id,status,core_job_id) VALUES(1,@tenant,@actor,'rendering',@core);
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status,scene_index) VALUES
                  (898,1,@tenant,'video_queued',1),(902,1,@tenant,'video_queued',5),(901,1,@tenant,'video_queued',4),(904,1,@tenant,'draft',6);
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,provider_video_id_base,created_at,updated_at,error_code) VALUES
                  (@failedJob,@tenant,@actor,'render_scene_video','failed','{"projectId":1,"sceneId":898}','t1','b1',now(),now(),'x'),
                  (@timedOutJob,@tenant,@actor,'render_scene_video','pending_reconciliation',
                   '{"projectId":"1","sceneId":"902","providerRecoveryStartedAt":"2026-10-28T00:00:00Z","providerRecoveryPollCount":20,"providerPollStartedAt":"2026-10-28T00:00:00Z"}',
                   't2','b2',now(),now()-interval '2 days','SCENE_VIDEO_PROVIDER_POLL_TIMEOUT'),
                  (@inFlightJob,@tenant,@actor,'render_scene_video','rendering','{"projectId":1,"sceneId":901}',NULL,NULL,now(),now()-interval '2 days',NULL);
                INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status) VALUES
                  (@v1,1,898,@tenant,1,'l1',@failedJob,'failed'),(@v2,1,902,@tenant,1,'l2',@timedOutJob,'pending_reconciliation'),(@v3,1,901,@tenant,1,'l3',@inFlightJob,'queued');
                INSERT INTO video_render.scene_video_operation_heads(tenant_id,project_id,scene_id,generation,active_render_job_id,active_video_version_id) VALUES
                  (@tenant,1,898,0,@failedJob,@v1),(@tenant,1,902,0,@timedOutJob,@v2),(@tenant,1,901,0,@inFlightJob,@v3);
                INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token,submit_state) VALUES
                  (@tenant,902,'l2',@timedOutJob,@token1,'CONFIRMED_PROVIDER_TASK'),(@tenant,901,'l3',@inFlightJob,@token2,'SUBMIT_IN_FLIGHT');
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,created_at)
                VALUES(@orphan,@tenant,@actor,'render_scene_video','queued','{"projectId":1,"sceneId":898}',now());
                """, new { tenant, actor, core, failedJob, orphan, timedOutJob, inFlightJob, v1 = Guid.NewGuid(), v2 = Guid.NewGuid(), v3 = Guid.NewGuid(), token1 = Guid.NewGuid(), token2 = Guid.NewGuid() });
            // Job cf498c5c: no head row, a manual-review job with its version, and a queued version nothing can run.
            var reviewJob = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status,scene_index) VALUES(778,1,@tenant,'video_queued',7);
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,error_code,attempt_count,created_at,updated_at) VALUES
                  (@reviewJob,@tenant,@actor,'render_scene_video','pending_reconciliation','{"projectId":1,"sceneId":778}','SCENE_VIDEO_MANUAL_REVIEW_REQUIRED',1,now(),now()-interval '10 days'),
                  (@oldJob,@tenant,@actor,'render_scene_video','failed','{"projectId":1,"sceneId":778}',NULL,1,now()-interval '11 days',now()-interval '11 days');
                INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status) VALUES
                  (@stray,1,778,@tenant,1,'l-stray',@oldJob,'queued'),(@review,1,778,@tenant,2,'l-review',@reviewJob,'pending_reconciliation');
                """, new { tenant, actor, reviewJob, oldJob = Guid.NewGuid(), stray = Guid.NewGuid(), review = Guid.NewGuid() });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString() }).Build();
            var factory = new TodoXConnectionFactory(config);
            var repository = new JobRepairRepository(factory, new TenantContext(factory, config));

            var scenes = (await repository.GetSceneSnapshotsAsync(core)).ToDictionary(s => s.SceneId);
            Assert.Equal(5, scenes.Count);
            Assert.Equal(reviewJob, scenes[778].HeadJobId);
            Assert.Equal((0, 0, true), (scenes[778].CompetingJobCount, scenes[778].CompetingVersionCount, scenes[778].AckFound));
            Assert.Equal(JobRepairPlaybook.ManualReviewUnconfirmedSubmit, JobRepairPlaybook.Classify(scenes[778]).CaseCode);
            Assert.Equal((1, 0), (scenes[898].OrphanJobCount, scenes[898].CompetingJobCount));
            Assert.Equal(JobRepairPlaybook.FailedOperation, JobRepairPlaybook.Classify(scenes[898]).CaseCode);
            Assert.Equal(JobRepairPlaybook.KnownTaskTimedOut, JobRepairPlaybook.Classify(scenes[902]).CaseCode);
            Assert.Equal(JobRepairPlaybook.AmbiguousSubmit, JobRepairPlaybook.Classify(scenes[901]).CaseCode);
            Assert.Equal(JobRepairPlaybook.NotStarted, JobRepairPlaybook.Classify(scenes[904]).CaseCode);

            Assert.True(await repository.ResetKnownTaskRecoveryBudgetAsync(1, 902, timedOutJob));
            var input = await conn.ExecuteScalarAsync<string>("SELECT input_json::text FROM render.render_jobs WHERE id=@timedOutJob;", new { timedOutJob });
            Assert.DoesNotContain("providerRecovery", input);
            Assert.Contains("providerPollStartedAt", input);
            Assert.Equal(("pending_reconciliation", "t2", "b2"), await conn.QuerySingleAsync<(string, string, string)>(
                "SELECT status,provider_task_id,provider_video_id_base FROM render.render_jobs WHERE id=@timedOutJob;", new { timedOutJob }));

            // Wrong status or a job the head does not own is refused without a write.
            Assert.False(await repository.ResetKnownTaskRecoveryBudgetAsync(1, 901, inFlightJob));
            Assert.False(await repository.ResetKnownTaskRecoveryBudgetAsync(1, 902, failedJob));
        }
        finally
        {
            if (owned) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP TABLE billing_fixture;");
        }
    }
}
