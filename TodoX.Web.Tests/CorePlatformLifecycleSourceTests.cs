using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CorePlatformLifecycleSourceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(RenderJobStatuses.Draft, true)]
    [InlineData(RenderJobStatuses.Queued, true)]
    [InlineData(RenderJobStatuses.Preparing, true)]
    [InlineData(RenderJobStatuses.Rendering, true)]
    [InlineData(RenderJobStatuses.PostProcessing, true)]
    [InlineData(RenderJobStatuses.PendingReconciliation, true)]
    [InlineData(RenderJobStatuses.Completed, false)]
    [InlineData(RenderJobStatuses.Failed, false)]
    [InlineData(RenderJobStatuses.Cancelled, false)]
    public void CancelStatus_GuardsTerminalJobs(string status, bool expected)
    {
        Assert.Equal(expected, CoreJobApplicationService.CanCancelStatus(status));
    }

    [Fact]
    public void CreateJob_UsesAdvisoryLockBeforeDuplicateLookupAndInsert()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobApplicationService.cs");
        var lockIndex = source.IndexOf("pg_advisory_xact_lock", StringComparison.Ordinal);
        var duplicateIndex = source.IndexOf("logical_request_id = @logicalRequestId", StringComparison.Ordinal);
        var insertIndex = source.IndexOf("INSERT INTO render.render_jobs", StringComparison.Ordinal);

        Assert.True(lockIndex >= 0);
        Assert.True(duplicateIndex > lockIndex);
        Assert.True(insertIndex > duplicateIndex);
        Assert.Contains("if (existing is not null)", source, StringComparison.Ordinal);
        Assert.Contains("return Map(existing);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_PreservesCorrelationAndBuildsNewLogicalIdentity()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobApplicationService.cs");

        Assert.Contains("retry_of_job_id", source, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"retry:{source.Id:N}:{retryKey}\"", source, StringComparison.Ordinal);
        Assert.Contains("source.Id,", source, StringComparison.Ordinal);
        Assert.Contains("ShouldReleaseSourceOnBusinessRetry(billingState?.PointStatus)", source, StringComparison.Ordinal);
        Assert.Contains("=> pointStatus == RenderPointStatuses.Pending;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BillingFacade_ExposesRequiredLifecycleAndIdempotentStateGuards()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreBillingService.cs");

        Assert.Contains("EstimateAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ReserveAsync(", source, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync(", source, StringComparison.Ordinal);
        Assert.Contains("RefundOrReleaseAsync(", source, StringComparison.Ordinal);
        Assert.Contains("GetBillingStateAsync(", source, StringComparison.Ordinal);
        Assert.Contains("job.PointStatus == RenderPointStatuses.Charged", source, StringComparison.Ordinal);
        Assert.Contains("job.PointStatus == RenderPointStatuses.Pending", source, StringComparison.Ordinal);
        Assert.Contains("RenderPointStatuses.Cancelled or RenderPointStatuses.Refunded", source, StringComparison.Ordinal);
        Assert.Contains("point_status='insufficient'", source, StringComparison.Ordinal);
        Assert.Contains("status='failed'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BillingReservation_BlocksExecutionUntilReservationSucceeds()
    {
        var jobs = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobApplicationService.cs");
        var billing = ReadSource("TodoX.Web", "Services", "Platform", "CoreBillingService.cs");

        Assert.Contains("'draft', 'billing'", jobs, StringComparison.Ordinal);
        Assert.DoesNotContain("'queued', 0, @priority", jobs, StringComparison.Ordinal);
        Assert.Contains("SET status='queued'", billing, StringComparison.Ordinal);
        Assert.Contains("point_status='pending'", billing, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiV1_UsesCallerResolverForCatalogAndJobRoutes()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreApiEndpointExtensions.cs");

        Assert.Contains("MapGet(\"/services\"", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/services/{serviceCode}\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/jobs\"", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/jobs\"", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/jobs/{jobId:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/jobs/{jobId:guid}/cancel\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/jobs/{jobId:guid}/retry\"", source, StringComparison.Ordinal);
        Assert.Equal(7, CountOccurrences(source, "callers.ResolveAsync(httpRequest, ct)"));
        Assert.Contains("Status402PaymentRequired", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreApi_IsDisabledByDefaultAndConditionallyMapped()
    {
        var settings = ReadSource("TodoX.Web", "appsettings.json");
        var program = ReadSource("TodoX.Web", "Program.cs");

        Assert.Contains("\"CoreApi\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", settings, StringComparison.Ordinal);
        Assert.Contains("GetValue(\"CoreApi:Enabled\", false)", program, StringComparison.Ordinal);
        Assert.Contains("app.MapTodoXCoreApiV1();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogProjection_ExposesSharedFormSchemaAndServerPrices()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreServiceCatalogService.cs");

        Assert.Contains("default_options->'form_schema'", source, StringComparison.Ordinal);
        Assert.Contains("catalog.service_sell_prices", source, StringComparison.Ordinal);
        Assert.Contains("p.is_active = true", source, StringComparison.Ordinal);
        Assert.Contains("PricesJson", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualSchemaScript_AddsOnlyCurrentStepAndIsNotAutomatic()
    {
        var source = ReadSource("database", "manual", "core-api-platform", "03_add_core_job_current_step.sql");

        Assert.Contains("ADD COLUMN IF NOT EXISTS current_step", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreHandler_UsesExplicitCompletionAndDefersGenericWorkerForBothOutcomes()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreServiceJobHandler.cs");

        Assert.Contains("case CoreExecutionDisposition.Completed:", source, StringComparison.Ordinal);
        Assert.Contains("_completion.CompleteAsync(", source, StringComparison.Ordinal);
        Assert.Contains("case CoreExecutionDisposition.Deferred:", source, StringComparison.Ordinal);
        Assert.Contains("_completion.MarkDeferredAsync(", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "throw new RenderJobDeferredException("));
    }

    [Fact]
    public void DeferredExecution_RemainsNonTerminalStoresCorrelationAndReleasesWorkerLock()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobCompletionService.cs");

        Assert.Contains("SET status='rendering'", source, StringComparison.Ordinal);
        Assert.Contains("current_step='external_execution'", source, StringComparison.Ordinal);
        Assert.Contains("progress_percent=GREATEST(progress_percent, 1)", source, StringComparison.Ordinal);
        Assert.Contains("options=jsonb_set(", source, StringComparison.Ordinal);
        Assert.Contains("external_execution_id", source, StringComparison.Ordinal);
        Assert.Contains("lock_owner=NULL", source, StringComparison.Ordinal);
        Assert.Contains("lock_until=NULL", source, StringComparison.Ordinal);
        Assert.Contains("status NOT IN ('completed','failed','cancelled')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("status='queued'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Completion_IsAtomicWritesOutputProgressAndEventsWithIdempotentChargeGuard()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreBillingService.cs");

        Assert.Contains("using var tx = conn.BeginTransaction();", source, StringComparison.Ordinal);
        Assert.Contains("if (job.Status == RenderJobStatuses.Completed)", source, StringComparison.Ordinal);
        Assert.Contains("if (job.PointStatus == RenderPointStatuses.Charged)", source, StringComparison.Ordinal);
        Assert.Contains("output_json=CASE WHEN @outputJson IS NULL THEN output_json ELSE CAST(@outputJson AS jsonb) END", source, StringComparison.Ordinal);
        Assert.Contains("progress_percent=100", source, StringComparison.Ordinal);
        Assert.Contains("\"CORE_JOB_COMPLETED\"", source, StringComparison.Ordinal);
        Assert.Contains("\"CORE_BILLING_CHARGED\"", source, StringComparison.Ordinal);
        Assert.Contains("tx.Commit();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_UsesExplicitPolicyAndIsIdempotent()
    {
        var contracts = ReadSource("TodoX.Web", "Services", "Platform", "CorePlatformContracts.cs");
        var billing = ReadSource("TodoX.Web", "Services", "Platform", "CoreBillingService.cs");

        Assert.Contains("ReleaseReservation", contracts, StringComparison.Ordinal);
        Assert.Contains("KeepCharge", contracts, StringComparison.Ordinal);
        Assert.Contains("RefundCharge", contracts, StringComparison.Ordinal);
        Assert.Contains("if (job.Status == RenderJobStatuses.Failed)", billing, StringComparison.Ordinal);
        Assert.Contains("request.BillingPolicy == CoreFailureBillingPolicy.KeepCharge", billing, StringComparison.Ordinal);
        Assert.Contains("request.BillingPolicy == CoreFailureBillingPolicy.RefundCharge", billing, StringComparison.Ordinal);
        Assert.Contains("job.PointStatus == RenderPointStatuses.Charged", billing, StringComparison.Ordinal);
        Assert.Contains("nextPointStatus = RenderPointStatuses.Refunded;", billing, StringComparison.Ordinal);
        Assert.Contains("error_code=@errorCode", billing, StringComparison.Ordinal);
        Assert.Contains("error_message=@errorMessage", billing, StringComparison.Ordinal);
        Assert.Contains("\"CORE_JOB_FAILED\"", billing, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_ValidatesRangeAndDoesNotOverrideTerminalJobs()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobCompletionService.cs");

        Assert.Contains("request.ProgressPercent is < 0 or > 100", source, StringComparison.Ordinal);
        Assert.Contains("status NOT IN ('completed','failed','cancelled')", source, StringComparison.Ordinal);
        Assert.Contains("if (changed > 0)", source, StringComparison.Ordinal);
        Assert.Contains("\"CORE_JOB_PROGRESS\"", source, StringComparison.Ordinal);
        Assert.Contains("WHEN lower(@step)='post_processing' THEN 'post_processing'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void JobProjection_ReturnsStoredExecutionCorrelation()
    {
        var contracts = ReadSource("TodoX.Web", "Services", "Platform", "CorePlatformContracts.cs");
        var jobs = ReadSource("TodoX.Web", "Services", "Platform", "CoreJobApplicationService.cs");

        Assert.Contains("CoreExecutionCorrelation? Execution = null", contracts, StringComparison.Ordinal);
        Assert.Contains("COALESCE(r.options, '{}'::jsonb)::text AS OptionsJson", jobs, StringComparison.Ordinal);
        Assert.Contains("ParseExecutionCorrelation(row.OptionsJson)", jobs, StringComparison.Ordinal);
        Assert.Contains("\"external_execution_id\"", jobs, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalRetry_ReusesSameCanonicalJob()
    {
        var worker = ReadSource("TodoX.Web", "Services", "Render", "RenderJobWorker.cs");
        var jobs = ReadSource("TodoX.Web", "Services", "Render", "RenderJobService.cs");

        Assert.Contains("jobs.ScheduleRetryAsync(job.Id", worker, StringComparison.Ordinal);
        Assert.Contains("WHERE id=@jobId", jobs, StringComparison.Ordinal);
        Assert.Contains("SET status='queued'", jobs, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO render.render_jobs", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicApi_DoesNotExposeCompletionOrFailureEndpoints()
    {
        var source = ReadSource("TodoX.Web", "Services", "Platform", "CoreApiEndpointExtensions.cs");

        Assert.DoesNotContain("/complete", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/fail", source, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadSource(params string[] path)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find TodoX repository root.");
    }
}
