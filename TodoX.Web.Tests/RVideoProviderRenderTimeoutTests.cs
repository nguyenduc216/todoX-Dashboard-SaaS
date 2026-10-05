using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoProviderRenderTimeoutTests
{
    [Fact]
    public void ProviderTerminalFailureDoesNotRequireProviderErrorCode()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var failureBlock = source[source.IndexOf("if (status.Status != VideoProviderTaskStatus.Success)", StringComparison.Ordinal)..];

        Assert.Contains("status.ErrorMessage ?? $\"Video provider task failed with status {status.Status}.\"", failureBlock);
        Assert.Contains("status.ErrorCode ?? \"provider_failure\"", failureBlock);
        Assert.Contains("Scene-video provider reported a terminal failure.", failureBlock);
        Assert.Contains("throw new RenderJobTerminalFailureException(failure)", failureBlock);
    }

    [Fact]
    public void ProviderProcessingBeyondHardTimeoutFailsLocallyWithoutAutoResubmit()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var options = ReadRepoFile("Services", "VideoRender", "VideoRenderOptions.cs");
        var processingBlock = source[source.IndexOf("if (status.Status is VideoProviderTaskStatus.Queued or VideoProviderTaskStatus.Processing)", StringComparison.Ordinal)..];

        Assert.Contains("ProviderRenderHardTimeoutMinutes", options);
        Assert.Contains("RVideo:ProviderRenderHardTimeoutMinutes", source);
        Assert.Contains("ProviderRenderHardTimeoutEvaluator.Evaluate(", processingBlock);
        Assert.Contains("\"RVIDEO_PROVIDER_RENDER_TIMEOUT\"", processingBlock);
        Assert.Contains("FailAsync(project.Id, scene, version.Id, \"RVIDEO_PROVIDER_RENDER_TIMEOUT\"", processingBlock);
        Assert.Contains("throw new RenderJobTerminalFailureException(timeoutMessage)", processingBlock);
        Assert.DoesNotContain("SubmitAsync", processingBlock[..processingBlock.IndexOf("await _repo.AddProjectEventAsync(project.Id, \"SCENE_VIDEO_PROVIDER_PROCESSING\"", StringComparison.Ordinal)]);
    }

    [Fact]
    public void ProviderProcessingWithinTimeoutKeepsPollingSameTask()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var processingBlock = source[source.IndexOf("if (status.Status is VideoProviderTaskStatus.Queued or VideoProviderTaskStatus.Processing)", StringComparison.Ordinal)..];

        Assert.Contains("SCENE_VIDEO_PROVIDER_PROCESSING", processingBlock);
        Assert.Contains("MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId", processingBlock);
        Assert.Contains("DeferProviderPollAsync(job, taskId!", processingBlock);
        Assert.Contains("Video task remains pending; the same provider task will be polled later.", processingBlock);
    }

    [Fact]
    public void ProviderHardTimeoutUsesLegacyAttemptTimestampsWhenSubmittedAtIsMissing()
    {
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var inputCreatedAt = now.AddHours(-2);
        var decision = ProviderRenderHardTimeoutEvaluator.Evaluate(
            new SceneVideoVersionDto { Status = "pending_reconciliation", SubmittedAt = null },
            new SceneVideoRenderWorkItemInput { CreatedAtUtc = inputCreatedAt },
            new RenderJobDto { StartedAt = now.AddMinutes(-10).UtcDateTime, CreatedAt = now.AddMinutes(-5).UtcDateTime },
            TimeSpan.FromMinutes(60),
            now);

        Assert.True(decision.IsTimedOut);
        Assert.Equal(inputCreatedAt, decision.StartedAt);
        Assert.Equal(TimeSpan.FromHours(2), decision.Elapsed);
    }

    [Fact]
    public async Task LegacyPendingReconciliationJobIsScheduledAndKeepsOriginalTimeoutClock()
    {
        var jobId = Guid.NewGuid();
        var jobs = new RecordingRenderJobService();
        var submittedAt = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var firstPollAt = submittedAt.AddMinutes(70);

        await SceneVideoReconciliationWorker.SchedulePersistentJobsAsync(
            new[] { jobId }, jobs, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(jobId, Assert.Single(jobs.ScheduledJobIds));
        Assert.False(jobs.EnforceReconciliationLimit);
        Assert.False(jobs.EnforceProviderPollTimeout);

        var version = new SceneVideoVersionDto
        {
            Status = "pending_reconciliation",
            ProviderTaskId = "legacy-task-id",
            ProviderVideoIdBase = "legacy-video-id",
            SubmittedAt = submittedAt
        };
        var input = new SceneVideoRenderWorkItemInput { CreatedAtUtc = submittedAt.AddMinutes(5) };
        var job = new RenderJobDto
        {
            Id = jobId,
            JobType = RenderJobTypes.RenderSceneVideo,
            Status = RenderJobStatuses.PendingReconciliation,
            StartedAt = submittedAt.AddMinutes(10).UtcDateTime,
            CreatedAt = submittedAt.AddMinutes(5).UtcDateTime
        };

        var first = ProviderRenderHardTimeoutEvaluator.Evaluate(version, input, job, TimeSpan.FromMinutes(60), firstPollAt);
        var repeated = ProviderRenderHardTimeoutEvaluator.Evaluate(version, input, job, TimeSpan.FromMinutes(60), firstPollAt.AddMinutes(20));

        Assert.True(first.IsTimedOut);
        Assert.True(repeated.IsTimedOut);
        Assert.Equal(submittedAt, first.StartedAt);
        Assert.Equal(first.StartedAt, repeated.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(90), repeated.Elapsed);
    }

    [Fact]
    public void ManualSceneVideoRetryCreatesNewAttemptWithSelectedModelAndDuration()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("RequestedModelCode = input.ManualOverride ? requestedModel : null", source);
        Assert.Contains("RequestedDurationSeconds = input.ManualOverride ? requestedDuration : null", source);
        Assert.Contains("ManualOverride = input.ManualOverride", source);
        Assert.Contains("var childJob = await _jobs.EnqueueAsync", source);
        Assert.Contains("JobType = RenderJobTypes.RenderSceneVideo", source);
        Assert.Contains("ModelCode = requestedModel", source);
    }

    [Fact]
    public void SceneVideoCompletionRejectsTerminalOrSupersededOldVersions()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var method = ExtractMethodBlock(source, "public async Task CompleteSceneVideoVersionAsync");

        Assert.Contains("IsTerminalUnsuccessfulVersionStatus(version.Status)", method);
        Assert.Contains("RVIDEO_STALE_SCENE_VIDEO_VERSION_TERMINAL", method);
        Assert.Contains("version_number > @versionNumber", method);
        Assert.Contains("is_selected=true", method);
        Assert.Contains("status='completed'", method);
        Assert.Contains("RVIDEO_STALE_SCENE_VIDEO_VERSION_SUPERSEDED", method);
    }

    [Fact]
    public void SceneVideoVersionSqlMapsSubmittedAtForProviderTimeouts()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");

        Assert.Contains("submitted_at AS SubmittedAt", source);
        Assert.Contains("public DateTimeOffset? SubmittedAt", source);
    }

    [Fact]
    public void LegacyRecoveryProductionPathDiscoversSchedulesClaimsAndPollsExistingTask()
    {
        var repository = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");
        var reconciliation = ReadRepoFile("Services", "VideoRender", "SceneVideoReconciliationWorker.cs");
        var jobs = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var worker = ReadRepoFile("Services", "Render", "SceneVideoJobWorker.cs");
        var handler = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("v.status IN ('submitted', 'processing', 'pending_reconciliation', 'rendering')", repository);
        Assert.Contains("v.provider_task_id IS NOT NULL", repository);
        Assert.Contains("v.provider_video_id_base IS NOT NULL", repository);
        Assert.Contains("SchedulePersistentJobsAsync(", reconciliation);
        Assert.Contains("SchedulePersistentProviderPollAsync(", reconciliation);
        Assert.Contains("'{\"providerPoll\": true}'::jsonb", jobs);
        Assert.Contains("status='queued'", jobs);
        Assert.Contains("COALESCE(input_json->>'providerPoll', 'false') = 'true'", jobs);
        Assert.Contains("ClaimNextByJobTypeAsync(workerKey, lockFor, new[] { RenderJobTypes.RenderSceneVideo }", worker);
        Assert.Contains("var status = await adapter.PollAsync", handler);
        Assert.Contains("ProviderRenderHardTimeoutEvaluator.Evaluate(", handler);
        Assert.True(handler.IndexOf("var status = await adapter.PollAsync", StringComparison.Ordinal)
                    < handler.IndexOf("ProviderRenderHardTimeoutEvaluator.Evaluate(", StringComparison.Ordinal));
    }

    [Fact]
    public void FailedSceneUiKeepsOverridesVisibleAndPassesThemToManualRerender()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("Label=\"Model video\"", page);
        Assert.Contains("Label=\"Thời lượng\"", page);
        Assert.Contains("Render lại video", page);
        Assert.Contains("ResolveVideoSceneStatus(scene), VideoSceneStatuses.Failed", page);
        Assert.Contains("input.ManualOverride = true", page);
        Assert.Contains("input.RequestedModelCode = selectedOverride.Option.ModelCode", page);
        Assert.Contains("input.RequestedDurationSeconds = overrideDuration.Value", page);
        Assert.Contains("EnqueueForSceneIfNoneActiveAsync", page);
    }

    [Fact]
    public void TerminalTimeoutEscapesPersistenceReconciliationCatch()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("ex is not OperationCanceledException and not RenderJobTerminalFailureException", source);
        Assert.Contains("throw new RenderJobTerminalFailureException(timeoutMessage)", source);
    }

    [Fact]
    public void FinalMergeStillRequiresEverySceneVideoReady()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoProjectFinalizationService.cs");

        Assert.Contains("var missing = readiness.Where(x => !x.VideoReady || !x.MuxReady).ToList();", source);
        Assert.Contains("PROJECT_FINAL_MERGE_NOT_READY", source);
        Assert.Contains("return NotEnqueued(\"not_ready\", logicalRequestId);", source);
    }

    [Fact]
    public void SuccessfulSceneVideoFlowStillUsesCompletionService()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("VideoProviderTaskStatus.Success", source);
        Assert.Contains("CompleteProviderVideoAsync", source);
        Assert.Contains("RVIDEO_VIDEO_COMPLETED", source);
        Assert.Contains("SCENE_VIDEO_READY", ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs"));
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", Path.Combine(parts)));

    private static string ExtractMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {signature}.");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"Could not find body for {signature}.");
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}') depth--;
            if (depth == 0) return source[start..(i + 1)];
        }

        throw new InvalidOperationException($"Could not extract {signature}.");
    }

    private static string RepoRoot
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class RecordingRenderJobService : IRenderJobService
    {
        public List<Guid> ScheduledJobIds { get; } = [];
        public bool EnforceReconciliationLimit { get; private set; }
        public bool EnforceProviderPollTimeout { get; private set; }

        public Task<bool> ScheduleProviderPollAsync(Guid jobId, TimeSpan delay, string reasonCode, string reasonMessage,
            CancellationToken ct = default, bool enforceReconciliationLimit = true, bool enforceProviderPollTimeout = false)
        {
            ScheduledJobIds.Add(jobId);
            EnforceReconciliationLimit = enforceReconciliationLimit;
            EnforceProviderPollTimeout = enforceProviderPollTimeout;
            return Task.FromResult(true);
        }

        public Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForProjectIfNoneActiveAsync(RenderJobCreateModel model, long projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForLogCodeIfNoneActiveAsync(RenderJobCreateModel model, string logCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForSceneIfNoneActiveAsync(RenderJobCreateModel model, long sceneId, string? logicalRequestId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> GetByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RenderJobDto>> ListByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RenderJobEventDto>> GetEventsByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> GetProviderReconciliationAttemptCountAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> ClaimNextByJobTypeAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> jobTypes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RenderJobDto?> ClaimNextExcludingJobTypesAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> excludedJobTypes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SchedulePersistentProviderPollAsync(Guid jobId, TimeSpan delay, string reasonCode, string reasonMessage, CancellationToken ct = default)
        {
            ScheduledJobIds.Add(jobId);
            return Task.FromResult(true);
        }
        public Task SetProviderIdentifiersAsync(Guid jobId, string? providerTaskId, string? providerVideoIdBase, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> MarkRecoveredCompletedAsync(Guid jobId, long projectId, long sceneId, Guid sceneVideoVersionId, string logicalRequestId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertSnapshotAsync(Guid jobId, object projectSnapshot, object sceneSnapshots, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
