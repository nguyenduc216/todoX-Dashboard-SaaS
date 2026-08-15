using System.Text.Json;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CoreExecutionLifecycleTests
{
    [Fact]
    public async Task DeferredAdapter_RecordsCorrelationAndPreventsWorkerCompletion()
    {
        var result = CoreExecutionResult.Deferred(
            "legacy-runtime",
            "external-123",
            "test-adapter",
            "Accepted.");
        var completion = new CapturingCompletionService();
        var handler = CreateHandler(result, completion);

        var exception = await Assert.ThrowsAsync<RenderJobDeferredException>(
            () => handler.HandleAsync(CreateJob(), CancellationToken.None));

        Assert.Contains("Accepted", exception.Message);
        Assert.NotNull(completion.Deferred);
        Assert.Equal("legacy-runtime", completion.Deferred!.ExecutionSystem);
        Assert.Equal("external-123", completion.Deferred.ExternalExecutionId);
        Assert.Null(completion.Completed);
    }

    [Fact]
    public async Task SynchronousCompletedAdapter_UsesCompletionServiceAndPreventsGenericCompletion()
    {
        var output = JsonSerializer.SerializeToElement(new
        {
            outputs = new[]
            {
                new { type = "video", url = "https://example.invalid/result.mp4", mime_type = "video/mp4" }
            }
        });
        var completion = new CapturingCompletionService();
        var handler = CreateHandler(CoreExecutionResult.Completed(output, "Finished."), completion);

        await Assert.ThrowsAsync<RenderJobDeferredException>(
            () => handler.HandleAsync(CreateJob(), CancellationToken.None));

        Assert.NotNull(completion.Completed);
        Assert.Equal(JsonValueKind.Object, completion.Completed!.Output.ValueKind);
        Assert.Equal("Finished.", completion.Completed.Message);
        Assert.Null(completion.Deferred);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ProgressValidation_RejectsOutOfRangeValues(int progress)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CoreJobCompletionService.ValidateProgress(new CoreJobProgressRequest(
                Guid.NewGuid(),
                "rendering",
                progress)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void ProgressValidation_AcceptsCanonicalRange(int progress)
    {
        CoreJobCompletionService.ValidateProgress(new CoreJobProgressRequest(
            Guid.NewGuid(),
            "rendering",
            progress));
    }

    [Theory]
    [InlineData(RenderPointStatuses.Pending, true)]
    [InlineData(RenderPointStatuses.Charged, false)]
    [InlineData(RenderPointStatuses.Cancelled, false)]
    [InlineData(RenderPointStatuses.Refunded, false)]
    [InlineData(RenderPointStatuses.NotRequired, false)]
    public void BusinessRetry_ReleasesOnlyPendingSourceReservation(string pointStatus, bool expected)
    {
        Assert.Equal(expected, CoreJobApplicationService.ShouldReleaseSourceOnBusinessRetry(pointStatus));
    }

    private static CoreServiceJobHandler CreateHandler(
        CoreExecutionResult result,
        CapturingCompletionService completion)
        => new(
            new CoreExecutionRouter(new[] { new FixedAdapter(result) }),
            completion);

    private static RenderJobDto CreateJob()
    {
        var envelope = new CoreServiceJobEnvelope
        {
            ServiceId = Guid.NewGuid(),
            ServiceCode = "TEST_SERVICE",
            Channel = CoreChannelCodes.System,
            Payload = JsonSerializer.SerializeToElement(new { input = true })
        };
        return new RenderJobDto
        {
            Id = Guid.NewGuid(),
            JobType = RenderJobTypes.CoreService,
            InputJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    private sealed class FixedAdapter : ICoreJobExecutionAdapter
    {
        private readonly CoreExecutionResult _result;

        public FixedAdapter(CoreExecutionResult result)
        {
            _result = result;
        }

        public string ServiceCode => "TEST_SERVICE";

        public Task<CoreExecutionResult> DispatchAsync(
            CoreJobDispatchContext context,
            CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class CapturingCompletionService : ICoreJobCompletionService
    {
        public CoreExecutionCorrelation? Deferred { get; private set; }
        public CoreJobCompleteRequest? Completed { get; private set; }

        public Task MarkDeferredAsync(
            CoreExecutionAuthority authority,
            Guid jobId,
            CoreExecutionCorrelation correlation,
            string? message = null,
            CancellationToken ct = default)
        {
            Deferred = correlation;
            return Task.CompletedTask;
        }

        public Task MarkProgressAsync(
            CoreExecutionAuthority authority,
            CoreJobProgressRequest request,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<CoreBillingCompletion> CompleteAsync(
            CoreExecutionAuthority authority,
            CoreJobCompleteRequest request,
            CancellationToken ct = default)
        {
            Completed = request;
            return Task.FromResult(new CoreBillingCompletion(
                true,
                RenderPointStatuses.Charged,
                10,
                null));
        }

        public Task<CoreBillingCompletion> FailAsync(
            CoreExecutionAuthority authority,
            CoreJobFailRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new CoreBillingCompletion(
                true,
                RenderPointStatuses.Cancelled,
                0,
                null));
    }
}
