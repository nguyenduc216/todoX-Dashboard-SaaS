using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// Only an operator with a separately verified disposable database may enable these tests.
public sealed class ApprovedReconciliationFactAttribute : FactAttribute
{
    public ApprovedReconciliationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_TEST_APPROVED") != "YES"
            || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DB_OWNER_CONFIRMED") != "YES"
            || Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_DISPOSABLE_ROWS_APPROVED") != "YES")
            Skip = "Isolated PostgreSQL test database approval required";
    }
}

public sealed class SceneImageEnqueuePostgresTests
{
    private static async Task<(ImageEnqueuePostgresTestFixture Fixture, NpgsqlConnection Connection,
        SceneImageEnqueueReconciliationRepository Repository, Guid Tenant, long Project, long Scene)> Setup()
    {
        var fixture = ImageEnqueuePostgresTestFixture.CreateApproved();
        try
        {
            var connection = await fixture.OpenAsync(CancellationToken.None);
            // The operator supplies existing disposable parent rows. Never create production projects.
            var tenant = Guid.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_TENANT_ID")
                ?? throw new InvalidOperationException("Approved test tenant required"));
            var project = long.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_PROJECT_ID")
                ?? throw new InvalidOperationException("Approved disposable test project required"));
            var scene = long.Parse(Environment.GetEnvironmentVariable("TODOX_RECONCILIATION_TEST_SCENE_ID")
                ?? throw new InvalidOperationException("Approved disposable test scene required"));
            var owner = await connection.ExecuteScalarAsync<int>("""
                SELECT count(*) FROM video_render.video_project_scenes s
                JOIN video_render.video_projects p ON p.id=s.project_id
                WHERE s.id=@scene AND p.id=@project AND p.tenant_id=@tenant
                """, new { scene, project, tenant });
            if (owner != 1) throw new InvalidOperationException("Test parent ownership not confirmed");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoXSaaS"] = fixture.ConnectionString,
                ["TodoX:TenantId"] = tenant.ToString()
            }).Build();
            var factory = new TodoXConnectionFactory(configuration);
            var context = new TenantContext(factory, configuration);
            await context.EnsureLoadedAsync();
            return (fixture, connection, new SceneImageEnqueueReconciliationRepository(factory, context), tenant, project, scene);
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private static async Task<Guid> Seed(NpgsqlConnection db, Guid tenant, long project, long scene,
        string status = "pending_reconciliation", string error = "RENDER_JOB_ENQUEUE_UNKNOWN", bool deadlinePast = false)
    {
        var id = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO video_render.scene_image_versions
              (id, tenant_id, project_id, scene_id, version_number, logical_request_id, status, error_code,
               enqueue_reconcile_after, enqueue_reconcile_deadline)
            VALUES (@id, @tenant, @project, @scene,
              (SELECT COALESCE(max(version_number),0)+1 FROM video_render.scene_image_versions WHERE scene_id=@scene),
              @logical, @status, @error, now()-interval '1 minute',
              now()+CASE WHEN @deadlinePast THEN interval '-1 minute' ELSE interval '1 hour' END)
            """, new { id, tenant, project, scene, logical = id.ToString(), status, error, deadlinePast });
        return id;
    }

    private static Task<int> Delete(NpgsqlConnection db, Guid id, Guid tenant) => db.ExecuteAsync(
        "DELETE FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant", new { id, tenant });

    private static async Task<Guid> SeedJob(NpgsqlConnection db, Guid tenant, Guid version,
        string type = "render_scene_image", string? versionValue = null, string? invalidField = null)
    {
        var job = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT INTO render.render_jobs (id, tenant_id, job_type, input_json)
            SELECT @job, @tenant, @type,
                   jsonb_build_object('imageVersionId', @identity,
                     'projectId', CASE WHEN @invalidField='projectId' THEN -1 ELSE v.project_id END,
                     'sceneId', CASE WHEN @invalidField='sceneId' THEN -1 ELSE v.scene_id END,
                     'logicalRequestId', CASE WHEN @invalidField='logicalRequestId' THEN 'invalid' ELSE v.logical_request_id END)
              FROM video_render.scene_image_versions v WHERE v.id=@version AND v.tenant_id=@tenant
            """, new { job, tenant, type, version, invalidField, identity = versionValue ?? version.ToString() });
        return job;
    }

    private static Task<int> DeleteJob(NpgsqlConnection db, Guid job, Guid tenant) => db.ExecuteAsync(
        "DELETE FROM render.render_jobs WHERE id=@job AND tenant_id=@tenant", new { job, tenant });

    [ApprovedReconciliationFact]
    public async Task ClaimIsExclusiveFencedAndReclaimedAfterLeaseOrRestart()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var id = await Seed(db, tenant, project, scene);
        try
        {
            var first = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, first?.VersionId);
            Assert.Null(await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None));
            await db.ExecuteAsync("""UPDATE video_render.scene_image_versions SET enqueue_reconcile_lease_until=now()-interval '1 second' WHERE id=@id AND tenant_id=@tenant""", new { id, tenant });
            var second = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, second?.VersionId);
            Assert.NotEqual(first!.Token, second!.Token);
            await repo.ReconcileAsync(first, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(second.Token, await db.ExecuteScalarAsync<Guid?>(
                "SELECT enqueue_reconcile_claim_token FROM video_render.scene_image_versions WHERE id=@id", new { id }));
            await repo.ReconcileAsync(second, TimeSpan.FromMinutes(1), CancellationToken.None);
            var state = await db.QuerySingleAsync<(int attempts, string status)>(
                "SELECT enqueue_reconcile_attempts AS attempts, status FROM video_render.scene_image_versions WHERE id=@id", new { id });
            Assert.Equal(1, state.attempts);
            Assert.Equal("pending_reconciliation", state.status);
        }
        finally { await Delete(db, id, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task DeadlineAndPositiveEvidenceRouteManualWithoutHandoff()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var id = await Seed(db, tenant, project, scene, deadlinePast: true);
        try
        {
            var claim = await repo.ClaimAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(id, claim?.VersionId);
            await repo.ReconcileAsync(claim!, TimeSpan.FromMinutes(1), CancellationToken.None);
            var state = await db.QuerySingleAsync<(DateTime? manual, DateTime? handoff, string reason)>(
                "SELECT enqueue_reconcile_manual_at AS manual, enqueue_reconcile_handed_off_at AS handoff, enqueue_reconcile_alert_type AS reason FROM video_render.scene_image_versions WHERE id=@id", new { id });
            Assert.NotNull(state.manual);
            Assert.Null(state.handoff);
            Assert.Equal("DEADLINE_EXPIRED", state.reason);
        }
        finally { await Delete(db, id, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task ProviderOwnedRowsAreExcludedAndSchemaHasRequiredColumns()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var count = await db.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='video_render' AND table_name='scene_image_versions'
            AND column_name IN ('enqueue_reconcile_after','enqueue_reconcile_deadline','enqueue_reconcile_claim_token',
                'enqueue_reconcile_lease_until','enqueue_reconcile_attempts','enqueue_reconcile_manual_at',
                'enqueue_reconcile_handed_off_at','enqueue_reconcile_alert_type')
            """);
        Assert.Equal(8, count);
        var id = await Seed(db, tenant, project, scene, status: "processing");
        try { Assert.Null(await repo.ClaimAsync(TimeSpan.FromMinutes(1), CancellationToken.None)); }
        finally { await Delete(db, id, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task TwoIndependentClaimersCannotOwnOneVersion()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var (first, second) = await fixture.OpenIndependentConnectionsAsync(CancellationToken.None);
        await using var sessionOne = first;
        await using var sessionTwo = second;
        var id = await Seed(db, tenant, project, scene);
        try
        {
            // Session one holds the row lock while the production claim on its own
            // connection must skip it rather than block or deadlock.
            await using var transaction = await sessionOne.BeginTransactionAsync();
            await sessionOne.ExecuteAsync(
                "SELECT id FROM video_render.scene_image_versions WHERE id=@id FOR UPDATE",
                new { id }, transaction);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Null(await repo.ClaimAsync(TimeSpan.FromMinutes(2), timeout.Token));
            await transaction.CommitAsync();

            // Both independent sessions synchronize their start; the repository
            // opens one real Npgsql connection per production claim invocation.
            using var barrier = new Barrier(2);
            var results = await Task.WhenAll(
                Task.Run(async () => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return await repo.ClaimAsync(TimeSpan.FromMinutes(2), timeout.Token); }),
                Task.Run(async () => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return await repo.ClaimAsync(TimeSpan.FromMinutes(2), timeout.Token); }));
            Assert.Single(results, c => c?.VersionId == id);
            Assert.Single(results, c => c is null);
        }
        finally { await Delete(db, id, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task ActiveLeaseSurvivesRestartAndExpiredLeaseCanBeReclaimed()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var id = await Seed(db, tenant, project, scene);
        try
        {
            var old = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, old?.VersionId);
            // New repository instance is the restarted worker; leases persist in DB.
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoXSaaS"] = fixture.ConnectionString,
                ["TodoX:TenantId"] = tenant.ToString()
            }).Build();
            var factory = new TodoXConnectionFactory(config);
            var context = new TenantContext(factory, config);
            await context.EnsureLoadedAsync();
            var restarted = new SceneImageEnqueueReconciliationRepository(factory, context);
            Assert.Null(await restarted.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None));
            await db.ExecuteAsync("""UPDATE video_render.scene_image_versions SET enqueue_reconcile_lease_until=now()-interval '1 second' WHERE id=@id AND tenant_id=@tenant""", new { id, tenant });
            var current = await restarted.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, current?.VersionId);
            await repo.ReconcileAsync(old!, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(current!.Token, await db.ExecuteScalarAsync<Guid?>(
                "SELECT enqueue_reconcile_claim_token FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant", new { id, tenant }));
            await restarted.ReconcileAsync(current, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(1, await db.ExecuteScalarAsync<int>(
                "SELECT enqueue_reconcile_attempts FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant", new { id, tenant }));
        }
        finally { await Delete(db, id, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task WrongJobTypeOrVersionDoesNotCountAsPositiveEvidence()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var id = await Seed(db, tenant, project, scene);
        var wrongType = await SeedJob(db, tenant, id, "render_scene_video");
        var wrongVersion = await SeedJob(db, tenant, id, versionValue: Guid.NewGuid().ToString());
        try
        {
            var claim = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, claim?.VersionId);
            await repo.ReconcileAsync(claim!, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Null(await db.ExecuteScalarAsync<DateTime?>(
                "SELECT enqueue_reconcile_manual_at FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant", new { id, tenant }));
        }
        finally { await Delete(db, id, tenant); await DeleteJob(db, wrongType, tenant); await DeleteJob(db, wrongVersion, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task DuplicateJobsAreManualEvenWithoutJobEventsOrPositiveHandoff()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        var id = await Seed(db, tenant, project, scene);
        var first = await SeedJob(db, tenant, id);
        var second = await SeedJob(db, tenant, id);
        try
        {
            var claim = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.Equal(id, claim?.VersionId);
            await repo.ReconcileAsync(claim!, TimeSpan.FromMinutes(1), CancellationToken.None);
            var result = await db.QuerySingleAsync<(string reason, DateTime? manual, DateTime? handoff)>(
                "SELECT enqueue_reconcile_alert_type AS reason, enqueue_reconcile_manual_at AS manual, enqueue_reconcile_handed_off_at AS handoff FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant", new { id, tenant });
            Assert.Equal("DUPLICATE_JOB_IDENTITY", result.reason);
            Assert.NotNull(result.manual);
            Assert.Null(result.handoff);
            Assert.Equal(1, await repo.CountManualBacklogAsync(CancellationToken.None));
            Assert.Equal(0, await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM render.render_job_events WHERE job_id IN (@first,@second)", new { first, second }));
        }
        finally { await Delete(db, id, tenant); await DeleteJob(db, first, tenant); await DeleteJob(db, second, tenant); }
    }

    [ApprovedReconciliationFact]
    public async Task MismatchedOrMissingIdentityRoutesToAmbiguousManualReview()
    {
        var (fixture, db, repo, tenant, project, scene) = await Setup();
        await using var disposable = fixture;
        await using var connection = db;
        foreach (var field in new[] { "projectId", "sceneId", "logicalRequestId" })
        {
            var id = await Seed(db, tenant, project, scene);
            Guid job = Guid.Empty;
            try
            {
                job = await SeedJob(db, tenant, id, invalidField: field);
                var claim = await repo.ClaimAsync(TimeSpan.FromMinutes(2), CancellationToken.None);
                Assert.Equal(id, claim?.VersionId);
                await repo.ReconcileAsync(claim!, TimeSpan.FromMinutes(1), CancellationToken.None);
                var state = await db.QuerySingleAsync<(string reason, DateTime? manual, DateTime? handoff)>(
                    "SELECT enqueue_reconcile_alert_type AS reason, enqueue_reconcile_manual_at AS manual, enqueue_reconcile_handed_off_at AS handoff FROM video_render.scene_image_versions WHERE id=@id AND tenant_id=@tenant",
                    new { id, tenant });
                Assert.Equal("AMBIGUOUS_JOB_IDENTITY", state.reason);
                Assert.NotNull(state.manual);
                Assert.Null(state.handoff);
            }
            finally
            {
                if (job != Guid.Empty) await DeleteJob(db, job, tenant);
                await Delete(db, id, tenant);
            }
        }
    }
}