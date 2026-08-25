namespace TodoX.Web.Services.Timelapse;

public sealed class TimelapseImageModelSelector
{
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseImageModelSelector> _logger;

    public TimelapseImageModelSelector(
        Microsoft.Extensions.Options.IOptions<TimelapseProviderWorkerOptions> options,
        ILogger<TimelapseImageModelSelector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<string> Select(bool hasReference)
    {
        var selected = GetModels(hasReference).ToArray();

        if (selected.Length == 0)
        {
            throw new InvalidOperationException("Timelapse image model chain is empty.");
        }

        _logger.LogInformation(
            "TIMELAPSE_IMAGE_MODEL_SELECTED hasReference={HasReference} primary={Primary} fallbackCount={FallbackCount} models={Models}",
            hasReference,
            selected[0],
            Math.Max(0, selected.Length - 1),
            string.Join(",", selected));

        return selected;
    }

    public string? GetNext(string? currentModel, bool hasReference)
    {
        var models = GetModels(hasReference).ToArray();
        if (models.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(currentModel))
        {
            return models[0];
        }

        var index = Array.FindIndex(models, model => string.Equals(model, currentModel, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return models[0];
        }

        return index + 1 < models.Length ? models[index + 1] : null;
    }

    private IEnumerable<string> GetModels(bool hasReference)
    {
        var source = hasReference ? _options.ImageModelsWithReference : _options.ImageModelsWithoutReference;
        return source
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
