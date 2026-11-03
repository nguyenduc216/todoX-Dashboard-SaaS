using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SceneImageEnqueueReconciliationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task DisabledWorkerNeverCreatesScopeOrTouchesSchema(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null) values["ImageEnqueueReconciliation:Enabled"] = enabled;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var scopes = new RejectingScopeFactory();
        using var worker = new SceneImageEnqueueReconciliationWorker(scopes, config,
            NullLogger<SceneImageEnqueueReconciliationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, scopes.Calls);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("0", "60", "30", "20")]
    [InlineData("30", "-1", "30", "20")]
    [InlineData("30", "60", "0", "20")]
    [InlineData("30", "60", "30", "101")]
    public async Task EnabledWithInvalidSchedulingConfigurationStopsBeforeOpeningScope(
        string? interval, string? lease, string? backoff, string? batch)
    {
        var values = new Dictionary<string, string?>
        {
            ["ImageEnqueueReconciliation:Enabled"] = "true",
            ["ImageEnqueueReconciliation:IntervalSeconds"] = interval,
            ["ImageEnqueueReconciliation:LeaseSeconds"] = lease,
            ["ImageEnqueueReconciliation:BackoffSeconds"] = backoff,
            ["ImageEnqueueReconciliation:BatchSize"] = batch
        };
        var scopes = new RejectingScopeFactory();
        using var worker = new SceneImageEnqueueReconciliationWorker(scopes,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<SceneImageEnqueueReconciliationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, scopes.Calls);
    }

    [Fact]
    public async Task AlreadyCancelledWorkerDoesNotOpenScope()
    {
        var values = new Dictionary<string, string?>
        {
            ["ImageEnqueueReconciliation:Enabled"] = "true",
            ["ImageEnqueueReconciliation:IntervalSeconds"] = "30",
            ["ImageEnqueueReconciliation:LeaseSeconds"] = "60",
            ["ImageEnqueueReconciliation:BackoffSeconds"] = "30",
            ["ImageEnqueueReconciliation:BatchSize"] = "20"
        };
        var scopes = new RejectingScopeFactory();
        using var worker = new SceneImageEnqueueReconciliationWorker(scopes,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<SceneImageEnqueueReconciliationWorker>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await worker.StartAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, scopes.Calls);
    }

    [Theory]
    [InlineData(0, false, false, null)]
    [InlineData(0, true, true, "DEADLINE_EXPIRED")]
    [InlineData(1, false, true, "POSITIVE_HANDOFF_UNAPPROVED")]
    [InlineData(2, false, true, "DUPLICATE_JOB_IDENTITY")]
    public void EvidenceNeverAuthorizesAutomaticEnqueueOrPositiveHandoff(int jobs, bool expired, bool manual, string? reason)
    {
        var decision = ImageEnqueueReconciliationPolicy.Decide(jobs, expired);
        Assert.Equal(manual, decision.Manual);
        Assert.Equal(reason, decision.Reason);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void InvalidJobIdentityIsAmbiguousAndNeverHandsOff(int jobs, int invalid)
    {
        var decision = ImageEnqueueReconciliationPolicy.Decide(jobs, false, invalid);
        Assert.True(decision.Manual);
        Assert.Equal("AMBIGUOUS_JOB_IDENTITY", decision.Reason);
    }

    private sealed class RejectingScopeFactory : IServiceScopeFactory
    {
        public int Calls;
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref Calls);
            throw new Xunit.Sdk.XunitException("Disabled worker attempted to access scoped services");
        }
    }
}