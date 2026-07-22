using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public class AiCoreRuntimeTask4Tests
{
    [Fact]
    public void GenericBillingContracts_ArePresent()
    {
        Assert.True(typeof(IAiBillingService).IsInterface);
        Assert.True(typeof(IAiBillingDashboardService).IsInterface);
        Assert.True(typeof(IAiProviderUsageService).IsInterface);
        Assert.True(typeof(IAiProviderUsageRepository).IsInterface);
    }

    [Fact]
    public void GenericBillingEstimate_SeparatesProviderUsdFromTodoXPoints()
    {
        var estimate = new AiBillingEstimateResult(
            EstimatedPoints: 0.064m,
            ProviderEstimatedCostUsd: 0.08m,
            ExchangeRateVndPerUsd: 8000m,
            TodoXVndPerPoint: 10000m);

        Assert.Equal(0.064m, estimate.EstimatedPoints);
        Assert.Equal(0.08m, estimate.ProviderEstimatedCostUsd);
    }

    [Fact]
    public void UsageService_SourceContainsIdempotentGenericMapping()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "AiProviders", "AiProviderUsageService.cs"));

        Assert.Contains("public.todox_ai_provider_usage_log", source);
        Assert.Contains("ON CONFLICT (idempotency_key)", source);
        Assert.Contains("logical_request_id", source);
        Assert.Contains("render_job_id", source);
        Assert.Contains("provider_task_id", source);
        Assert.DoesNotContain(" request_id,", source);
        Assert.DoesNotContain(" job_id,", source);
    }

    [Fact]
    public void Task4Sql_AddsUsageAndBillingContractIndexes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sql35 = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "35_task4_billing_contract_fixes.sql"));
        var sql36 = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "36_task4_usage_contract_fixes.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "37_verify_task4_runtime.sql"));

        Assert.Contains("ai_provider_attempts_record_attempt_uk", sql35);
        Assert.Contains("todox_ai_provider_usage_log_idempotency_uk", sql36);
        Assert.Contains("Task 4 runtime verification passed", verify);
    }

    [Fact]
    public void DanceSellCompletion_UsesGuidCustomerForUsageLog()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "DanceSell", "DanceSellCompletionService.cs"));

        Assert.Contains("CustomerGuid = danceJob.CustomerId", source);
        Assert.DoesNotContain("CustomerId = ToBigIntCustomerId(danceJob.CustomerId)", source);
    }
}
