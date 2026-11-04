namespace TodoX.EditVideoTool.Server;

/// <summary>
/// Which app asked for the work (Claude Desktop, Claude Code, Codex, the TodoX window...), sent with every usage
/// session so the customer and TodoX can tell who ran it. MCP sets it from the client's own name on each call.
/// </summary>
public static class CallerApp
{
    private static volatile string? _current;

    public static string? Current => _current;

    public static void Set(string? name, string? version = null) =>
        _current = string.IsNullOrWhiteSpace(name) ? null : string.IsNullOrWhiteSpace(version) ? name.Trim() : $"{name.Trim()} {version.Trim()}";
}
