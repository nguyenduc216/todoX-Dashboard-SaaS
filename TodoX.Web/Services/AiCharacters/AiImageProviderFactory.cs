using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.AiCharacters;

public interface IAiImageProviderFactory
{
    IAiImageProviderService GetProvider(string providerCode);
}

public sealed class AiImageProviderFactory : IAiImageProviderFactory
{
    private readonly ITodoXImageProviderService _todoX;
    private readonly IOpenRouterImageService _openRouter;
    private readonly IYEScaleImageService _yescale;
    private readonly IAiImageProviderService _gommo79Ai;

    public AiImageProviderFactory(
        ITodoXImageProviderService todoX,
        IOpenRouterImageService openRouter,
        IYEScaleImageService yescale,
        Gommo79AiImageService gommo79Ai)
    {
        _todoX = todoX;
        _openRouter = openRouter;
        _yescale = yescale;
        _gommo79Ai = gommo79Ai;
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

        if (providerCode.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase))
        {
            return _yescale;
        }

        if (providerCode.Equals("79ai_task_image", StringComparison.OrdinalIgnoreCase))
        {
            return _gommo79Ai;
        }

        throw new NotSupportedException($"Image provider '{providerCode}' is not supported yet.");
    }
}
