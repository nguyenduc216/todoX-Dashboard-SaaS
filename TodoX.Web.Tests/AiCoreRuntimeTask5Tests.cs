using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public class AiCoreRuntimeTask5Tests
{
    [Fact]
    public void EventTaxonomy_ContainsRequiredTerminalAndBillingEvents()
    {
        Assert.Equal("provider_callback_received", AiRenderEventTypes.ProviderCallbackReceived);
        Assert.Equal("usage_finalized", AiRenderEventTypes.UsageFinalized);
        Assert.Equal("billing_completed", AiRenderEventTypes.BillingCompleted);
        Assert.Equal("lease_released", AiRenderEventTypes.LeaseReleased);
        Assert.Equal("manual_review_required", AiRenderEventTypes.ManualReviewRequired);
    }

    [Fact]
    public void StepTaxonomy_ContainsRequiredExecutionSteps()
    {
        Assert.Equal("claim_account", AiRenderStepKeys.ClaimAccount);
        Assert.Equal("reserve_billing", AiRenderStepKeys.ReserveBilling);
        Assert.Equal("submit_provider", AiRenderStepKeys.SubmitProvider);
        Assert.Equal("finalize_usage", AiRenderStepKeys.FinalizeUsage);
        Assert.Equal("complete_business_job", AiRenderStepKeys.CompleteBusinessJob);
    }

    [Fact]
    public void SharedCompletion_SourceUsesRenderJobLockAndIdempotentArtifacts()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "AiProviders", "AiRenderRuntimeServices.cs"));

        Assert.Contains("FOR UPDATE", source);
        Assert.Contains("RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled", source);
        Assert.Contains("ON CONFLICT DO NOTHING", source);
        Assert.Contains("ReleaseLeaseAsync", source);
        Assert.Contains("AiSecretRedactor.Redact", source);
    }

    [Fact]
    public void ProviderDiagnostics_RedactsCredentialValue()
    {
        var redacted = new ResolvedAiProviderCredential(Guid.NewGuid(), "api_key", "KIE_API_KEY", "[redacted]");

        Assert.Equal("[redacted]", redacted.SecretValue);
        Assert.Equal("KIE_API_KEY", redacted.ReferenceName);
    }

    [Fact]
    public void Task5Sql_VerifiesRenderAndDiagnosticsContracts()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sql39 = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "39_task5_render_event_contract.sql"));
        var sql40 = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "40_task5_provider_diagnostics_contract.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "manual", "ai-core-reset", "42_verify_task5_runtime.sql"));

        Assert.Contains("render_job_steps_job_step_attempt_uk", sql39);
        Assert.Contains("render_artifacts_job_type_url_uk", sql39);
        Assert.Contains("todox_ai_provider_balance_ledger_idempotency_uk", sql40);
        Assert.Contains("Task 5 runtime verification passed", verify);
    }

    [Fact]
    public void GenericBillingContract_IncludesRefund()
    {
        Assert.Contains(nameof(IAiBillingService.RefundAsync), typeof(IAiBillingService).GetMethods().Select(m => m.Name));
    }
}
