using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseWorkerClaimRegressionTests
{
    [Fact]
    public void RenderingImageWithoutProviderTaskIsWaitingForWorkerAndBecomesStuckAfterThreshold()
    {
        var image = new TimelapseStageImage
        {
            Status = TimelapseOperationStatuses.Rendering,
            StartedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        Assert.Equal(TimelapseImageExecutionPhase.QueuedForWorker, TimelapseImageExecutionPhase.Resolve(image));
        Assert.True(TimelapseImageExecutionPhase.IsWaitingForWorker(image));
        Assert.True(TimelapseImageExecutionPhase.IsStuckWaitingForWorker(image, DateTime.UtcNow, TimeSpan.FromMinutes(2)));

        image.ProviderTaskId = "provider-task";
        Assert.Equal(TimelapseImageExecutionPhase.Submitted, TimelapseImageExecutionPhase.Resolve(image));
        Assert.False(TimelapseImageExecutionPhase.IsWaitingForWorker(image));
    }

    [Fact]
    public void ImageClaimPreservesDependencyAndExpiredClaimRulesForRestartRecovery()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var claim = Between(source, "public async Task<TimelapseImageWorkItem?> ClaimImageAsync", "public async Task<int> DiagnoseImageClaimsAsync");

        Assert.Contains("s.status='RENDERING'", claim);
        Assert.Contains("v.status='RENDERING'", claim);
        Assert.Contains("d.status='COMPLETED'", claim);
        Assert.Contains("d.result_media_id IS NOT NULL", claim);
        Assert.Contains("NULLIF(d.public_url,'') IS NOT NULL OR NULLIF(d.object_key,'') IS NOT NULL", claim);
        Assert.Contains("COALESCE((v.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) <= now()", claim);
        Assert.Contains("FOR UPDATE OF v SKIP LOCKED", claim);
        Assert.DoesNotContain("FOR UPDATE OF s", claim);
        Assert.DoesNotContain("FOR UPDATE SKIP LOCKED", claim);
        Assert.DoesNotContain("AS ClaimUntil", claim);
        Assert.DoesNotContain("active_attempt=active_attempt+1", claim);
    }

    [Fact]
    public void ImageClaimSeparatesOuterJoinCandidateFromVersionLockForConcurrentWorkers()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var claim = Between(source, "public async Task<TimelapseImageWorkItem?> ClaimImageAsync", "public async Task<int> DiagnoseImageClaimsAsync");
        var candidate = Between(claim, "WITH candidate AS MATERIALIZED", "), locked AS");
        var locked = Between(claim, "), locked AS", "UPDATE timelapse.timelapse_image_stage_versions v");
        var update = Between(claim, "UPDATE timelapse.timelapse_image_stage_versions v", "tx.Commit();");

        Assert.DoesNotContain("FOR UPDATE", candidate);
        Assert.Contains("LEFT JOIN timelapse.timelapse_image_stages d", candidate);
        Assert.Contains("FROM timelapse.timelapse_image_stage_versions v", locked);
        Assert.Contains("JOIN candidate c", locked);
        Assert.DoesNotContain("LEFT JOIN", locked);
        Assert.Contains("FOR UPDATE OF v SKIP LOCKED", locked);
        Assert.Contains("JOIN locked l", update);
        Assert.Contains("ON l.Id=c.Id", update);
        Assert.Contains("AND l.Attempt=c.Attempt", update);
    }

    [Fact]
    public void ImageClaimDiagnosticAndStuckDiagnosticsExposeReasonedEligibility()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("Task<TimelapseImageClaimDiagnostic?> DiagnoseImageClaimAsync", source);
        Assert.Contains("Task<int> DiagnoseImageClaimsAsync", source);
        Assert.Contains("StageId", source);
        Assert.Contains("ParentTenantId", source);
        Assert.Contains("ProviderTaskId", source);
        Assert.Contains("Reason", source);
        Assert.Contains("HasDependencyMedia", source);
        Assert.Contains("HasDependencyReference", source);
        Assert.Contains("ClaimExpired", source);
        Assert.Contains("TenantMatches", source);
        Assert.Contains("Eligible", source);
        Assert.Contains("TIMELAPSE_IMAGE_STUCK_BEFORE_CLAIM", source);
        Assert.Contains("TIMELAPSE_IMAGE_CLAIM_SKIPPED", source);
    }

    [Fact]
    public void ClaimRuleEvaluation_CoversTenantMismatchAndExpiredClaimRecovery()
    {
        var workerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var unclaimed = TimelapseWorkerRepository.EvaluateImageClaim(
            workerTenantId,
            workerTenantId,
            "RENDERING",
            "RENDERING",
            "GENERATING_IMAGES",
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.True(unclaimed.TenantMatches);
        Assert.True(unclaimed.Eligible);
        Assert.Equal("ELIGIBLE", unclaimed.Reason);

        var eligible = TimelapseWorkerRepository.EvaluateImageClaim(
            workerTenantId,
            workerTenantId,
            "RENDERING",
            "RENDERING",
            "GENERATING_IMAGES",
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(eligible.TenantMatches);
        Assert.True(eligible.Eligible);
        Assert.Equal("ELIGIBLE", eligible.Reason);

        var mismatch = TimelapseWorkerRepository.EvaluateImageClaim(
            workerTenantId,
            otherTenantId,
            "RENDERING",
            "RENDERING",
            "GENERATING_IMAGES",
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(mismatch.TenantMatches);
        Assert.False(mismatch.Eligible);
        Assert.Equal("TENANT_MISMATCH", mismatch.Reason);

        var claimBlocked = TimelapseWorkerRepository.EvaluateImageClaim(
            workerTenantId,
            workerTenantId,
            "RENDERING",
            "RENDERING",
            "GENERATING_IMAGES",
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(claimBlocked.Eligible);
        Assert.Equal("CLAIM_NOT_EXPIRED", claimBlocked.Reason);
    }

    [Fact]
    public void DiagnosticClaimUntilUsesNullableTimestampWithoutInfinitySentinel()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("(v.request_json->'worker_claim'->>'until')::timestamptz AS ClaimUntil", source);
        Assert.DoesNotContain("COALESCE((v.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) AS ClaimUntil", source);
        Assert.Contains("ClaimExpired = row.ClaimUntil is null || row.ClaimUntil <= DateTimeOffset.UtcNow", source);
    }

    [Fact]
    public void WorkerStartupAndClaimLoopEmitThrottledOperationalDiagnostics()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseProviderWorkers.cs");

        Assert.Contains("TIMELAPSE_IMAGE_WORKER_EXECUTE_START", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_CONFIG", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_TENANT_READY", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_LOOP_ENTER", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_CLAIM_BEGIN", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_CLAIM_RESULT", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_PROCESS_BEGIN", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_FATAL", source);
        Assert.Contains("TIMELAPSE_WORKER_LOOP_ENTER", source);
        Assert.Contains("TIMELAPSE_WORKER_LANE_ENTER", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_RESULT", source);
        Assert.Contains("TIMELAPSE_WORKER_FATAL", source);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_TENANT", source);
        Assert.Contains("TIMELAPSE_WORKER_DISABLED", source);
        Assert.Contains("TIMELAPSE_WORKER_PARALLELISM_NORMALIZED", source);
        Assert.Contains("TIMELAPSE_WORKER_HEARTBEAT", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_BEGIN", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_RETURNED", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIM_NULL", source);
        Assert.Contains("TIMELAPSE_WORKER_CLAIMED", source);
        Assert.Contains("TIMELAPSE_WORKER_ERROR", source);
        Assert.Contains("ShouldLogNullClaim", source);
        Assert.Contains("Math.Max(1, parallelism)", source);
        Assert.Contains("TimelapseWorkerIterationResult", source);
        Assert.Contains("pollDelayMs={PollDelayMs}", source);
        Assert.Contains("providerCode={ProviderCode}", source);
        Assert.Contains("imageModelName={ImageModelName}", source);
    }

    [Fact]
    public void TimelapseImageWorkerIsSelfRecoveringBeforeClaimLoopStarts()
    {
        var source = ReadRepoFile("Services", "Timelapse", "TimelapseProviderWorkers.cs");
        var execute = Between(source, "protected override async Task ExecuteAsync", "public sealed class TimelapseVideoWorker");

        Assert.Contains("TIMELAPSE_IMAGE_WORKER_EXECUTE_START", execute);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_CONFIG", execute);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_TENANT_READY", execute);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_LOOP_ENTER", execute);
        Assert.Contains("TIMELAPSE_IMAGE_WORKER_FATAL", execute);
        Assert.Contains("while (!stoppingToken.IsCancellationRequested)", execute);
        Assert.Contains("catch (Exception ex)", execute);
        Assert.Contains("await Task.Delay(startupRetryDelay, stoppingToken);", execute);
    }

    [Fact]
    public void ProgramLogsTimelapseHostedServiceRegistrationOnce()
    {
        var source = ReadRepoFile("Program.cs");

        Assert.Contains("TIMELAPSE_HOSTED_SERVICES_CONFIGURED", source);
        Assert.Contains("imageWorker=true", source);
        Assert.Contains("videoWorker=true", source);
        Assert.Contains("finalizerWorker=true", source);
    }

    [Fact]
    public void CustomerUiDistinguishesWorkerWaitFromProviderSubmission()
    {
        var workflow = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var runtime = ReadRepoFile("Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("TimelapseImageExecutionPhase.IsWaitingForWorker(image)", page);
        Assert.Contains("TIMELAPSE_IMAGE_SUBMIT_BEGIN", runtime);
        Assert.Contains("TIMELAPSE_IMAGE_SUBMITTED", runtime);
        Assert.Contains("TIMELAPSE_IMAGE_FAILED", runtime);
        Assert.Contains("TIMELAPSE_RENDER_STARTED", workflow);
    }

    [Fact]
    public void ImageModelChainUses79AiPrimaryAndFallbackWithoutWrapping()
    {
        var options = ReadRepoFile("Services", "Timelapse", "TimelapseProviderWorkerOptions.cs");
        var runtime = ReadRepoFile("Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var config = ReadRepoFile("appsettings.json");

        Assert.Contains("NanoBanana2ModelName = \"google_image_gen_banana_2\"", options);
        Assert.Contains("Seedream50ModelName = \"seedream_5_0\"", options);
        Assert.Contains("\"ImageModelName\": \"google_image_gen_banana_2\"", config);
        Assert.Contains("\"ImageModelsWithReference\"", config);
        Assert.Contains("\"ImageModelsWithoutReference\"", config);
        Assert.Contains("GetNext(currentModel, HasImageReference(item))", runtime);
        Assert.Contains("return index + 1 < models.Length ? models[index + 1] : null;", ReadRepoFile("Services", "Timelapse", "TimelapseImageModelSelector.cs"));
        Assert.DoesNotContain("next ?? _imageModelSelector.Select(hasReference)[0]", runtime);
    }

    [Fact]
    public void ImageFallbackPreservesAttemptAndPersistsNextModel()
    {
        var repository = ReadRepoFile("Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var runtime = ReadRepoFile("Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("SaveImageFallbackAsync", repository);
        Assert.Contains("item.Attempt,", runtime);
        Assert.Contains("TIMELAPSE_IMAGE_MODEL_FALLBACK", runtime);
        Assert.Contains("await _repo.ReleaseImageClaimAsync(item.Id, item.Attempt, ct);", runtime);
        Assert.DoesNotContain("active_attempt=active_attempt+1", runtime);
        Assert.DoesNotContain("CreateImageAttempt", runtime);
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
