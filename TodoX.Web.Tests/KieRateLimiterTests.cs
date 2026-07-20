using TodoX.Web.Services.AiProviders.Kie;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KieRateLimiterTests
{
    [Fact]
    public async Task AcquireSubmitPermitAsync_DeniesAfterWindowLimit()
    {
        var limiter = new InMemoryKieRateLimiter(new StaticOptionsMonitor<KieOptions>(new KieOptions
        {
            RateLimitRequestsPer10S = 2
        }));

        Assert.True((await limiter.AcquireSubmitPermitAsync("acct", CancellationToken.None)).Allowed);
        Assert.True((await limiter.AcquireSubmitPermitAsync("acct", CancellationToken.None)).Allowed);
        var third = await limiter.AcquireSubmitPermitAsync("acct", CancellationToken.None);

        Assert.False(third.Allowed);
        Assert.True(third.RetryAfter > TimeSpan.Zero);
    }
}
