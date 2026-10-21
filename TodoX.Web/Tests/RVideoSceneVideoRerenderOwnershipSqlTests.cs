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

/// <summary>
/// Executable regression tests that run the real pre-dispatch ownership claim against PostgreSQL.
/// The connection string comes from TODOX_TEST_PG_CONNECTION; when it is absent the tests are skipped
/// so an offline build stays green. Point it ONLY at a disposable database - the fixtures truncate.
/// </summary>
[Collection("pg-scene-video-ownership")]
public sealed class RVideoSceneVideoRerenderOwnershipSqlTests
{
    private const string EnvName = "TODOX_TEST_PG_CONNECTION";
    private const long ProjectId = 183;
    private const long SceneId = 814;
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string? ConnectionString => Environment.GetEnvironmentVariable(EnvName);

    private sealed record Fixture(RenderJobService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var cs = ConnectionString!;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoXSaaS"] = cs,
                ["TodoX:TenantId"] = Tenant.ToString(),
                ["TodoX:TenantCode"] = "TODOX_INTERNAL"
            })
            .Build();
        var factory = new TodoXConnectionFactory(configuration);
        var service = new RenderJobService(factory, new TenantContext(factory, configuration),
            configuration, NullLogger<RenderJobService>.Instance);
        await using var conn = await OpenAsync();
        await conn.ExecuteAsync("""
            TRUNCATE render.render_job_events, render.scene_video_submit_intents, render.render_jobs,
                     video_render.scene_video_versions, video_render.video_project_scenes,
                     video_render.video_projects;
            INSERT INTO video_render.video_projects (id, tenant_id) VALUES (183, @Tenant);
            INSERT INTO video_render.video_project_scenes (id, project_id, tenant_id) VALUES (814, 183, @Tenant);
            """, new { Tenant });
        return new Fixture(service);
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>Seeds V2 (completed+selected), V3 and V4 as history plus the quarantined production job.</summary>
    private static async Task SeedScene814HistoryAsync()
    {
        await using var conn = await OpenAsync();
        for (var i = 1; i <= 3; i++)
        {
            var jobId = Guid.Parse($"aaaaaaaa-0000-0000-0000-{i:D12}");
            await conn.ExecuteAsync("""
                INSERT INTO video_render.scene_video_versions
                  (id, project_id, scene_id, tenant_id, version_number, logical_request_id,
                   render_job_id, status, is_selected)
                VALUES (@id, 183, 814, @Tenant, @n, @logical, @jobId, @status, @selected);
                """, new
            {
                id = Guid.Parse($"aaaaaaaa-0000-0000-0000-{i:D12}"),
                Tenant,
                n = i + 1,
                logical = $"render_job_scene_video-job-{jobId:N}-scene-814",
                jobId,
                status = i == 1 ? "completed" : "failed",
                selected = i == 1
            });
        }

        await conn.ExecuteAsync("""
            INSERT INTO render.render_jobs (id, tenant_id, job_type, status, input_json, error_code,
                   error_message, attempt_count, max_attempts, created_at)
            VALUES ('4190c02c-5279-4534-8ec6-95fbcbbf05b9', @Tenant, 'render_scene_video',
                    'pending_reconciliation',
                    '{"projectId":"183","sceneId":"814","logicalRequestId":"render_job_scene_video-job-4190c02c527945348ec695fbcbbf05b9-scene-814"}'::jsonb,
                    'RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED',
                    'Scene video claim rejected; manual reconciliation required.', 1, 3,
                    now() - interval '2 hours');
            """, new { Tenant });
    }

    /// <summary>Creates a parent batch job and the child scene-video job already leased to a worker.</summary>
    private static async Task<RenderJobDto> CreateClaimedChildAsync(string workerKey = "worker-A")
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await using var conn = await OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO render.render_jobs (id, tenant_id, job_type, status, input_json, attempt_count, max_attempts)
            VALUES (@id, @Tenant, 'render_video_batch', 'rendering',
                    '{"projectId":"183","sceneId":"814"}'::jsonb, 1, 3);
            """, new { id = parentId, Tenant });

        var logical = SceneVideoRenderHandler.BuildLogicalRequestId(parentId, SceneId);
        await conn.ExecuteAsync("""
            INSERT INTO render.render_jobs (id, tenant_id, job_type, status, worker_key, lock_owner,
                   lock_until, input_json, attempt_count, max_attempts)
            VALUES (@id, @Tenant, 'render_scene_video', 'preparing', @worker, @worker,
                    now() + interval '10 minutes', CAST(@input AS jsonb), 1, 3);
            """, new
        {
            id = childId,
            Tenant,
            worker = workerKey,
            input = $"{{\"projectId\":\"183\",\"sceneId\":\"814\",\"parentJobId\":\"{parentId}\",\"logicalRequestId\":\"{logical}\"}}"
        });

        return new RenderJobDto
        {
            Id = childId,
            TenantId = Tenant,
            JobType = RenderJobTypes.RenderSceneVideo,
            Status = RenderJobStatuses.Preparing,
            WorkerKey = workerKey,
            AttemptCount = 1,
            MaxAttempts = 3,
            InputJson = "{}"
        };
    }

    private static SkipOrRun Gate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return SkipOrRun.Skip($"Set {EnvName} to a disposable PostgreSQL database to run this test.");
        }
        return SkipOrRun.Run();
    }

    private readonly record struct SkipOrRun(bool ShouldSkip, string? Reason)
    {
        public static SkipOrRun Run() => new(false, null);
        public static SkipOrRun Skip(string reason) => new(true, reason);
    }

    [Fact]
    public async Task TestA_FirstRenderOfSceneWithoutVersionsIsClaimed()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        var job = await CreateClaimedChildAsync();

        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var status = await conn.ExecuteScalarAsync<string>(
            "SELECT status FROM render.render_jobs WHERE id=@id", new { id = job.Id });
        Assert.Equal(RenderJobStatuses.Rendering, status);
    }

    [Fact]
    public async Task TestB_NewOperationIsClaimedEvenThoughSceneHasV2ToV4()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();

        // Previously threw RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED because the scene already had versions.
        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var status = await conn.ExecuteScalarAsync<string>(
            "SELECT status FROM render.render_jobs WHERE id=@id", new { id = job.Id });
        Assert.Equal(RenderJobStatuses.Rendering, status);
    }

    [Fact]
    public async Task TestC_NewVersionBelongsToTheNewJobAndItsLogicalRequestId()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var logical = await conn.ExecuteScalarAsync<string>(
            "SELECT input_json->>'logicalRequestId' FROM render.render_jobs WHERE id=@id", new { id = job.Id });
        var versionId = Guid.NewGuid();
        var nextNumber = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(max(version_number),0)+1 FROM video_render.scene_video_versions WHERE scene_id=814");
        await conn.ExecuteAsync("""
            INSERT INTO video_render.scene_video_versions
              (id, project_id, scene_id, tenant_id, version_number, logical_request_id, render_job_id, status)
            VALUES (@id, 183, 814, @Tenant, @n, @logical, @job, 'queued');
            """, new { id = versionId, Tenant, n = nextNumber, logical, job = job.Id });

        Assert.Equal(5, nextNumber);
        var owner = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT render_job_id FROM video_render.scene_video_versions WHERE id=@id", new { id = versionId });
        var ownerLogical = await conn.ExecuteScalarAsync<string>(
            "SELECT logical_request_id FROM video_render.scene_video_versions WHERE id=@id", new { id = versionId });
        Assert.Equal(job.Id, owner);
        Assert.Equal(logical, ownerLogical);

        // The submit-intent verification must accept the new attempt.
        await fixture.Service.VerifySceneVideoSubmitClaimAsync(job, versionId, logical!);
    }

    [Fact]
    public async Task TestE_DoubleClickProducesExactlyOneSubmitIntent()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var logical = await conn.ExecuteScalarAsync<string>(
            "SELECT input_json->>'logicalRequestId' FROM render.render_jobs WHERE id=@id", new { id = job.Id });

        const string sql = """
            INSERT INTO render.scene_video_submit_intents
                (tenant_id, scene_id, logical_request_id, render_job_id, owner_token)
            VALUES (@Tenant, 814, @logical, @job, @token)
            ON CONFLICT (tenant_id, scene_id, logical_request_id) DO NOTHING;
            """;
        var first = await conn.ExecuteAsync(sql, new { Tenant, logical, job = job.Id, token = Guid.NewGuid() });
        var second = await conn.ExecuteAsync(sql, new { Tenant, logical, job = job.Id, token = Guid.NewGuid() });

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task TestF_ExpiredLeaseIsRefused()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await using (var conn = await OpenAsync())
        {
            await conn.ExecuteAsync(
                "UPDATE render.render_jobs SET lock_until=now() - interval '1 minute' WHERE id=@id", new { id = job.Id });
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A"));
        Assert.Equal("RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED", ex.Message);
    }

    [Fact]
    public async Task TestF_WrongWorkerKeyIsRefused()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-B"));
        Assert.Equal("RVIDEO_JOB_CALLER_OWNERSHIP_LOST", ex.Message);
    }

    [Fact]
    public async Task TestF_AnotherLiveOwnerIsRefused()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await using (var conn = await OpenAsync())
        {
            await conn.ExecuteAsync("""
                INSERT INTO render.render_jobs (id, tenant_id, job_type, status, worker_key, lock_owner,
                       lock_until, input_json)
                VALUES (gen_random_uuid(), @Tenant, 'render_scene_video', 'rendering', 'worker-X', 'worker-X',
                        now() + interval '5 minutes',
                        '{"projectId":"183","sceneId":"814","logicalRequestId":"other-live-operation"}'::jsonb);
                """, new { Tenant });
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A"));
        Assert.Equal("RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED", ex.Message);
    }

    [Fact]
    public async Task TestF_WrongTenantIsRefused()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await using (var conn = await OpenAsync())
        {
            await conn.ExecuteAsync(
                "UPDATE render.render_jobs SET tenant_id='22222222-2222-2222-2222-222222222222' WHERE id=@id",
                new { id = job.Id });
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A"));
        Assert.Equal("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED", ex.Message);
    }

    [Fact]
    public async Task TestG_HistoricalVersionsAndSelectionAreUntouched()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();
        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var history = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM video_render.scene_video_versions WHERE scene_id=814 AND version_number IN (2,3,4)");
        var selected = await conn.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM video_render.scene_video_versions
             WHERE scene_id=814 AND version_number=2 AND is_selected AND status='completed'
            """);
        Assert.Equal(3, history);
        Assert.Equal(1, selected);
    }

    [Fact]
    public async Task TestH_QuarantinedJobIsNotRetriedAndDoesNotBlockANewOperation()
    {
        var gate = Gate();
        if (gate.ShouldSkip) return;

        await using var fixture = await CreateFixtureAsync();
        await SeedScene814HistoryAsync();
        var job = await CreateClaimedChildAsync();

        // The new operation is permitted while 4190c02c stays quarantined.
        await fixture.Service.MarkSceneVideoClaimRenderingAsync(job, "worker-A");

        await using var conn = await OpenAsync();
        var quarantined = await conn.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM render.render_jobs
             WHERE id='4190c02c-5279-4534-8ec6-95fbcbbf05b9'
               AND status='pending_reconciliation'
               AND error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
               AND attempt_count=1 AND provider_task_id IS NULL AND lock_owner IS NULL
            """);
        Assert.Equal(1, quarantined);
    }
}