using System.Reflection;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Status;

public sealed record AgentStatus(
    string Version,
    string InstallRoot,
    string LicenseState,
    string LicenseMessage,
    string? Plan,
    DateTimeOffset? LicenseExpiresAt,
    IReadOnlyList<ComponentInfo> Components);

public sealed class StatusService
{
    private readonly AgentPaths _paths;
    private readonly ComponentLocator _components;
    private readonly LicenseGuard _license;

    public StatusService(AgentPaths paths, ComponentLocator components, LicenseGuard license)
    {
        _paths = paths;
        _components = components;
        _license = license;
    }

    public static string Version =>
        typeof(StatusService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    public async Task<AgentStatus> GetAsync(CancellationToken ct = default)
    {
        var license = await _license.EnsureAsync(ct);
        return new AgentStatus(
            Version,
            _paths.Root,
            license.Code,
            license.Message,
            license.Payload?.Plan,
            license.Payload?.Exp is { } exp ? DateTimeOffset.FromUnixTimeSeconds(exp) : null,
            _components.LocateAll());
    }
}
