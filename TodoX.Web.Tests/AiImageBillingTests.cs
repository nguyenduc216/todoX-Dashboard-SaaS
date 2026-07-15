using TodoX.Web.Services.AiProviders;
using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public class AiImageBillingTests
{
    [Fact]
    public void ConfiguredCost_ConvertsPointsToUsdUsingTodoXPolicy()
    {
        var cost = AiImageBillingCost.FromConfiguredPoints(0.064m, 8000m, 10000m);

        Assert.Equal(0.08m, cost.ProviderEstimatedCostUsd);
        Assert.Equal(0.064m, cost.ProviderCostPoints);
        Assert.Equal(0.064m, cost.CustomerChargedPoints);
        Assert.Null(cost.ProviderActualCostUsd);
        Assert.Equal("configured_tariff", cost.ProviderCostSource);
    }

    [Fact]
    public async Task YEScaleAccountSnapshot_ReturnsNotSupportedUntilBalanceEndpointIsVerified()
    {
        var service = new YEScaleAccountService();

        var snapshot = await service.GetSnapshotAsync(forceRefresh: true);

        Assert.Equal(YEScaleBalanceStatus.NotSupported, snapshot.Status);
        Assert.Null(snapshot.BalanceUsd);
        Assert.Contains("not verified", snapshot.Message);
    }

    [Fact]
    public void PayerResolver_UsesAuthenticatedCustomerScope()
    {
        var customerId = Guid.NewGuid();
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            CustomerId = customerId,
            Role = TodoXUserRole.CustomerOwner
        };

        var payer = AiBillingPayerResolver.ResolveCore(session, new AiBillingPayerResolveRequest(
            customerId,
            session.UserId,
            "avatar_builder",
            "avatar_generation",
            Metadata: null));

        Assert.Equal(AiBillingPayerTypes.Customer, payer.PayerType);
        Assert.Equal(customerId, payer.PayerCustomerId);
        Assert.Null(payer.SystemWalletCode);
    }

    [Fact]
    public void PayerResolver_RejectsSpoofedCustomerScope()
    {
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Role = TodoXUserRole.CustomerOwner
        };

        Assert.Throws<InvalidOperationException>(() => AiBillingPayerResolver.ResolveCore(session, new AiBillingPayerResolveRequest(
            Guid.NewGuid(),
            session.UserId,
            "avatar_builder",
            "avatar_generation",
            Metadata: null)));
    }

    [Fact]
    public void PayerResolver_UsesSystemWalletForTrustedSystemUser()
    {
        var payer = AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: Guid.NewGuid(),
            "service_thumbnail",
            "thumbnail_generation",
            Metadata: null));

        Assert.Equal(AiBillingPayerTypes.System, payer.PayerType);
        Assert.Null(payer.PayerCustomerId);
        Assert.Equal(AiBillingPayerResolver.SystemImageWalletCode, payer.SystemWalletCode);
    }

    [Fact]
    public void PayerResolver_RejectsAnonymousWithoutPayer()
    {
        Assert.Throws<InvalidOperationException>(() => AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: null,
            "public_avatar_builder",
            "avatar_generation",
            Metadata: null)));
    }

    [Fact]
    public void AttemptParser_DoesNotCopyFinalActualCostToFallbackAttempts()
    {
        var usageJson = """
        {
          "fallbackTrail": [
            { "from": "nano-banana-2", "taskId": "task_a", "errorCode": "rate_limit", "reason": "rate limit" }
          ]
        }
        """;

        var attempts = AiImageBillingAttemptParser.Parse(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = "img-1",
            Success = true,
            ActualModel = "seedream-5",
            ProviderTaskId = "task_b",
            ProviderActualCostUsd = 0.065m,
            ProviderUsageJson = usageJson
        });

        Assert.Equal(2, attempts.Count);
        Assert.Equal("nano-banana-2", attempts[0].ModelName);
        Assert.Equal(0.08m, attempts[0].ProviderEstimatedCostUsd);
        Assert.Null(attempts[0].ProviderActualCostUsd);
        Assert.Equal("configured_tariff", attempts[0].CostSource);
        Assert.Equal("seedream-5", attempts[1].ModelName);
        Assert.Equal(0.065m, attempts[1].ProviderEstimatedCostUsd);
        Assert.Equal(0.065m, attempts[1].ProviderActualCostUsd);
        Assert.Equal("provider_actual", attempts[1].CostSource);
    }
}
