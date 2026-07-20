using TodoX.Web.Services.AiProviders.Kie;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KieTaskStatusMapperTests
{
    [Theory]
    [InlineData("waiting", KieTaskStatuses.Queued)]
    [InlineData("queuing", KieTaskStatuses.Queued)]
    [InlineData("generating", KieTaskStatuses.Rendering)]
    [InlineData("success", KieTaskStatuses.Completed)]
    [InlineData("fail", KieTaskStatuses.Failed)]
    [InlineData("mystery", KieTaskStatuses.Unknown)]
    [InlineData(null, KieTaskStatuses.Unknown)]
    public void Map_ReturnsExpectedStatus(string? providerState, string expected)
    {
        Assert.Equal(expected, KieTaskStatusMapper.Map(providerState));
    }

    [Fact]
    public void TerminalHelpers_ClassifyStatuses()
    {
        Assert.True(KieTaskStatusMapper.IsTerminal(KieTaskStatuses.Completed));
        Assert.True(KieTaskStatusMapper.IsTerminal(KieTaskStatuses.Failed));
        Assert.True(KieTaskStatusMapper.IsSuccess(KieTaskStatuses.Completed));
        Assert.True(KieTaskStatusMapper.IsFailure(KieTaskStatuses.Failed));
        Assert.True(KieTaskStatusMapper.IsTransient(KieTaskStatuses.Rendering));
    }
}
