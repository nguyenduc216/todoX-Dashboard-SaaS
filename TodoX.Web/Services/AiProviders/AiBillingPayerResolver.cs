using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiBillingPayerTypes
{
    public const string Customer = "customer";
    public const string System = "system";
    public const string SystemCampaign = "system_campaign";
}

public static class AiBillingPermissions
{
    public const string UseSystemImageWallet = "ai.image.system_wallet.use";
}

public sealed record AiBillingTrustedPayerContext(
    string PayerType,
    Guid? PayerCustomerId,
    Guid? UserId,
    string? SystemWalletCode,
    string Source);

public sealed record AiBillingPayerResolveRequest(
    Guid? CustomerId,
    Guid? UserId,
    string? FeatureCode,
    string? CapabilityCode,
    object? Metadata,
    AiBillingTrustedPayerContext? TrustedContext = null);

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

        if (current?.IsAuthenticated == true)
        {
            if (current.IsRoot || current.Can(AiBillingPermissions.UseSystemImageWallet))
            {
                return new AiBillingPayerContext(
                    AiBillingPayerTypes.System,
                    PayerCustomerId: null,
                    SystemImageWalletCode,
                    ResolutionSource: current.IsRoot ? "authenticated_root" : "authenticated_system_wallet_permission");
            }

            throw new InvalidOperationException("Authenticated user is not allowed to use the system image wallet.");
        }

        if (request.TrustedContext is not null)
        {
            return ResolveTrustedContext(request);
        }

        throw new InvalidOperationException("Cannot resolve image billing payer: missing authenticated session or trusted background payer context.");
    }

    public static AiBillingTrustedPayerContext CreateTrustedBackgroundContext(CurrentUserSession user)
    {
        if (user.IsAuthenticated && user.IsCustomer)
        {
            return new AiBillingTrustedPayerContext(
                AiBillingPayerTypes.Customer,
                user.CustomerId ?? throw new InvalidOperationException("Authenticated customer session is missing customer id."),
                user.UserId,
                SystemWalletCode: null,
                Source: "background_job");
        }

        if (user.IsAuthenticated && (user.IsRoot || user.Can(AiBillingPermissions.UseSystemImageWallet)))
        {
            return new AiBillingTrustedPayerContext(
                AiBillingPayerTypes.System,
                PayerCustomerId: null,
                user.UserId,
                SystemImageWalletCode,
                Source: "background_job");
        }

        throw new InvalidOperationException("Current user is not allowed to enqueue system-funded image billing jobs.");
    }

    private static AiBillingPayerContext ResolveTrustedContext(AiBillingPayerResolveRequest request)
    {
        var trusted = request.TrustedContext!;
        if (!string.Equals(trusted.Source, "background_job", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trusted.Source, "public_campaign", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Trusted image billing payer context has an invalid source.");
        }

        if (trusted.UserId is Guid trustedUser && request.UserId is Guid requestUser && trustedUser != requestUser)
        {
            throw new InvalidOperationException("Trusted image billing payer context user does not match the render request.");
        }

        if (string.Equals(trusted.PayerType, AiBillingPayerTypes.Customer, StringComparison.OrdinalIgnoreCase))
        {
            var customerId = trusted.PayerCustomerId
                ?? throw new InvalidOperationException("Trusted customer payer context is missing customer id.");
            if (request.CustomerId is Guid requested && requested != customerId)
            {
                throw new InvalidOperationException("Trusted image billing payer context customer does not match the render request.");
            }

            return new AiBillingPayerContext(
                AiBillingPayerTypes.Customer,
                customerId,
                SystemWalletCode: null,
                ResolutionSource: trusted.Source);
        }

        if (string.Equals(trusted.PayerType, AiBillingPayerTypes.System, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(trusted.SystemWalletCode, SystemImageWalletCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Trusted system payer context references an unsupported system wallet.");
            }

            return new AiBillingPayerContext(
                AiBillingPayerTypes.System,
                PayerCustomerId: null,
                SystemImageWalletCode,
                ResolutionSource: trusted.Source);
        }

        throw new InvalidOperationException("Trusted image billing payer context has an unsupported payer type.");
    }
}
