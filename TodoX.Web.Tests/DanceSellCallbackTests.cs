using Microsoft.AspNetCore.Http;
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

    [Fact]
    public void CallbackAuthorization_ReturnsNotConfiguredWhenSecretIsEmpty()
    {
        var request = new DefaultHttpContext().Request;

        var status = DanceSellPhase1Endpoints.GetCallbackAuthorizationStatus(request, "");

        Assert.Equal(KieCallbackAuthorizationStatus.NotConfigured, status);
    }

    [Fact]
    public void CallbackAuthorization_ReturnsMissingSecretWhenRequestHasNoSecret()
    {
        var request = new DefaultHttpContext().Request;

        var status = DanceSellPhase1Endpoints.GetCallbackAuthorizationStatus(request, "configured-secret");

        Assert.Equal(KieCallbackAuthorizationStatus.MissingSecret, status);
    }

    [Fact]
    public void CallbackAuthorization_ReturnsInvalidSecretWhenHeaderIsWrong()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-KIE-CALLBACK-SECRET"] = "wrong";

        var status = DanceSellPhase1Endpoints.GetCallbackAuthorizationStatus(request, "configured-secret");

        Assert.Equal(KieCallbackAuthorizationStatus.InvalidSecret, status);
    }

    [Fact]
    public void CallbackAuthorization_ReturnsAuthorizedWhenHeaderMatches()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-KIE-CALLBACK-SECRET"] = "configured-secret";

        var status = DanceSellPhase1Endpoints.GetCallbackAuthorizationStatus(request, "configured-secret");

        Assert.Equal(KieCallbackAuthorizationStatus.Authorized, status);
    }

    [Fact]
    public void CallbackAuthorization_UsesQueryFallbackWhenHeaderMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?secret=configured-secret");

        var status = DanceSellPhase1Endpoints.GetCallbackAuthorizationStatus(context.Request, "configured-secret");

        Assert.Equal(KieCallbackAuthorizationStatus.Authorized, status);
    }
}
