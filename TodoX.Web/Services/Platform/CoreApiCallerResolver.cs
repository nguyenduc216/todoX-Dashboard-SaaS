namespace TodoX.Web.Services.Platform;

/// <summary>
/// Authentication adapters live at the transport edge. Examples: Zalo login token, Telegram account
/// binding, partner API key or OAuth client. Core business services only receive CoreRequestContext.
/// </summary>
public interface ICoreApiCallerAuthenticator
{
    bool CanHandle(HttpRequest request);
    Task<CoreRequestContext?> AuthenticateAsync(HttpRequest request, CancellationToken ct = default);
}

public interface ICoreApiCallerResolver
{
    Task<CoreRequestContext?> ResolveAsync(HttpRequest request, CancellationToken ct = default);
}

public sealed class CoreApiCallerResolver : ICoreApiCallerResolver
{
    private readonly IReadOnlyList<ICoreApiCallerAuthenticator> _authenticators;

    public CoreApiCallerResolver(IEnumerable<ICoreApiCallerAuthenticator> authenticators)
    {
        _authenticators = authenticators.ToList();
    }

    public async Task<CoreRequestContext?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        foreach (var authenticator in _authenticators)
        {
            if (!authenticator.CanHandle(request))
            {
                continue;
            }

            var context = await authenticator.AuthenticateAsync(request, ct);
            if (context is null)
            {
                return null;
            }

            // Validate at the boundary so unknown transport/channel values never reach the business layer.
            _ = context.NormalizedChannel;
            try
            {
                CoreJobAccess.EnsureAuthenticated(context);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return context;
        }

        return null;
    }
}
