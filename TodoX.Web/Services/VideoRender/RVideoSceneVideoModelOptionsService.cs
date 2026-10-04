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
    Task<IReadOnlyList<int>> GetAllDurationsAsync(CancellationToken ct = default);
    IReadOnlyList<SceneVideoModelOption> GetCachedOptions();
}

public sealed class RVideoSceneVideoModelOptionsService : IRVideoSceneVideoModelOptionsService
{
    private readonly IAiProviderModelService _models;
    private IReadOnlyList<SceneVideoModelOption> _cached = Array.Empty<SceneVideoModelOption>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    public RVideoSceneVideoModelOptionsService(IAiProviderModelService models) => _models = models;

    public async Task<IReadOnlyList<SceneVideoModelOption>> GetOptionsAsync(CancellationToken ct = default)
    {
        var models = await _models.GetModelsAsync(
            RVideoVideoModelPolicy.ProviderCode,
            mediaType: "video",
            enabled: true,
            ct: ct);
        var opts = BuildOptions(models);
        await _gate.WaitAsync(ct);
        try { _cached = opts; } finally { _gate.Release(); }
        return opts;
    }

    public async Task<IReadOnlyList<int>> GetAllDurationsAsync(CancellationToken ct = default)
    {
        var opts = await GetOptionsAsync(ct);
        return opts.SelectMany(x => x.Durations).Distinct().OrderBy(x => x).ToList();
    }

    public IReadOnlyList<SceneVideoModelOption> GetCachedOptions() => _cached;

    public static bool IsSupportedDuration(SceneVideoModelOption option, int duration) => option.Durations.Contains(duration);

    internal static IReadOnlyList<SceneVideoModelOption> BuildOptions(IReadOnlyList<AiProviderModelListItemDto> models)
    {
        if (models is null || models.Count == 0) return Array.Empty<SceneVideoModelOption>();
        return models
            .Where(m => m.AllowUserSelect && !m.IsDeprecated)
            .SelectMany(BuildForModel)
            .OrderBy(o => ResolvePolicyRank(o.ModelCode, o.Mode))
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

    private static int ResolvePolicyRank(string modelCode, string? mode)
    {
        var p = RVideoVideoModelPolicy.Models.FirstOrDefault(x =>
            string.Equals(x.Model, modelCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Mode, mode, StringComparison.OrdinalIgnoreCase));
        return p?.AttemptIndex ?? int.MaxValue;
    }

    public static string BuildKey(string providerCode, string modelCode, string? mode)
        => $"{providerCode.Trim()}|{modelCode.Trim()}|{mode?.Trim()}";

    public static int ResolveNearestDuration(int sceneDuration, IReadOnlyList<int> supported)
    {
        if (supported is null || supported.Count == 0) return Math.Max(1, sceneDuration);
        return supported.FirstOrDefault(d => d >= sceneDuration) is int n and > 0 ? n : supported.Max();
    }
}

