using System.Text.Json;
using TodoX.Web.Models.Catalog;
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
        Assert.StartsWith("core-service-job:core:zalo:", zaloLock);
    }

    [Fact]
    public void LogicalRequestIdentity_IsStableForSameScopeAndKey()
    {
        var context = new CoreRequestContext(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.NewGuid(),
            CoreChannelCodes.Api,
            "client-a");

        var first = CoreJobApplicationService.BuildLogicalRequestId(context, "TIMELAPSE", "request-1");
        var second = CoreJobApplicationService.BuildLogicalRequestId(context, "timelapse", "request-1");

        Assert.Equal(first, second);
        Assert.StartsWith("core:api:", first);
    }

    [Fact]
    public void LogicalRequestIdentity_DiffersByCustomerAndClient()
    {
        var customerA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var customerB = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var first = CoreJobApplicationService.BuildLogicalRequestId(
            new CoreRequestContext(customerA, null, CoreChannelCodes.Partner, "client-a"),
            "TIMELAPSE",
            "request-1");
        var otherCustomer = CoreJobApplicationService.BuildLogicalRequestId(
            new CoreRequestContext(customerB, null, CoreChannelCodes.Partner, "client-a"),
            "TIMELAPSE",
            "request-1");
        var otherClient = CoreJobApplicationService.BuildLogicalRequestId(
            new CoreRequestContext(customerA, null, CoreChannelCodes.Partner, "client-b"),
            "TIMELAPSE",
            "request-1");

        Assert.NotEqual(first, otherCustomer);
        Assert.NotEqual(first, otherClient);
    }

    [Fact]
    public void JobAccess_IsCustomerScopedAndDoesNotTrustUserIdAlone()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var user = Guid.NewGuid();

        Assert.True(CoreJobAccess.CanAccess(
            new CoreRequestContext(customerA, user, CoreChannelCodes.Dashboard),
            customerA));
        Assert.False(CoreJobAccess.CanAccess(
            new CoreRequestContext(customerA, user, CoreChannelCodes.Dashboard),
            customerB));
        Assert.False(CoreJobAccess.CanAccess(
            new CoreRequestContext(null, user, CoreChannelCodes.Dashboard),
            customerA));
    }

    [Fact]
    public void JobAccess_RequiresTrustedSystemForBroadAccess()
    {
        var owner = Guid.NewGuid();

        Assert.False(CoreJobAccess.CanAccess(
            new CoreRequestContext(null, Guid.NewGuid(), CoreChannelCodes.System),
            owner));
        Assert.False(CoreJobAccess.CanAccess(
            new CoreRequestContext(null, Guid.NewGuid(), CoreChannelCodes.Dashboard, IsTrustedInternal: true),
            owner));
        Assert.True(CoreJobAccess.CanAccess(
            new CoreRequestContext(null, Guid.NewGuid(), CoreChannelCodes.System, IsTrustedInternal: true),
            owner));
    }

    [Theory]
    [InlineData("""{"qualityTier":"premium"}""", ServiceSellPriceQualityTiers.Premium)]
    [InlineData("""{"quality_tier":"standard"}""", ServiceSellPriceQualityTiers.Standard)]
    [InlineData("""{"mode":"professional"}""", ServiceSellPriceQualityTiers.Premium)]
    [InlineData("""{"videoMode":"fast"}""", ServiceSellPriceQualityTiers.Standard)]
    public void BillingQualityTier_IsResolvedServerSide(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, CoreBillingService.ResolveQualityTier(document.RootElement));
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
