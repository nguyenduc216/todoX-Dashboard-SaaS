using System.Text.Json;
using TodoX.Web.Services.Platform;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CorePlatformContractTests
{
    [Theory]
    [InlineData("dashboard", CoreChannelCodes.Dashboard)]
    [InlineData("ZALO", CoreChannelCodes.Zalo)]
    [InlineData(" telegram ", CoreChannelCodes.Telegram)]
    [InlineData("partner", CoreChannelCodes.Partner)]
    [InlineData("api", CoreChannelCodes.Api)]
    [InlineData(null, CoreChannelCodes.System)]
    public void NormalizeChannel_ReturnsStableCode(string? input, string expected)
    {
        Assert.Equal(expected, CoreChannelCodes.Normalize(input));
    }

    [Fact]
    public void NormalizeChannel_RejectsUnknownChannel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoreChannelCodes.Normalize("random-client"));
    }

    [Theory]
    [InlineData(CoreChannelCodes.Zalo, true)]
    [InlineData(CoreChannelCodes.Telegram, true)]
    [InlineData(CoreChannelCodes.Partner, true)]
    [InlineData(CoreChannelCodes.Api, true)]
    [InlineData(CoreChannelCodes.Dashboard, false)]
    [InlineData(CoreChannelCodes.System, false)]
    public void ExternalChannels_RequireIdempotency(string channel, bool expected)
    {
        Assert.Equal(expected, CoreJobApplicationService.RequiresIdempotencyKey(channel));
    }

    [Fact]
    public void IdempotencyLock_IsScopedByCallerAndService()
    {
        var customerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var zalo = new CoreRequestContext(customerId, null, CoreChannelCodes.Zalo, "mini-app", "req-001");
        var partner = new CoreRequestContext(customerId, null, CoreChannelCodes.Partner, "partner-a", "req-001");

        var zaloLock = CoreJobApplicationService.BuildIdempotencyLockName(zalo, "TIMELAPSE", "req-001");
        var partnerLock = CoreJobApplicationService.BuildIdempotencyLockName(partner, "TIMELAPSE", "req-001");
        var otherServiceLock = CoreJobApplicationService.BuildIdempotencyLockName(zalo, "RDANCE", "req-001");

        Assert.NotEqual(zaloLock, partnerLock);
        Assert.NotEqual(zaloLock, otherServiceLock);
        Assert.Contains("zalo", zaloLock);
        Assert.Contains("TIMELAPSE", zaloLock);
    }

    [Fact]
    public async Task ExecutionRouter_RoutesByServiceCodeCaseInsensitively()
    {
        var adapter = new CapturingAdapter("TIMELAPSE");
        var router = new CoreExecutionRouter(new[] { adapter });
        var payload = JsonSerializer.SerializeToElement(new { sceneCount = 4 });
        var context = new CoreJobDispatchContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "timelapse",
            new CoreRequestContext(Guid.NewGuid(), Guid.NewGuid(), CoreChannelCodes.Zalo),
            payload,
            null,
            null);

        await router.DispatchAsync(context);

        Assert.Same(context, adapter.LastContext);
    }

    [Fact]
    public void ExecutionRouter_RejectsDuplicateServiceAdapters()
    {
        Assert.Throws<InvalidOperationException>(() => new CoreExecutionRouter(new ICoreJobExecutionAdapter[]
        {
            new CapturingAdapter("TIMELAPSE"),
            new CapturingAdapter("timelapse")
        }));
    }

    private sealed class CapturingAdapter : ICoreJobExecutionAdapter
    {
        public CapturingAdapter(string serviceCode)
        {
            ServiceCode = serviceCode;
        }

        public string ServiceCode { get; }
        public CoreJobDispatchContext? LastContext { get; private set; }

        public Task DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default)
        {
            LastContext = context;
            return Task.CompletedTask;
        }
    }
}
