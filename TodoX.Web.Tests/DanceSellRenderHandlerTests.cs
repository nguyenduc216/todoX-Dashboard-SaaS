using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellRenderHandlerTests
{
    [Fact]
    public async Task CompleteAsync_LogsCustomerIdFromDanceJob()
    {
        var customerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Rendering, customerId: customerId));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        await service.CompleteAsync(new DanceSellCompletionRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "success",
            ResponseJson = """{"ok":true}""",
            ResultVideoUrl = "https://cdn.example/result.mp4",
            ResultUrlCount = 1,
            Source = "poll"
        });

        Assert.Equal(DanceSellCompletionService.ToBigIntCustomerId(customerId), providers.LastUsage?.CustomerId);
    }

    [Fact]
    public async Task CompleteAsync_DuplicateCallbackDoesNotDuplicateRenderEventOrUsage()
    {
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Completed));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        var result = await service.CompleteAsync(new DanceSellCompletionRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "success",
            ResponseJson = """{"ok":true}""",
            ResultVideoUrl = "https://cdn.example/result.mp4",
            ResultUrlCount = 1,
            Source = "callback"
        });

        Assert.True(result.Found);
        Assert.False(result.CompletedNow);
        Assert.Equal(0, renderJobs.MarkStatusCallCount);
        Assert.Equal(0, renderJobs.AddEventCallCount);
        Assert.Equal(0, providers.LogUsageCallCount);
    }

    [Fact]
    public async Task CompleteAsync_PollAfterCallbackDoesNotDuplicateCompletion()
    {
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Completed));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        var result = await service.CompleteAsync(new DanceSellCompletionRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "success",
            ResponseJson = """{"ok":true}""",
            ResultVideoUrl = "https://cdn.example/result.mp4",
            ResultUrlCount = 1,
            Source = "poll"
        });

        Assert.True(result.Found);
        Assert.False(result.CompletedNow);
        Assert.Equal(0, repo.UpdateCompletedCallCount);
        Assert.Equal(0, renderJobs.MarkStatusCallCount);
        Assert.Equal(0, providers.LogUsageCallCount);
    }

    [Fact]
    public async Task CompleteAsync_FirstTerminalUpdateUpdatesDanceJobRenderJobEventAndUsageOnce()
    {
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Rendering));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        var result = await service.CompleteAsync(new DanceSellCompletionRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "success",
            ResponseJson = """{"ok":true}""",
            ResultVideoUrl = "https://cdn.example/result.mp4",
            ResultUrlCount = 1,
            Source = "callback"
        });

        Assert.True(result.CompletedNow);
        Assert.Equal(1, repo.UpdateCompletedCallCount);
        Assert.Equal(DanceSellJobStatuses.Completed, repo.Job.Status);
        Assert.Equal(1, renderJobs.MarkStatusCallCount);
        Assert.Equal(1, renderJobs.AddEventCallCount);
        Assert.Equal(1, providers.LogUsageCallCount);
    }

    [Fact]
    public async Task FailAsync_FirstTerminalUpdateUpdatesDanceJobRenderJobEventAndUsageOnce()
    {
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Rendering));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        var result = await service.FailAsync(new DanceSellFailureRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "fail",
            ResponseJson = """{"failCode":"bad_input"}""",
            ErrorCode = "bad_input",
            ErrorMessage = "Bad input.",
            Source = "callback"
        });

        Assert.True(result.FailedNow);
        Assert.Equal(1, repo.UpdateFailedCallCount);
        Assert.Equal(DanceSellJobStatuses.Failed, repo.Job.Status);
        Assert.Equal(1, renderJobs.MarkStatusCallCount);
        Assert.Equal(1, renderJobs.AddEventCallCount);
        Assert.Equal(1, providers.LogUsageCallCount);
        Assert.Equal("failed", providers.LastUsage?.Status);
    }

    [Fact]
    public async Task FailAsync_DuplicateCallbackDoesNotDuplicateRenderEventOrUsage()
    {
        var repo = new FakeDanceSellRepository(CreateJob(status: DanceSellJobStatuses.Failed));
        var renderJobs = new FakeRenderJobService();
        var providers = new CapturingProviderService();
        var service = new DanceSellCompletionService(repo, renderJobs, providers, new FakeOperationRepository());

        var result = await service.FailAsync(new DanceSellFailureRequest
        {
            DanceJob = repo.Job,
            ProviderTaskId = repo.Job.ProviderTaskId,
            ProviderStatus = "fail",
            ErrorCode = "bad_input",
            ErrorMessage = "Bad input.",
            Source = "callback"
        });

        Assert.True(result.Found);
        Assert.False(result.FailedNow);
        Assert.Equal(0, repo.UpdateFailedCallCount);
        Assert.Equal(0, renderJobs.MarkStatusCallCount);
        Assert.Equal(0, renderJobs.AddEventCallCount);
        Assert.Equal(0, providers.LogUsageCallCount);
    }

    private static DanceSellJobDto CreateJob(string status, Guid? customerId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            RenderJobId = Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            LogicalRequestId = $"dance-{Guid.NewGuid():N}",
            Status = status,
            Prompt = "Dance.",
            CharacterImageUrl = "https://cdn.example/character.png",
            MotionVideoUrl = "https://cdn.example/motion.mp4",
            ProviderTaskId = "task-123"
        };

    private sealed class FakeDanceSellRepository : IDanceSellRepository
    {
        public FakeDanceSellRepository(DanceSellJobDto job)
        {
            Job = job;
        }

        public DanceSellJobDto Job { get; }
        public int UpdateCompletedCallCount { get; private set; }
        public int UpdateFailedCallCount { get; private set; }

        public Task<DanceSellJobDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<DanceSellJobDto?>(Job.Id == id ? Job : null);

        public Task<DanceSellJobDto?> GetByProviderTaskIdAsync(string providerTaskId, CancellationToken ct = default)
            => Task.FromResult<DanceSellJobDto?>(Job.ProviderTaskId == providerTaskId ? Job : null);

        public Task<bool> UpdateCompletedAsync(Guid id, string providerStatus, string pollResponseJson, string resultVideoUrl, CancellationToken ct = default)
        {
            UpdateCompletedCallCount++;
            if (Job.Status is DanceSellJobStatuses.Completed or DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)
            {
                return Task.FromResult(false);
            }

            Job.Status = DanceSellJobStatuses.Completed;
            Job.ProviderStatus = providerStatus;
            Job.PollResponseJson = pollResponseJson;
            Job.ResultVideoUrl ??= resultVideoUrl;
            Job.CompletedAt ??= DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<DanceSellJobDto> CreateAsync(DanceSellJobCreateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DanceSellJobDto> CreateDraftAsync(DanceSellDraftCreateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DanceSellJobDto>> ListAsync(Guid? customerId = null, int limit = 20, int offset = 0, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DanceSellJobDto?> GetByRenderJobIdAsync(Guid renderJobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetRenderJobIdAsync(Guid id, Guid renderJobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task QueueForRenderAsync(Guid id, Guid renderJobId, string logicalRequestId, string preparedReferenceUrl, string motionVideoUrl, DanceSellProviderRouteDto motionRoute, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateCharacterAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateProductAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateDirectReferenceAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateMotionUploadAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateMotionTikTokAsync(Guid id, string sourceUrl, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateReferenceStatusAsync(Guid id, string status, string? error = null, Guid? mediaId = null, string? objectKey = null, string? publicUrl = null, DateTime? approvedAt = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DanceSellReferenceVersionDto>> ListReferenceVersionsAsync(Guid danceSellJobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DanceSellReferenceVersionDto?> GetReferenceVersionAsync(Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DanceSellReferenceVersionDto> CreateReferenceVersionAsync(DanceSellReferenceVersionDto version, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> SelectReferenceVersionAsync(Guid danceSellJobId, Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateSubmittedAsync(Guid id, string requestJson, string providerTaskId, string submitResponseJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdatePollingAsync(Guid id, string providerStatus, string pollResponseJson, int pollCount, DateTime nextPollAtUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateFailedAsync(Guid id, string status, string? providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default)
        {
            UpdateFailedCallCount++;
            if (Job.Status is DanceSellJobStatuses.Completed or DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)
            {
                return Task.FromResult(false);
            }

            Job.Status = status;
            Job.ProviderStatus = providerStatus ?? Job.ProviderStatus;
            Job.ErrorJson = responseJson;
            Job.ErrorCode = errorCode;
            Job.ErrorMessage = errorMessage;
            Job.CompletedAt ??= DateTime.UtcNow;
            return Task.FromResult(true);
        }
        public Task UpdateCallbackAsync(string providerTaskId, string callbackJson, string providerStatus, string? resultVideoUrl, string? errorCode, string? errorMessage, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeOperationRepository : IDanceSellOperationRepository
    {
        public Task<DanceSellProviderOperationDto?> UpsertOperationAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
            => Task.FromResult<DanceSellProviderOperationDto?>(operation);

        public Task<int> GetNextAttemptNoAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default)
            => Task.FromResult(1);

        public Task MarkSubmittedAsync(Guid operationId, string providerTaskId, string responseJson, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkCompletedAsync(Guid operationId, string providerStatus, string responseJson, decimal? creditsConsumed, string? resultUrl, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid operationId, string providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertAssetAsync(AiOperationAssetDto asset, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PagedResult<DanceSellOperationLogItemDto>> SearchLogsAsync(DanceSellOperationLogFilter filter, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<DanceSellOperationLogItemDto>(Array.Empty<DanceSellOperationLogItemDto>(), filter.Page, filter.PageSize, 0));
        public Task<DanceSellOperationLogDetailDto?> GetLogDetailAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<DanceSellOperationLogDetailDto?>(null);
    }

    private sealed class FakeRenderJobService : IRenderJobService
    {
        public int MarkStatusCallCount { get; private set; }
        public int AddEventCallCount { get; private set; }

        public Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
        {
            MarkStatusCallCount++;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default)
        {
            AddEventCallCount++;
            return Task.CompletedTask;
        }

        public Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForProjectIfNoneActiveAsync(RenderJobCreateModel model, long projectId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> GetByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RenderJobDto>> ListByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RenderJobEventDto>> GetEventsByLogCodeAsync(string logCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> ClaimNextByJobTypeAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> jobTypes, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RenderJobDto?> ClaimNextExcludingJobTypesAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> excludedJobTypes, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class CapturingProviderService : IAiProviderService
    {
        public int LogUsageCallCount { get; private set; }
        public AiProviderUsageLog? LastUsage { get; private set; }

        public Task LogUsageAsync(AiProviderUsageLog log, CancellationToken ct = default)
        {
            LogUsageCallCount++;
            LastUsage = log;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiProviderListItemDto>> GetProvidersAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto?> GetProviderAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto?> GetProviderByCodeAsync(string providerCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderDetailDto> UpdateProviderAsync(long id, UpdateAiProviderRequest request, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AiProviderCapabilityDto> UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetDefaultCapabilityAsync(long capabilityId, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetDefaultCapabilitiesAsync(IReadOnlyList<long> capabilityIds, CurrentUserSession user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProviderOptionDto>> GetSelectableProvidersAsync(string capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderOptionDto?> GetDefaultProviderAsync(string capabilityCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProviderOptionDto> ResolveProviderForCapabilityAsync(string capabilityCode, long? providerCapabilityId, bool fromUser, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
