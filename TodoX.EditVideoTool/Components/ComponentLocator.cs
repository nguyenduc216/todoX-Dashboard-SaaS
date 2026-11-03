namespace TodoX.EditVideoTool.Components;

public sealed record ComponentInfo(string Name, bool Installed, string? Path, string Source);

/// <summary>
/// Finds the external tools the agent drives. Components installed by TodoX win over anything on PATH,
/// so a customer's own (possibly incompatible) ffmpeg/node never shadows the tested version.
/// </summary>
public sealed class ComponentLocator
{
    public static readonly IReadOnlyList<string> KnownComponents = ["ffmpeg", "ffprobe", "node"];

    private readonly AgentPaths _paths;
    private readonly Func<string?> _pathVariable;

    public ComponentLocator(AgentPaths paths, Func<string?>? pathVariable = null)
    {
        _paths = paths;
        _pathVariable = pathVariable ?? (() => Environment.GetEnvironmentVariable("PATH"));
    }

    public IReadOnlyList<ComponentInfo> LocateAll() => KnownComponents.Select(Locate).ToList();

    public ComponentInfo Locate(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;

        if (Directory.Exists(_paths.ComponentsDir))
        {
            var bundled = Directory
                .EnumerateFiles(_paths.ComponentsDir, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (bundled is not null)
            {
                return new ComponentInfo(name, true, bundled, "todox");
            }
        }

        foreach (var dir in (_pathVariable() ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return new ComponentInfo(name, true, candidate, "path");
            }
        }

        return new ComponentInfo(name, false, null, "missing");
    }
}
