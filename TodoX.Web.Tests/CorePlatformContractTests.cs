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
