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

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    // Deliberately not executed for this task: user permits writing tests but forbids running PostgreSQL.
    // Reuses the UUID-named, EMPTY loopback-only fixture; never reads application connection settings.
    [SupersedePostgresTheory]
    [Trait("Category", "Postgres")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task QueueThirteenSupersededJobsDoNotBlockThreeLanesOrLegacy(bool forceClaimFence, bool failAudit)
    {
        var cs = Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")!;
        ValidateIsolatedConnection(cs);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN('pg_catalog','information_schema');"));
        var ownsFixture = false;
        try
        {
            await conn.ExecuteAsync(FixtureSql);
            ownsFixture = true;
            await conn.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("scene-video-operation-v1-forward.sql")));
            var tenant = Guid.NewGuid();
            await conn.ExecuteAsync("INSERT INTO video_render.video_projects(id,tenant_id) VALUES(205,@tenant); INSERT INTO billing_fixture VALUES(100);", new { tenant });
            var stale = new List<Guid>();
            for (var index = 0; index < 13; index++)
            {
                var scene = 838 + index;
                var old = Guid.NewGuid(); var current = Guid.NewGuid(); var selected = Guid.NewGuid();
                stale.Add(old);
                await conn.ExecuteAsync(
                    """
                    INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,selected_video_version_id) VALUES(@scene,205,@tenant,@selected);
                    INSERT INTO render.render_jobs(id,tenant_id,job_type,status,priority,input_json,attempt_count,max_attempts,queued_at,created_at)
                    VALUES (@old,@tenant,'render_scene_video','queued',0,jsonb_build_object('projectId',205,'sceneId',@scene,'logicalRequestId',@old::text),0,1,now()-interval '1 day',now()),
                      (@current,@tenant,'render_scene_video','completed',100,jsonb_build_object('projectId',205,'sceneId',@scene,'logicalRequestId',@current::text),1,1,now(),now());
                    INSERT INTO video_render.scene_video_versions(id,project_id,scene_id,tenant_id,render_job_id,logical_request_id,version_number,status,is_selected)
                      VALUES(@selected,205,@scene,@tenant,@current,@current::text,1,'completed',true);
                    INSERT INTO video_render.scene_video_operation_heads(tenant_id,project_id,scene_id,generation,active_render_job_id,active_video_version_id)
                      VALUES(@tenant,205,@scene,0,@current,@selected);
                    """, new { tenant, scene, old, current, selected });
            }
            var ready = new List<Guid>();
            for (var scene = 911; scene <= 916; scene++)
            {
                var id = Guid.NewGuid(); ready.Add(id);
                await conn.ExecuteAsync(
                    """
                    INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id) VALUES(@scene,205,@tenant);
                    INSERT INTO render.render_jobs(id,tenant_id,job_type,status,priority,input_json,attempt_count,max_attempts,queued_at,created_at)
                    VALUES(@id,@tenant,'render_scene_video','queued',100,jsonb_build_object('projectId',205,'sceneId',@scene,'logicalRequestId',@id::text),0,1,now(),now());
                    """, new { tenant, scene, id });
            }
            var unresolved = Guid.NewGuid(); stale.Add(unresolved);
            await conn.ExecuteAsync(
                """
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id) VALUES(930,205,@tenant);
                INSERT INTO render.render_jobs(id,tenant_id,job_type,status,priority,input_json,attempt_count,max_attempts,queued_at,created_at)
                VALUES(@unresolved,@tenant,'render_scene_video','queued',25,jsonb_build_object('projectId',205,'sceneId',930,'logicalRequestId',@unresolved::text),0,1,now(),now());
                INSERT INTO render.scene_video_submit_intents(tenant_id,scene_id,logical_request_id,render_job_id,owner_token,submit_state)
                VALUES(@tenant,930,@unresolved::text,@unresolved,@token,'AMBIGUOUS_SUBMIT');
                """, new { tenant, unresolved, token = Guid.NewGuid() });
            var snapshots = (await conn.QueryAsync<string>("SELECT to_jsonb(j)::text FROM render.render_jobs j WHERE id=ANY(@ids) ORDER BY id;", new { ids = stale.ToArray() })).ToArray();
            var versions = await conn.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(v) ORDER BY id)::text FROM video_render.scene_video_versions v;");
            if (forceClaimFence)
            {
                // Simulate ownership becoming invalid between the SELECT predicate and trigger recheck.
                // Existing generation trigger is retained; only this test's SELECT predicate is made stale.
                await conn.ExecuteAsync("CREATE OR REPLACE FUNCTION video_render.scene_video_job_is_current(t uuid,j uuid) RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT true $$;");
            }
            if (failAudit)
                await conn.ExecuteAsync("ALTER TABLE render.render_job_events ADD CONSTRAINT reject_queue_audit CHECK(event_type <> 'SCENE_VIDEO_QUEUE_QUARANTINED');");

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString() }).Build();
            RenderJobService Jobs()
            {
                var factory = new TodoXConnectionFactory(config);
                return new RenderJobService(factory, new TenantContext(factory, config), config, NullLogger<RenderJobService>.Instance);
            }
            if (failAudit)
            {
                await Assert.ThrowsAsync<PostgresException>(() => Jobs().ClaimNextByJobTypeAsync("audit-failure", TimeSpan.FromMinutes(1), new[] { RenderJobTypes.RenderSceneVideo }));
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs WHERE status='preparing';"));
                Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_job_events;"));
                return;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var laneClaims = await Task.WhenAll(Enumerable.Range(1, 3).Select(lane => Jobs().ClaimNextByJobTypeAsync(
                $"queue-lane-{lane}", TimeSpan.FromMinutes(1), new[] { RenderJobTypes.RenderSceneVideo }, timeout.Token)));
            Assert.All(laneClaims, job => Assert.NotNull(job));
            Assert.Equal(3, laneClaims.Select(job => job!.Id).Distinct().Count());
            Assert.All(laneClaims, job => { Assert.Contains(job!.Id, ready); Assert.Equal(1, job.AttemptCount); });
            foreach (var laneClaim in laneClaims)
                await Jobs().MarkSceneVideoClaimRenderingAsync(laneClaim!, laneClaim!.WorkerKey!, timeout.Token);
            for (var index = 0; index < 3; index++)
                Assert.Contains((await Jobs().ClaimNextByJobTypeAsync("queue-rest", TimeSpan.FromMinutes(1), new[] { RenderJobTypes.RenderSceneVideo }, timeout.Token))!.Id, ready);
            Assert.Null(await Jobs().ClaimNextByJobTypeAsync("queue-empty", TimeSpan.FromMinutes(1), new[] { RenderJobTypes.RenderSceneVideo }, timeout.Token));
            Assert.Equal(14, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_job_events WHERE event_type='SCENE_VIDEO_QUEUE_QUARANTINED';"));
            Assert.Equal(snapshots, (await conn.QueryAsync<string>("SELECT to_jsonb(j)::text FROM render.render_jobs j WHERE id=ANY(@ids) ORDER BY id;", new { ids = stale.ToArray() })).ToArray());
            Assert.Equal(versions, await conn.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(v) ORDER BY id)::text FROM video_render.scene_video_versions v;"));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.scene_video_submit_intents;"));

            // The unchanged real coordinator admits exactly one durable intent and one BeginSubmit.
            var job = laneClaims[0]!;
            var sceneId = await conn.ExecuteScalarAsync<long>("SELECT (input_json->>'sceneId')::bigint FROM render.render_jobs WHERE id=@id;", new { id = job.Id });
            var factory = new TodoXConnectionFactory(config);
            SceneVideoSubmitCoordinator Coordinator() => new(factory, new TenantContext(factory, config));
            var submits = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Coordinator().ClaimSubmitAsync(205, sceneId, job.Id.ToString(), job.Id)));
            var accepted = Assert.Single(submits, x => x.Outcome == SceneVideoSubmitClaimOutcome.Claimed);
            Assert.True(await Coordinator().BeginSubmitAsync(accepted.OwnerToken!.Value));
            Assert.False(await Coordinator().BeginSubmitAsync(accepted.OwnerToken.Value));
            await Coordinator().MarkAmbiguousAsync(accepted.OwnerToken.Value);
            Assert.NotEqual(SceneVideoSubmitClaimOutcome.Claimed, (await Coordinator().ClaimSubmitAsync(205, sceneId, job.Id.ToString(), job.Id)).Outcome);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.scene_video_submit_intents WHERE render_job_id=@id;", new { id = job.Id }));
            Assert.Equal(100, await conn.ExecuteScalarAsync<int>("SELECT points FROM billing_fixture;"));
        }
        finally
        {
            if (ownsFixture) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE; DROP TABLE billing_fixture;");
        }
    }
}
