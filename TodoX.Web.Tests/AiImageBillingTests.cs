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
    public async Task BillingDashboard_RequiresDashboardPermissionNotSystemWalletPermission()
    {
        var service = new AiImageBillingDashboardService(null!, null!);
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Role = TodoXUserRole.Admin,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiBillingPermissions.UseSystemImageWallet }
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetSnapshotAsync(new AiImageBillingDashboardRequest(), session));
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
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Role = TodoXUserRole.Admin,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiBillingPermissions.UseSystemImageWallet }
        };

        var payer = AiBillingPayerResolver.ResolveCore(session, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: session.UserId,
            "service_thumbnail",
            "thumbnail_generation",
            Metadata: null));

        Assert.Equal(AiBillingPayerTypes.System, payer.PayerType);
        Assert.Null(payer.PayerCustomerId);
        Assert.Equal(AiBillingPayerResolver.SystemImageWalletCode, payer.SystemWalletCode);
    }

    [Fact]
    public void PayerResolver_RejectsAdminWithoutSystemWalletPermission()
    {
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Role = TodoXUserRole.Admin
        };

        Assert.Throws<InvalidOperationException>(() => AiBillingPayerResolver.ResolveCore(session, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: session.UserId,
            "service_thumbnail",
            "thumbnail_generation",
            Metadata: null)));
    }

    [Fact]
    public void PayerResolver_AllowsRootSystemWallet()
    {
        var session = new CurrentUserSession
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Role = TodoXUserRole.Admin,
            IsRoot = true
        };

        var payer = AiBillingPayerResolver.ResolveCore(session, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: session.UserId,
            "service_thumbnail",
            "thumbnail_generation",
            Metadata: null));

        Assert.Equal(AiBillingPayerTypes.System, payer.PayerType);
    }

    [Fact]
    public void PayerResolver_AllowsTrustedBackgroundCustomerContext()
    {
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var payer = AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            customerId,
            userId,
            "render_job_scene_image",
            "scene_image_generation",
            Metadata: null,
            new AiBillingTrustedPayerContext(AiBillingPayerTypes.Customer, customerId, userId, null, "background_job")));

        Assert.Equal(AiBillingPayerTypes.Customer, payer.PayerType);
        Assert.Equal(customerId, payer.PayerCustomerId);
    }

    [Fact]
    public void PayerResolver_RejectsBackgroundWithoutTrustedContext()
    {
        Assert.Throws<InvalidOperationException>(() => AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: Guid.NewGuid(),
            "render_job_scene_image",
            "scene_image_generation",
            Metadata: null)));
    }

    [Fact]
    public void PayerResolver_RejectsSpoofedSystemWalletCode()
    {
        Assert.Throws<InvalidOperationException>(() => AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            CustomerId: null,
            UserId: Guid.NewGuid(),
            "render_job_scene_image",
            "scene_image_generation",
            Metadata: null,
            new AiBillingTrustedPayerContext(AiBillingPayerTypes.System, null, Guid.NewGuid(), "FAKE_WALLET", "background_job"))));
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
        var tariffSnapshotJson = """
        {
          "tariffs": [
            { "model": "nano-banana-2", "providerEstimatedCostUsd": 0.08, "costSource": "configured_tariff" },
            { "model": "seedream-5", "providerEstimatedCostUsd": 0.065, "costSource": "configured_tariff" }
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
            ProviderUsageJson = usageJson,
            TariffSnapshotJson = tariffSnapshotJson
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

    [Fact]
    public void AttemptParser_DoesNotInventEstimatedCostWhenTariffMissing()
    {
        var attempts = AiImageBillingAttemptParser.Parse(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = "img-2",
            Success = true,
            ActualModel = "seedream-5",
            ProviderTaskId = "task_b",
            ProviderUsageJson = """
            { "fallbackTrail": [ { "from": "nano-banana-2", "taskId": "task_a" } ] }
            """,
            TariffSnapshotJson = """
            { "tariffs": [ { "model": "seedream-5", "providerEstimatedCostUsd": 0.065, "costSource": "configured_tariff" } ] }
            """
        });

        Assert.Null(attempts[0].ProviderEstimatedCostUsd);
        Assert.Null(attempts[0].CostSource);
        Assert.Equal(0.065m, attempts[1].ProviderEstimatedCostUsd);
    }
}
