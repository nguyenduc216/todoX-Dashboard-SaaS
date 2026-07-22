using TodoX.Web.Services.AiProviders;
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

    [Fact(Skip = "Requires disposable PostgreSQL/Testcontainers. A skipped DB integration suite is reported as NO-GO readiness evidence.")]
    public void WalletLockingIntegration_ConcurrentSameLogicalRequest()
    {
    }

    [Fact(Skip = "Requires disposable PostgreSQL/Testcontainers. A skipped DB integration suite is reported as NO-GO readiness evidence.")]
    public void ProviderAccountConcurrencyIntegration_SkipLockedAndLeaseLimits()
    {
    }

    [Fact(Skip = "Requires disposable PostgreSQL/Testcontainers. A skipped DB integration suite is reported as NO-GO readiness evidence.")]
    public void CallbackPollRaceIntegration_CompletesExactlyOnce()
    {
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
