using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiBillingPayerTypes
{
    public const string Customer = "customer";
    public const string System = "system";
}

public sealed record AiBillingPayerResolveRequest(
    Guid? CustomerId,
    Guid? UserId,
    string? FeatureCode,
    string? CapabilityCode,
    object? Metadata);

public sealed record AiBillingPayerContext(
    string PayerType,
    Guid? PayerCustomerId,
    string? SystemWalletCode,
    string ResolutionSource);

public interface IAiBillingPayerResolver
{
    AiBillingPayerContext Resolve(AiBillingPayerResolveRequest request);
}

public sealed class AiBillingPayerResolver : IAiBillingPayerResolver
{
    public const string SystemImageWalletCode = "TODOX_AI_IMAGE_SYSTEM";

    private readonly AuthStateService _auth;

    public AiBillingPayerResolver(AuthStateService auth)
    {
        _auth = auth;
    }

    public AiBillingPayerContext Resolve(AiBillingPayerResolveRequest request)
        => ResolveCore(_auth.CurrentUser, request);

    public static AiBillingPayerContext ResolveCore(CurrentUserSession? current, AiBillingPayerResolveRequest request)
    {
        if (current?.IsAuthenticated == true && current.IsCustomer)
        {
            var customerId = current.CustomerId
                ?? throw new InvalidOperationException("Authenticated customer session is missing customer id.");

            if (request.CustomerId is Guid requested && requested != customerId)
            {
                throw new InvalidOperationException("Image billing customer scope does not match the authenticated session.");
            }

            return new AiBillingPayerContext(
                AiBillingPayerTypes.Customer,
                customerId,
                SystemWalletCode: null,
                ResolutionSource: "authenticated_customer");
        }

        if (request.CustomerId is Guid customerIdFromServer)
        {
            return new AiBillingPayerContext(
                AiBillingPayerTypes.Customer,
                customerIdFromServer,
                SystemWalletCode: null,
                ResolutionSource: "trusted_server_customer_context");
        }

        if (request.UserId is Guid)
        {
            return new AiBillingPayerContext(
                AiBillingPayerTypes.System,
                PayerCustomerId: null,
                SystemImageWalletCode,
                ResolutionSource: current?.IsAuthenticated == true ? "authenticated_system_user" : "trusted_background_user");
        }

        throw new InvalidOperationException("Cannot resolve image billing payer: missing customer and authenticated/system user context.");
    }
}
