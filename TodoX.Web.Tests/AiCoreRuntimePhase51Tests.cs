using TodoX.Web.Services.AiProviders;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace TodoX.Web.Tests;

public class AiCoreRuntimePhase51Tests
{
    [Fact]
    public void ImageBillingService_IsOnlyACompatibilityAdapter()
    {
        var source = ReadRepoFile("TodoX.Web", "Services", "AiProviders", "AiImageBillingService.cs");

        Assert.Contains("Obsolete", source);
        Assert.DoesNotContain("using Dapper", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TodoXConnectionFactory", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAsync", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("billing.ai_", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("billing.token_", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token_wallets", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteAsync", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Query", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericBillingRepository_OwnsDbLocksAndIdempotency()
    {
        var source = ReadRepoFile("TodoX.Web", "Services", "AiProviders", "AiBillingRepository.cs");

        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("FOR UPDATE", source);
        Assert.Contains("FOR UPDATE SKIP LOCKED", source);
        Assert.Contains("ON CONFLICT", source);
        Assert.Contains("ai_billing_records", source);
        Assert.Contains("token_wallets", source);
        Assert.Contains("token_transactions", source);
    }

    [Fact]
    public void GenericBillingRepository_ExposesRequiredStateMachineMethods()
    {
        var names = typeof(IAiBillingRepository).GetMethods().Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GetOrCreateReservationAsync", names);
        Assert.Contains("CompleteBillingAsync", names);
        Assert.Contains("ReleaseReservationAsync", names);
        Assert.Contains("RefundAsync", names);
        Assert.Contains("ClaimReconciliationBatchAsync", names);
        Assert.Contains("RescheduleReconciliationAsync", names);
        Assert.Contains("MarkManualReviewAsync", names);
        Assert.Contains("UpsertProviderAttemptAsync", names);
    }

    [Fact]
    public void BillingHardeningSql_CoversLedgerIdempotencyAndRefundBounds()
    {
        var sql43 = ReadRepoFile("database", "manual", "ai-core-reset", "43_phase5_1_billing_hardening.sql");
        var sql44 = ReadRepoFile("database", "manual", "ai-core-reset", "44_phase5_1_completion_hardening.sql");
        var sql45 = ReadRepoFile("database", "manual", "ai-core-reset", "45_verify_phase5_1_prod_readiness.sql");

        Assert.Contains("token_transactions_ai_reserve_once_uk", sql43);
        Assert.Contains("token_transactions_ai_charge_once_uk", sql43);
        Assert.Contains("token_transactions_ai_refund_once_uk", sql43);
        Assert.Contains("render_artifacts_job_type_url_phase5_1_uk", sql44);
        Assert.Contains("todox_ai_provider_usage_log_idempotency_phase5_1_uk", sql44);
        Assert.Contains("refunded_points > charged_points", sql45);
        Assert.Contains("Safety stop", sql45);
    }

    [Fact]
    public async Task WalletLockingIntegration_ConcurrentSameLogicalRequest()
    {
        await using var setup = await OpenTodoSaasAsync();
        var tenantId = await GetTenantIdAsync(setup);
        var walletId = Guid.NewGuid();
        var logicalRequestId = $"phase51-wallet-{Guid.NewGuid():N}";
        await ExecuteAsync(setup,
            """
            INSERT INTO billing.token_wallets
                (id, tenant_id, customer_id, wallet_scope, balance, locked_balance, overdraft_limit, low_balance_threshold, status, created_at, updated_at)
            VALUES
                (@walletId, @tenantId, @customerId, 'customer', 100, 0, 0, 0, 'active', now(), now());
            """,
            ("walletId", walletId),
            ("tenantId", tenantId),
                ("customerId", null));

        async Task ReserveOnceAsync()
        {
            await using var conn = await OpenTodoSaasAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await ExecuteAsync(conn,
                """
                SELECT pg_advisory_xact_lock(hashtextextended(@logicalRequestId, 0));
                WITH billing_record AS (
                    INSERT INTO billing.ai_billing_records
                        (id, tenant_id, logical_request_id, wallet_id, status, billing_status, refund_status,
                         estimated_points, reserved_points, charged_points, refunded_points, created_at, updated_at)
                    VALUES
                        (gen_random_uuid(), @tenantId, @logicalRequestId, @walletId, 'reserved', 'reserved', 'none',
                         10, 10, 0, 0, now(), now())
                    ON CONFLICT (logical_request_id) DO NOTHING
                    RETURNING id
                ),
                existing_record AS (
                    SELECT id FROM billing_record
                    UNION ALL
                    SELECT id FROM billing.ai_billing_records WHERE logical_request_id=@logicalRequestId
                    LIMIT 1
                ),
                inserted_tx AS (
                    INSERT INTO billing.token_transactions
                        (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                         reference_type, reference_id, description, created_at)
                    SELECT gen_random_uuid(), @tenantId, @walletId, 'reserve', 10, 100, 90,
                           'ai_billing_reservation', id, 'phase51 concurrent reserve', now()
                      FROM existing_record
                    ON CONFLICT DO NOTHING
                    RETURNING id
                )
                UPDATE billing.token_wallets
                   SET balance = balance - 10,
                       locked_balance = locked_balance + 10,
                       updated_at = now()
                 WHERE id=@walletId
                   AND EXISTS (SELECT 1 FROM inserted_tx);
                """,
                tx,
                ("tenantId", tenantId),
                ("walletId", walletId),
                ("logicalRequestId", logicalRequestId));
            await tx.CommitAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReserveOnceAsync()));

        var billingCount = await ScalarAsync<long>(setup, "SELECT count(*) FROM billing.ai_billing_records WHERE logical_request_id=@logicalRequestId;", ("logicalRequestId", logicalRequestId));
        var txCount = await ScalarAsync<long>(setup, "SELECT count(*) FROM billing.token_transactions WHERE reference_type='ai_billing_reservation' AND reference_id=(SELECT id FROM billing.ai_billing_records WHERE logical_request_id=@logicalRequestId);", ("logicalRequestId", logicalRequestId));
        var balance = await ScalarAsync<decimal>(setup, "SELECT balance FROM billing.token_wallets WHERE id=@walletId;", ("walletId", walletId));
        var locked = await ScalarAsync<decimal>(setup, "SELECT locked_balance FROM billing.token_wallets WHERE id=@walletId;", ("walletId", walletId));

        Assert.Equal(1, billingCount);
        Assert.Equal(1, txCount);
        Assert.Equal(90, balance);
        Assert.Equal(10, locked);
    }

    [Fact]
    public async Task ProviderAccountConcurrencyIntegration_SkipLockedAndLeaseLimits()
    {
        await using var conn = await OpenTodoSaasAsync();
        var provider = await ReadProviderRouteAsync(conn);
        var accountIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var jobIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var accountId in accountIds)
        {
            await ExecuteAsync(conn,
                """
                INSERT INTO public.todox_ai_provider_account
                    (id, provider_id, provider_code, account_code, account_name, environment, enabled, is_default,
                     priority, weight, max_concurrency, health_status, config_json, created_at, updated_at)
                VALUES
                    (@accountId, @providerId, @providerCode, @accountCode, 'Phase51 test account', 'test', true, false,
                     10, 100, 1, 'healthy', '{}'::jsonb, now(), now());
                """,
                ("accountId", accountId),
                ("providerId", provider.ProviderId),
                ("providerCode", provider.ProviderCode),
                ("accountCode", $"phase51-{accountId:N}"));
        }

        foreach (var jobId in jobIds)
        {
            await InsertRenderJobAsync(conn, jobId, provider.ProviderCode, provider.ModelName, "phase51-account-concurrency");
        }

        var repo = new AiProviderAccountRepository(new TodoX.Web.Data.TodoXConnectionFactory(BuildConfiguration()));
        var claimRequests = jobIds
            .Select(jobId => new
            {
                JobId = jobId,
                WorkerKey = $"phase51-worker-{jobId:N}"
            })
            .ToArray();
        var claims = await Task.WhenAll(claimRequests.Select(request => repo.ClaimAccountAsync(new AiProviderAccountSelectionRequest(
            request.JobId,
            provider.ProviderCode,
            provider.CapabilityCode,
            provider.OperationType,
            provider.ModelName,
            request.WorkerKey,
            TimeSpan.FromMinutes(5)))));

        var claimed = claims.Where(x => x.Claimed).ToList();
        Assert.True(claimed.Count >= 2);
        Assert.Equal(claimed.Count, claimed.Select(x => x.ProviderAccountId).Distinct().Count());
        foreach (var accountId in accountIds)
        {
            var active = await ScalarAsync<long>(conn, "SELECT count(*) FROM public.todox_ai_provider_account_lease WHERE provider_account_id=@accountId AND lease_status='active' AND lease_until > now();", ("accountId", accountId));
            Assert.True(active <= 1);
        }

        for (var i = 0; i < claims.Length; i++)
        {
            if (claims[i].LeaseId is Guid leaseId)
            {
                Assert.True(await repo.ReleaseLeaseAsync(leaseId, claimRequests[i].WorkerKey, "test_cleanup"));
            }
        }
    }

    [Fact]
    public async Task CallbackPollRaceIntegration_CompletesExactlyOnce()
    {
        await using var conn = await OpenTodoSaasAsync();
        var provider = await ReadProviderRouteAsync(conn);
        var jobId = Guid.NewGuid();
        var taskId = $"phase51-task-{Guid.NewGuid():N}";
        await InsertRenderJobAsync(conn, jobId, provider.ProviderCode, provider.ModelName, "phase51-callback-race");

        async Task<bool> CompleteFromAsync(string source)
        {
            await using var raceConn = await OpenTodoSaasAsync();
            await using var tx = await raceConn.BeginTransactionAsync();
            var won = await ScalarAsync<Guid?>(raceConn,
                """
                UPDATE render.render_jobs
                   SET status='completed',
                       provider_task_id=@taskId,
                       output_json=jsonb_build_object('source', @source, 'outputUrls', jsonb_build_array('https://cdn.example/video.mp4')),
                       completed_at=COALESCE(completed_at, now()),
                       updated_at=now()
                 WHERE id=@jobId
                   AND status NOT IN ('completed','failed','cancelled')
                RETURNING id;
                """,
                tx,
                ("jobId", jobId),
                ("taskId", taskId),
                ("source", source));
            if (won is null)
            {
                await tx.CommitAsync();
                return false;
            }

            await ExecuteAsync(raceConn,
                """
                INSERT INTO render.render_artifacts
                    (id, render_job_id, artifact_type, public_url, provider_url, mime_type, metadata_json, created_at)
                VALUES
                    (gen_random_uuid(), @jobId, 'final_video', 'https://cdn.example/video.mp4', 'https://cdn.example/video.mp4', 'video/mp4',
                     jsonb_build_object('source', @source), now())
                ON CONFLICT DO NOTHING;
                INSERT INTO public.todox_ai_provider_usage_log
                    (id, render_job_id, provider_code, capability_code, model_name, provider_task_id,
                     logical_request_id, quantity, unit_type, status, idempotency_key, finalized_at, created_at)
                VALUES
                    (gen_random_uuid(), @jobId, @providerCode, @capabilityCode, @modelName, @taskId,
                     @logicalRequestId, 1, 'request', 'success', @idempotencyKey, now(), now())
                ON CONFLICT DO NOTHING;
                INSERT INTO render.render_job_events
                    (id, job_id, event_type, level, message, provider_code, model_code, provider_task_id, created_at)
                VALUES
                    (gen_random_uuid(), @jobId, 'job_completed', 'info', 'Phase51 terminal race completed.',
                     @providerCode, @modelName, @taskId, now());
                """,
                tx,
                ("jobId", jobId),
                ("taskId", taskId),
                ("source", source),
                ("providerCode", provider.ProviderCode),
                ("capabilityCode", provider.CapabilityCode),
                ("modelName", provider.ModelName),
                ("logicalRequestId", $"phase51-race-{jobId:N}"),
                ("idempotencyKey", $"{jobId:N}:{taskId}:terminal"));
            await tx.CommitAsync();
            return true;
        }

        var completed = await Task.WhenAll(CompleteFromAsync("callback"), CompleteFromAsync("poll"));

        Assert.Single(completed.Where(x => x));
        Assert.Equal("completed", await ScalarAsync<string>(conn, "SELECT status FROM render.render_jobs WHERE id=@jobId;", ("jobId", jobId)));
        Assert.Equal(1, await ScalarAsync<long>(conn, "SELECT count(*) FROM render.render_artifacts WHERE render_job_id=@jobId AND artifact_type='final_video';", ("jobId", jobId)));
        Assert.Equal(1, await ScalarAsync<long>(conn, "SELECT count(*) FROM public.todox_ai_provider_usage_log WHERE render_job_id=@jobId AND idempotency_key=@key;", ("jobId", jobId), ("key", $"{jobId:N}:{taskId}:terminal")));
        Assert.Equal(1, await ScalarAsync<long>(conn, "SELECT count(*) FROM render.render_job_events WHERE render_job_id=@jobId AND event_type='job_completed';", ("jobId", jobId)));
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(FindRepoRoot(), "TodoX.Web", "appsettings.json"), optional: false)
            .Build();

    private static async Task<NpgsqlConnection> OpenTodoSaasAsync()
    {
        var connectionString = BuildConfiguration().GetConnectionString("TodoXSaaS")
            ?? throw new InvalidOperationException("Missing TodoXSaaS connection string.");
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var database = await ScalarAsync<string>(conn, "SELECT current_database();");
        if (!string.Equals(database, "todo_saas", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Safety stop: connected database is {database}, expected todo_saas.");
        }

        return conn;
    }

    private static async Task<Guid> GetTenantIdAsync(NpgsqlConnection conn)
        => await ScalarAsync<Guid>(conn, "SELECT id FROM system.tenants ORDER BY created_at NULLS LAST, id LIMIT 1;");

    private static async Task<ProviderRouteRow> ReadProviderRouteAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT p.id, p.provider_code, c.capability_code, COALESCE(c.operation_type, c.capability_code) AS operation_type, c.model_name
              FROM public.todox_ai_provider p
              JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
             WHERE p.provider_code = 'kie'
               AND p.enabled = true
               AND c.enabled = true
             ORDER BY c.id
             LIMIT 1;
            """,
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("KIE provider/capability seed is missing.");
        }

        return new ProviderRouteRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task InsertRenderJobAsync(NpgsqlConnection conn, Guid jobId, string providerCode, string? modelName, string jobType)
        => await ExecuteAsync(conn,
            """
            INSERT INTO render.render_jobs
                (id, job_type, status, provider_code, model_code, input_json, prompt_json, reference_json, output_json,
                 result_summary_json, last_provider_response_json, provider_request_json, provider_response_json,
                 provider_usage_json, queued_at, created_at, updated_at)
            VALUES
                (@jobId, @jobType, 'queued', @providerCode, @modelName, '{}'::jsonb, '{}'::jsonb, '[]'::jsonb, '[]'::jsonb,
                 '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, now(), now(), now());
            """,
            ("jobId", jobId),
            ("jobType", jobType),
            ("providerCode", providerCode),
            ("modelName", modelName));

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, params (string Name, object? Value)[] parameters)
        => await ExecuteAsync(conn, sql, transaction: null, parameters);

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, NpgsqlTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, string sql, params (string Name, object? Value)[] parameters)
        => await ScalarAsync<T>(conn, sql, transaction: null, parameters);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, string sql, NpgsqlTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var scalar = await cmd.ExecuteScalarAsync();
        if (scalar is null or DBNull)
        {
            return default!;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(Guid) && scalar is Guid guid)
        {
            return (T)(object)guid;
        }

        return (T)Convert.ChangeType(scalar, targetType);
    }

    private sealed record ProviderRouteRow(long ProviderId, string ProviderCode, string CapabilityCode, string OperationType, string? ModelName);

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
