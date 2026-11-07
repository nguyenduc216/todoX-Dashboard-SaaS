using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public sealed record SceneVideoModelOption(
    string Key,
    string ProviderCode,
    string ModelCode,
    string? Mode,
    string DisplayName,
    IReadOnlyList<int> Durations)
{
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? ModelCode : DisplayName;
}

public interface IRVideoSceneVideoModelOptionsService
{
    Task<IReadOnlyList<SceneVideoModelOption>> GetOptionsAsync(CancellationToken ct = default);
    /// <summary>Options ordered by the service's configured video model order.</summary>
    Task<IReadOnlyList<SceneVideoModelOption>> GetOptionsAsync(Guid? serviceId, CancellationToken ct = default)
        => GetOptionsAsync(ct);
    Task<IReadOnlyList<int>> GetAllDurationsAsync(CancellationToken ct = default);
    IReadOnlyList<SceneVideoModelOption> GetCachedOptions();
    /// <summary>Cached options only when they were loaded for the same service order.</summary>
    IReadOnlyList<SceneVideoModelOption> GetCachedOptions(Guid? serviceId) => GetCachedOptions();
}

public sealed class RVideoSceneVideoModelOptionsService : IRVideoSceneVideoModelOptionsService
{
    private readonly IAiProviderModelService _models;
    private readonly CatalogRepository? _catalog;
    private IReadOnlyList<SceneVideoModelOption> _cached = Array.Empty<SceneVideoModelOption>();
    private Guid? _cachedServiceId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public RVideoSceneVideoModelOptionsService(IAiProviderModelService models, CatalogRepository? catalog = null)
    {
        _models = models;
        _catalog = catalog;
    }

    public Task<IReadOnlyList<SceneVideoModelOption>> GetOptionsAsync(CancellationToken ct = default)
        => GetOptionsAsync(null, ct);

    public async Task<IReadOnlyList<SceneVideoModelOption>> GetOptionsAsync(Guid? serviceId, CancellationToken ct = default)
    {
        var models = await _models.GetModelsAsync(
            RVideoVideoModelPolicy.ProviderCode,
            mediaType: "video",
            enabled: true,
            ct: ct);
        List<string>? order = null;
        if (serviceId is Guid id && _catalog is not null)
        {
            var service = await _catalog.GetServiceByIdAsync(id, ct);
            order = TodoX.Web.Models.Catalog.ServiceJobDefaultsCodec.FromJson(service?.JobDefaultsJson).VideoModelOrder;
        }
        var opts = BuildOptions(models, order);
        await _gate.WaitAsync(ct);
        try { _cached = opts; _cachedServiceId = serviceId; } finally { _gate.Release(); }
        return opts;
    }

    public async Task<IReadOnlyList<int>> GetAllDurationsAsync(CancellationToken ct = default)
    {
        var opts = await GetOptionsAsync(ct);
        return opts.SelectMany(x => x.Durations).Distinct().OrderBy(x => x).ToList();
    }

    public IReadOnlyList<SceneVideoModelOption> GetCachedOptions() => _cached;

    public IReadOnlyList<SceneVideoModelOption> GetCachedOptions(Guid? serviceId)
        => _cachedServiceId == serviceId ? _cached : Array.Empty<SceneVideoModelOption>();

    public static bool IsSupportedDuration(SceneVideoModelOption option, int duration) => option.Durations.Contains(duration);

    internal static IReadOnlyList<SceneVideoModelOption> BuildOptions(
        IReadOnlyList<AiProviderModelListItemDto> models,
        IEnumerable<string>? configuredOrder = null)
    {
        if (models is null || models.Count == 0) return Array.Empty<SceneVideoModelOption>();
        var ordered = RVideoVideoModelPolicy.Ordered(configuredOrder);
        return models
            .Where(m => m.AllowUserSelect && !m.IsDeprecated)
            .SelectMany(BuildForModel)
            .OrderBy(o => ResolvePolicyRank(ordered, o.ModelCode, o.Mode))
            .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<SceneVideoModelOption> BuildForModel(AiProviderModelListItemDto model)
    {
        var durations = model.SupportedDurations.Where(d => d > 0).Distinct().OrderBy(d => d).ToArray();
        if (durations.Length == 0) yield break;
        foreach (var policy in RVideoVideoModelPolicy.Models.Where(p =>
                     string.Equals(p.ProviderCode, model.ProviderCode, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(p.Model, model.ProviderModelCode, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(p.Mode)
                     && (model.SupportedModes.Count == 0 || model.SupportedModes.Contains(p.Mode!, StringComparer.OrdinalIgnoreCase))))
        {
            var mode = policy.Mode!;
            var label = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ProviderModelCode : model.DisplayName.Trim();
            yield return new SceneVideoModelOption(
                BuildKey(model.ProviderCode, model.ProviderModelCode, mode),
                model.ProviderCode,
                model.ProviderModelCode,
                mode,
                $"{label} / {mode}",
                durations);
        }
    }

    private static int ResolvePolicyRank(IReadOnlyList<RVideoVideoModelPolicyEntry> ordered, string modelCode, string? mode)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].Model, modelCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ordered[i].Mode, mode, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return int.MaxValue;
    }

    public static string BuildKey(string providerCode, string modelCode, string? mode)
        => $"{providerCode.Trim()}|{modelCode.Trim()}|{mode?.Trim()}";

    public static int ResolveNearestDuration(int sceneDuration, IReadOnlyList<int> supported)
    {
        if (supported is null || supported.Count == 0) return Math.Max(1, sceneDuration);
        return supported.FirstOrDefault(d => d >= sceneDuration) is int n and > 0 ? n : supported.Max();
    }
}

