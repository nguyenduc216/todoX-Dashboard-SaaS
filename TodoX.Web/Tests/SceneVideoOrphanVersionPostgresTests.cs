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
    // Reported job cf498c5c: an old manual-review job without a head, next to a queued version nothing can ever run.
    [SupersedePostgresTheory]
    [InlineData("failed_owner")]
    [InlineData("no_owner")]
    public Task QueuedVersionNothingCanRunDoesNotBlockNewOperation(string variant)
        => RunOrphanVersionScenarioAsync(variant, expectBlocked: false);

    [SupersedePostgresTheory]
    [InlineData("owner_has_provider_task")]
    [InlineData("version_has_provider_task")]
    [InlineData("has_intent")]
    [InlineData("submitted_version")]
    public Task PossiblySubmittedVersionStillBlocksNewOperation(string variant)
        => RunOrphanVersionScenarioAsync(variant, expectBlocked: true);

    private static async Task RunOrphanVersionScenarioAsync(string variant, bool expectBlocked)
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
            var reviewJob = Guid.NewGuid(); var ownerJob = Guid.NewGuid(); var source = Guid.NewGuid();
            var reviewVersion = Guid.NewGuid(); var strayVersion = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO video_render.video_projects(id,tenant_id,user_id,status) VALUES(1,@tenant,@actor,'rendering');
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,status) VALUES(778,1,@tenant,'video_queued');
                INSERT INTO video_render.scene_image_versions VALUES(@source,1,778,@tenant,'completed');
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,error_code,attempt_count,created_at)
                VALUES(@reviewJob,@tenant,@actor,'render_scene_video','pending_reconciliation','{"projectId":1,"sceneId":778}','SCENE_VIDEO_MANUAL_REVIEW_REQUIRED',1,now()-interval '10 days');
                INSERT INTO render.render_jobs(id,tenant_id,user_id,job_type,status,input_json,provider_task_id,created_at)
                VALUES(@ownerJob,@tenant,@actor,'render_scene_video','failed','{"projectId":1,"sceneId":778}',@ownerTask,now()-interval '11 days');
                INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,render_job_id,status,provider_task_id) VALUES
                  (@strayVersion,1,778,@tenant,1,'stray-request',@strayOwner,@strayStatus,@versionTask),
                  (@reviewVersion,1,778,@tenant,2,'review-request',@reviewJob,'pending_reconciliation',NULL);
                """, new
            {
                tenant,
                actor = actor.UserId,
                source,
                reviewJob,
                ownerJob,
                reviewVersion,
                strayVersion,
                ownerTask = variant == "owner_has_provider_task" ? "owner-task" : null,
                versionTask = variant == "version_has_provider_task" ? "version-task" : null,
                strayOwner = variant == "no_owner" ? (Guid?)null : ownerJob,
                strayStatus = variant == "submitted_version" ? "submitted" : "queued"
            });
            if (variant == "has_intent")
                await conn.ExecuteAsync("""
                    INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token,submit_state)
                    VALUES(@tenant,778,'stray-request',@ownerJob,@token,'SUBMIT_IN_FLIGHT');
                    """, new { tenant, ownerJob, token = Guid.NewGuid() });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString() }).Build();
            var factory = new TodoXConnectionFactory(config);
            var operations = new SceneVideoOperationDraftService(factory, new TenantContext(factory, config));
            var ack = (await operations.ReadAcknowledgementAsync(1, 778, actor, Guid.NewGuid())) with { RiskAcknowledged = true };
            Assert.Equal(reviewJob, ack.OldJobId);
            Assert.Equal(reviewVersion, ack.OldVersionId);
            SceneVideoRenderWorkItemInput Input() => new()
            {
                ProjectId = 1,
                SceneId = 778,
                SelectedSourceImageVersionId = source,
                VideoPrompt = "video",
                ImagePrompt = "image",
                ProviderCode = "fake",
                ModelName = "fake-model"
            };
            var strayBefore = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_video_versions v WHERE id=@strayVersion;", new { strayVersion });

            if (expectBlocked)
            {
                var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.PrepareAsync(ack, actor, Input()));
                Assert.Equal("Unacknowledged competing operation exists.", blocked.Message);
                return;
            }

            var draft = await operations.PrepareAsync(ack, actor, Input());
            Assert.Equal(draft.JobId, await operations.ActivateAsync(ack, actor, draft.TransitionId));
            Assert.Equal((1L, draft.JobId), await conn.QuerySingleAsync<(long, Guid)>(
                "SELECT generation,active_render_job_id FROM video_render.scene_video_operation_heads WHERE scene_id=778;"));
            Assert.Equal("queued", await conn.ExecuteScalarAsync<string>("SELECT status FROM render.render_jobs WHERE id=@job;", new { job = draft.JobId }));
            // History is retained untouched; only the head moved.
            Assert.Equal(strayBefore, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_video_versions v WHERE id=@strayVersion;", new { strayVersion }));
        }
        finally
        {
            if (owned) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP TABLE billing_fixture;");
        }
    }
}
