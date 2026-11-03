using System.Reflection;
using TodoX.EditVideoTool.Components;

namespace TodoX.EditVideoTool.Status;

public sealed record AgentStatus(
    string Version,
    string InstallRoot,
    string LicenseState,
    IReadOnlyList<ComponentInfo> Components);

public sealed class StatusService
{
    private readonly AgentPaths _paths;
    private readonly ComponentLocator _components;

    public StatusService(AgentPaths paths, ComponentLocator components)
    {
        _paths = paths;
        _components = components;
    }

    public static string Version =>
        typeof(StatusService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    public AgentStatus Get()
    {
        // P2 replaces this with real license-file verification.
        var licenseState = File.Exists(_paths.LicenseFile) ? "present_unverified" : "not_activated";
        return new AgentStatus(Version, _paths.Root, licenseState, _components.LocateAll());
    }
}
