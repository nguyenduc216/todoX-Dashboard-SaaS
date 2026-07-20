using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellCallbackTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("configured-secret", true)]
    public void CallbackSecretMustBeConfigured(string? secret, bool expected)
    {
        Assert.Equal(expected, DanceSellPhase1Endpoints.IsCallbackConfigured(secret));
    }
}
