namespace TodoX.Web.Services.AiCharacters;

public interface IAiImageProviderFactory
{
    IAiImageProviderService GetProvider(string providerCode);
}

public sealed class AiImageProviderFactory : IAiImageProviderFactory
{
    private readonly ITodoXImageProviderService _todoX;
    private readonly IOpenRouterImageService _openRouter;

    public AiImageProviderFactory(ITodoXImageProviderService todoX, IOpenRouterImageService openRouter)
    {
        _todoX = todoX;
        _openRouter = openRouter;
    }

    public IAiImageProviderService GetProvider(string providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)
            || providerCode.Equals("todox_image", StringComparison.OrdinalIgnoreCase)
            || providerCode.Equals("todox", StringComparison.OrdinalIgnoreCase))
        {
            return _todoX;
        }

        if (providerCode.Equals("openrouter_image", StringComparison.OrdinalIgnoreCase))
        {
            return _openRouter;
        }

        throw new NotSupportedException($"Image provider '{providerCode}' is not supported yet.");
    }
}
