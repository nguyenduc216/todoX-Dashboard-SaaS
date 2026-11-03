using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Only run against an operator-owned isolated database with disposable parent rows.</summary>
public sealed class SceneVideoPreDispatchPostgresTests
{
    private sealed class Harness : IAsyncDisposable
    {
        private readonly NpgsqlConnection _db;
        private readonly Dictionary<Guid, Guid> _jobs = [];
        private readonly HashSet<Guid> _versions = [];
        public readonly RenderJobService Jobs;
        public readonly Guid Tenant;
        public readonly long Project;
        public readonly long Scene;

        private Harness(NpgsqlConnection db, RenderJobService jobs, Guid tenant, long project, long scene)
        { _db = db; Jobs = jobs; Tenant = tenant; Project = project; Scene = scene; }

        public static async Task<Harness> OpenAsync()
        {
            // Pending full FK/schema inventory and proof that global claim cannot take another
            // session's queued job. Never connect or seed until those hazards are resolved.
            throw new InvalidOperationException("Scene-video PostgreSQL fixture is not yet safe for DB execution.");
#pragma warning disable CS0162
            if (Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_TEST_APPROVED") != "YES"
                || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_OWNER_CONFIRMED") != "YES"
                || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DISPOSABLE_ROWS_APPROVED") != "YES")
                throw new InvalidOperationException("Isolated PostgreSQL and disposable-row approval required.");

            var value = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_CONNECTION_STRING");
            var expected = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_DATABASE_NAME");
            var expectedUser = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_DATABASE_USER");
            var expectedHost = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_HOST");
            var expectedPort = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_PORT");
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(expected)
                || string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedHost)
                || !int.TryParse(expectedPort, out var port))
                throw new InvalidOperationException("Explicit test connection, database name and database user required.");
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (!expected.Contains("test", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(builder.Database, expected, StringComparison.Ordinal)
                || string.Equals(expected, "postgres", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(builder.Username, expectedUser, StringComparison.Ordinal)
                || !string.Equals(builder.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
                || builder.Port != port || builder.Multiplexing || builder.TargetSessionAttributes is not null)
                throw new InvalidOperationException("Test database allowlist mismatch.");
            // The operator must independently verify the server endpoint and database identity.
            if (expectedHost.Contains(',') || expectedHost.Contains("prod", StringComparison.OrdinalIgnoreCase)
                || expected.Contains("prod", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Production-like or multi-host targets are forbidden.");
            var tenant = Guid.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_TENANT_ID")
                ?? throw new InvalidOperationException("Test tenant required."));
            var project = long.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_PROJECT_ID")
                ?? throw new InvalidOperationException("Disposable project required."));
            var scene = long.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_SCENE_ID")
                ?? throw new InvalidOperationException("Disposable scene required."));
            var otherTenant = Guid.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_OTHER_TENANT_ID")
                ?? throw new InvalidOperationException("Second test tenant required."));
            if (otherTenant == tenant || project <= 0 || scene <= 0)
                throw new InvalidOperationException("Distinct tenants and positive parent IDs required.");
            var db = new NpgsqlConnection(builder.ConnectionString);
            try
            {
                await db.OpenAsync();
                var identity = await db.QuerySingleAsync<(string Database, string User)>(
                    "SELECT current_database() AS Database, current_user AS User");
                if (identity.Database != expected || identity.User != expectedUser)
                    throw new InvalidOperationException("Connected database identity mismatch.");
                var server = await db.QuerySingleAsync<(string Address, int Port)>(
                    "SELECT inet_server_addr()::text AS Address, inet_server_port() AS Port");
                var expectedAddress = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_SERVER_ADDRESS");
                if (string.IsNullOrWhiteSpace(expectedAddress) || server.Address != expectedAddress || server.Port != port)
                    throw new InvalidOperationException("Server address/port mismatch (Unix socket unsupported).");
                var columns = await db.ExecuteScalarAsync<int>("""
                    SELECT count(*) FROM information_schema.columns WHERE
                        (table_schema='render' AND table_name='render_jobs' AND column_name IN ('id','tenant_id','input_json','status','lock_owner','lock_until','attempt_count'))
                        OR (table_schema='video_render' AND table_name='scene_video_versions' AND column_name IN ('id','tenant_id','project_id','scene_id','version_number','logical_request_id','render_job_id'))
                    """);
                if (columns != 14) throw new InvalidOperationException("Required render/video schema is not compatible.");
                // Inventory every live incoming FK, including extensions not present in checked-in SQL.
                // Reject any new dependency rather than risking CASCADE/SET NULL on an unowned row.
                var incoming = (await db.QueryAsync<string>("""
                    SELECT child_ns.nspname || '.' || child.relname || '(' ||
                           string_agg(att.attname, ',' ORDER BY key.ordinality) || ')->' ||
                           parent_ns.nspname || '.' || parent.relname || ':' || c.confdeltype
                    FROM pg_constraint c
                    JOIN pg_class child ON child.oid=c.conrelid
                    JOIN pg_namespace child_ns ON child_ns.oid=child.relnamespace
                    JOIN pg_class parent ON parent.oid=c.confrelid
                    JOIN pg_namespace parent_ns ON parent_ns.oid=parent.relnamespace
                    JOIN unnest(c.conkey) WITH ORDINALITY key(attnum, ordinality) ON true
                    JOIN pg_attribute att ON att.attrelid=child.oid AND att.attnum=key.attnum
                    WHERE c.contype='f' AND c.confrelid IN
                          ('render.render_jobs'::regclass, 'video_render.scene_video_versions'::regclass)
                    GROUP BY c.oid, child_ns.nspname, child.relname, parent_ns.nspname, parent.relname, c.confdeltype
                    """)).OrderBy(x => x).ToArray();
                var allowed = new[]
                {
                    "render.render_job_events(job_id)->render.render_jobs:c",
                    "render.render_jobs(retry_of_job_id)->render.render_jobs:n",
                    "video_render.scene_image_versions(render_job_id)->render.render_jobs:n",
                    "video_render.scene_video_versions(render_job_id)->render.render_jobs:n",
                    "video_render.final_video_versions(render_job_id)->render.render_jobs:n"
                }.OrderBy(x => x).ToArray();
                if (!incoming.SequenceEqual(allowed))
                    throw new InvalidOperationException("Unknown/missing incoming FK; fixture cannot safely clean up.");
                var requiredConstraints = await db.ExecuteScalarAsync<int>("""
                    SELECT count(*) FROM pg_constraint WHERE conname IN
                    ('render_jobs_pkey','ck_render_jobs_status','ck_render_jobs_point_status','ck_render_jobs_job_type',
                     'ck_render_jobs_attempts','scene_video_versions_pkey','ux_scene_video_version_number',
                     'ux_scene_video_logical_request','ck_scene_video_version_number','ck_scene_video_status','ck_scene_video_cost')
                    AND conrelid IN ('render.render_jobs'::regclass,'video_render.scene_video_versions'::regclass)
                    """);
                if (requiredConstraints != 11) throw new InvalidOperationException("Render/video constraints incompatible.");
                var parents = await db.ExecuteScalarAsync<int>("""
                    SELECT count(*) FROM video_render.video_project_scenes s
                    JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
                    JOIN system.tenants t ON t.id=p.tenant_id
                    WHERE s.id=@scene AND s.project_id=@project AND s.tenant_id=@tenant
                    """, new { scene, project, tenant });
                var secondTenant = await db.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM system.tenants WHERE id=@otherTenant", new { otherTenant });
                if (parents != 1 || secondTenant != 1)
                    throw new InvalidOperationException("Disposable parents and second tenant not confirmed.");
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:TodoXSaaS"] = builder.ConnectionString, ["TodoX:TenantId"] = tenant.ToString() }).Build();
                var factory = new TodoXConnectionFactory(configuration);
                return new Harness(db, new RenderJobService(factory, new TenantContext(factory, configuration),
                    configuration, NullLogger<RenderJobService>.Instance), tenant, project, scene);
            }
            catch { await db.DisposeAsync(); throw; }
#pragma warning restore CS0162
        }

        public async Task<Guid> JobAsync(long? project = null, long? scene = null, Guid? tenant = null, bool claim = false)
        {
            var id = Guid.NewGuid();
            var logical = $"pre-dispatch-test-{id:N}";
            await _db.ExecuteAsync("""
                INSERT INTO render.render_jobs(id, tenant_id, job_type, status, input_json)
                VALUES (@id, @tenant, 'render_scene_video', 'queued',
                    jsonb_build_object('projectId', @project, 'sceneId', @scene, 'logicalRequestId', @logical))
                """, new { id, tenant = tenant ?? Tenant, project = project ?? Project, scene = scene ?? Scene, logical });
            _jobs.Add(id, tenant ?? Tenant);
            if (claim)
            {
                var claimed = await Jobs.ClaimNextByJobTypeAsync($"pre-dispatch-{id:N}", TimeSpan.FromMinutes(2),
                    [RenderJobTypes.RenderSceneVideo]);
                Assert.Equal(id, claimed?.Id);
            }
            return id;
        }

        public async Task<Guid> VersionAsync(Guid owner, string? logical = null)
        {
            var id = Guid.NewGuid();
            logical ??= await _db.ExecuteScalarAsync<string>(
                "SELECT input_json->>'logicalRequestId' FROM render.render_jobs WHERE id=@owner", new { owner });
            await _db.ExecuteAsync("""
                INSERT INTO video_render.scene_video_versions
                    (id, tenant_id, project_id, scene_id, version_number, logical_request_id, render_job_id, status)
                VALUES (@id, @tenant, @project, @scene,
                    (SELECT coalesce(max(version_number),0)+1 FROM video_render.scene_video_versions
                     WHERE tenant_id=@tenant AND project_id=@project AND scene_id=@scene), @logical, @owner, 'queued')
                """, new { id, tenant = Tenant, project = Project, scene = Scene, logical, owner });
            _versions.Add(id);
            return id;
        }

        public async Task<string> StatusAsync(Guid id) => (await _db.ExecuteScalarAsync<string>(
            "SELECT status FROM render.render_jobs WHERE id=@id", new { id }))!;

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var id in _versions)
                {
                    // No externally-owned row may reference a test version.
                    var references = await _db.ExecuteScalarAsync<int>("""
                        SELECT count(*) FROM video_render.scene_video_versions
                        WHERE id=@id AND tenant_id=@tenant AND project_id=@project AND scene_id=@scene
                          AND logical_request_id LIKE 'pre-dispatch-test-%'
                        """, new { id, tenant = Tenant, project = Project, scene = Scene });
                    if (references != 1) throw new InvalidOperationException("Test version ownership changed; cleanup refused.");
                    var removed = await _db.ExecuteAsync("DELETE FROM video_render.scene_video_versions WHERE id=@id AND tenant_id=@tenant AND project_id=@project AND scene_id=@scene AND logical_request_id LIKE 'pre-dispatch-test-%'", new { id, tenant = Tenant, project = Project, scene = Scene });
                    if (removed != 1) throw new InvalidOperationException("Version cleanup count mismatch.");
                }
                foreach (var (id, tenant) in _jobs)
                {
                    var references = await _db.ExecuteScalarAsync<int>("""
                        SELECT (SELECT count(*) FROM video_render.scene_video_versions WHERE render_job_id=@id)
                             + (SELECT count(*) FROM video_render.scene_image_versions WHERE render_job_id=@id)
                             + (SELECT count(*) FROM video_render.final_video_versions WHERE render_job_id=@id)
                             + (SELECT count(*) FROM render.render_jobs WHERE retry_of_job_id=@id)
                        """, new { id });
                    if (references != 0) throw new InvalidOperationException("External version references test job; cleanup refused.");
                    var eventCount = await _db.ExecuteScalarAsync<int>(
                        "SELECT count(*) FROM render.render_job_events WHERE job_id=@id", new { id });
                    if (eventCount != 0) throw new InvalidOperationException("Unowned job event references test job; cleanup refused.");
                    var removed = await _db.ExecuteAsync("DELETE FROM render.render_jobs WHERE id=@id AND tenant_id=@tenant AND job_type='render_scene_video' AND input_json->>'logicalRequestId'=@logical", new { id, tenant, logical = $"pre-dispatch-test-{id:N}" });
                    if (removed != 1) throw new InvalidOperationException("Job cleanup count mismatch.");
                }
            }
            finally { await _db.DisposeAsync(); }
        }
    }

    [ApprovedReconciliationFact]
    public async Task ClaimedJobWithoutVersion_PreDispatchRendering_Succeeds()
    {
        await using var h = await Harness.OpenAsync();
        var job = await h.JobAsync(claim: true);
        Assert.Equal("preparing", await h.StatusAsync(job));
        await h.Jobs.MarkStatusAsync(job, RenderJobStatuses.Rendering);
        Assert.Equal("rendering", await h.StatusAsync(job));
    }

    [ApprovedReconciliationFact]
    public async Task WrongProjectAndScene_RejectAndPreserveStatus()
    {
        await using var h = await Harness.OpenAsync();
        foreach (var job in new[] { await h.JobAsync(project: -1), await h.JobAsync(scene: -1) })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Jobs.MarkStatusAsync(job, RenderJobStatuses.Rendering));
            Assert.Equal("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED", error.Message);
            Assert.Equal("queued", await h.StatusAsync(job));
        }
    }

    [ApprovedReconciliationFact]
    public async Task ConflictingLatestVersion_RejectsOldJob()
    {
        await using var h = await Harness.OpenAsync();
        var old = await h.JobAsync();
        await h.VersionAsync(old);
        var newer = await h.JobAsync();
        await h.VersionAsync(newer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Jobs.MarkStatusAsync(old, RenderJobStatuses.Rendering));
        Assert.Equal("queued", await h.StatusAsync(old));
    }

    [ApprovedReconciliationFact]
    public async Task VersionBoundToAnotherJob_RejectsWrongJob()
    {
        await using var h = await Harness.OpenAsync();
        var owner = await h.JobAsync();
        var wrong = await h.JobAsync();
        var logical = $"pre-dispatch-test-{wrong:N}";
        await h.VersionAsync(owner, logical);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Jobs.MarkStatusAsync(wrong, RenderJobStatuses.Rendering));
        Assert.Equal("queued", await h.StatusAsync(wrong));
    }

    [ApprovedReconciliationFact]
    public async Task BoundOwnerUpdates_WrongJobRemainsFenced()
    {
        await using var h = await Harness.OpenAsync();
        var owner = await h.JobAsync();
        var wrong = await h.JobAsync();
        await h.VersionAsync(owner);
        await h.Jobs.MarkStatusAsync(owner, RenderJobStatuses.Rendering);
        Assert.Equal("rendering", await h.StatusAsync(owner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Jobs.MarkStatusAsync(wrong, RenderJobStatuses.Rendering));
        Assert.Equal("queued", await h.StatusAsync(wrong));
    }

    [ApprovedReconciliationFact]
    public async Task WrongTenantIsRejectedWithoutChangingJob()
    {
        await using var h = await Harness.OpenAsync();
        var otherTenant = Guid.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_OTHER_TENANT_ID")
            ?? throw new InvalidOperationException("Approved second tenant required."));
        var job = await h.JobAsync(tenant: otherTenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Jobs.MarkStatusAsync(job, RenderJobStatuses.Rendering));
        Assert.Equal("queued", await h.StatusAsync(job));
    }

    [ApprovedReconciliationFact]
    public async Task SceneLockContention_WriterWaitsUntilOtherConnectionRollsBack()
    {
        await using var h = await Harness.OpenAsync();
        var job = await h.JobAsync();
        await h.VersionAsync(job);
        // The second session holds the same scene advisory key used by the production writer.
        var value = Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_CONNECTION_STRING")!;
        await using var other = new NpgsqlConnection(value);
        await other.OpenAsync();
        await using var tx = await other.BeginTransactionAsync();
        await other.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@name,0))",
            new { name = RenderJobService.BuildSceneJobLockName(RenderJobTypes.RenderSceneVideo, h.Scene) }, tx);
        var update = Task.Run(() => h.Jobs.MarkStatusAsync(job, RenderJobStatuses.Rendering));
        try
        {
            await Task.Delay(200);
            Assert.False(update.IsCompleted);
            Assert.Equal("queued", await h.StatusAsync(job));
        }
        finally { await tx.RollbackAsync(); }
        await update.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("rendering", await h.StatusAsync(job));
    }
}