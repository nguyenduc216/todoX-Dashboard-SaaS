using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    // Reported wedge: a queued job the head does not own can never be claimed, yet it blocked every new operation.
    [SupersedePostgresFact]
    public Task NeverSubmittedNonCurrentQueuedJobDoesNotBlockNewOperation()
        => RunOrphanScenarioAsync("orphan", expectBlocked: false);

    // Anything that may have reached the provider or a worker keeps blocking: only the exact wedge is exempt.
    [SupersedePostgresTheory]
    [InlineData("provider_task")]
    [InlineData("provider_video_id")]
    [InlineData("intent")]
    [InlineData("version")]
    [InlineData("attempted")]
    [InlineData("rendering")]
    public Task PossiblySubmittedNonCurrentJobStillBlocksNewOperation(string variant)
        => RunOrphanScenarioAsync(variant, expectBlocked: true);

    private static async Task RunOrphanScenarioAsync(string variant, bool expectBlocked)
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
            var tenant = Guid.NewGuid();
            var actor = SceneVideoOperationDraftTests.Actor(); actor.IsRoot = true;
            var headJob = Guid.NewGuid(); var headVersion = Guid.NewGuid(); var orphan = Guid.NewGuid(); var source = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO video_render.video_projects(id,tenant_id,user_id,status) VALUES(1,@tenant,@actor,'rendering');
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status) VALUES(898,1,@tenant,'video_queued');
                INSERT INTO video_render.scene_image_versions VALUES(@source,1,898,@tenant,'completed');
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,created_at)
                VALUES(@headJob,@tenant,@actor,'render_scene_video','failed','{"projectId":1,"sceneId":898}','failed-task',now()-interval '1 day');
                INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status,provider_task_id)
                VALUES(@headVersion,1,898,@tenant,1,'head-operation',@headJob,'failed','failed-task');
                INSERT INTO video_render.scene_video_operation_heads(tenant_id,project_id,scene_id,generation,active_render_job_id,active_video_version_id)
                VALUES(@tenant,1,898,0,@headJob,@headVersion);
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,provider_video_id_base,attempt_count,created_at)
                VALUES(@orphan,@tenant,@actor,'render_scene_video',@status,'{"projectId":1,"sceneId":898}',@task,@videoId,@attempts,now());
                """, new
            {
                tenant,
                actor = actor.UserId,
                source,
                headJob,
                headVersion,
                orphan,
                status = variant == "rendering" ? "rendering" : "queued",
                task = variant == "provider_task" ? "orphan-task" : null,
                videoId = variant == "provider_video_id" ? "orphan-video" : null,
                attempts = variant == "attempted" ? 1 : 0
            });
            if (variant == "intent")
                await conn.ExecuteAsync("""
                    INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token,submit_state)
                    VALUES(@tenant,898,'orphan-operation',@orphan,@token,'SUBMIT_IN_FLIGHT');
                    """, new { tenant, orphan, token = Guid.NewGuid() });
            if (variant == "version")
                await conn.ExecuteAsync("""
                    INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status)
                    VALUES(@id,1,898,@tenant,2,'orphan-operation',@orphan,'failed');
                    """, new { id = Guid.NewGuid(), tenant, orphan });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString() }).Build();
            var factory = new TodoXConnectionFactory(config);
            var operations = new SceneVideoOperationDraftService(factory, new TenantContext(factory, config));
            var ack = (await operations.ReadAcknowledgementAsync(1, 898, actor, Guid.NewGuid())) with { RiskAcknowledged = true };
            Assert.Equal(headJob, ack.OldJobId);
            SceneVideoRenderWorkItemInput Input() => new()
            {
                ProjectId = 1,
                SceneId = 898,
                SelectedSourceImageVersionId = source,
                VideoPrompt = "video",
                ImagePrompt = "image",
                ProviderCode = "fake",
                ModelName = "fake-model"
            };
            var orphanBefore = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(j)::text FROM render.render_jobs j WHERE id=@orphan;", new { orphan });

            if (expectBlocked)
            {
                var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.PrepareAsync(ack, actor, Input()));
                Assert.Equal("Unacknowledged competing operation exists.", blocked.Message);
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_video_operation_transitions;"));
                return;
            }

            var draft = await operations.PrepareAsync(ack, actor, Input());
            var activated = await operations.ActivateAsync(ack, actor, draft.TransitionId);
            Assert.Equal(draft.JobId, activated);
            Assert.Equal((1L, draft.JobId), await conn.QuerySingleAsync<(long, Guid)>(
                "SELECT generation,active_render_job_id FROM video_render.scene_video_operation_heads WHERE scene_id=898;"));
            Assert.Equal("queued", await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@job;", new { job = draft.JobId }));
            // The orphan is left untouched as history, and still can never run.
            Assert.Equal(orphanBefore, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(j)::text FROM render.render_jobs j WHERE id=@orphan;", new { orphan }));
            Assert.False(await conn.ExecuteScalarAsync<bool>("SELECT video_render.scene_video_job_is_current(@tenant,@orphan);", new { tenant, orphan }));
        }
        finally
        {
            if (owned) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP TABLE billing_fixture;");
        }
    }
}
