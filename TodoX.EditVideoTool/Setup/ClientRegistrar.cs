using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoX.EditVideoTool.Setup;

public sealed record ClientStatus(string Id, string Name, bool Detected, bool Registered, string Detail);

/// <summary>
/// Registers the installed exe as the "todox" MCP server with the assistants found on this machine and installs
/// the thin skill. Existing settings in those files are preserved; only the "todox" entry is added/replaced.
/// </summary>
public sealed class ClientRegistrar
{
    public const string ServerName = "todox";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private string ClaudeDesktopConfig => Path.Combine(_appData, "Claude", "claude_desktop_config.json");
    private string ClaudeCodeConfig => Path.Combine(_home, ".claude.json");
    private string CodexConfig => Path.Combine(_home, ".codex", "config.toml");

    public IReadOnlyList<ClientStatus> Detect(string exePath) =>
    [
        new("claude-desktop", "Claude Desktop", Directory.Exists(Path.Combine(_appData, "Claude")),
            JsonHasServer(ClaudeDesktopConfig, exePath), ClaudeDesktopConfig),
        new("claude-code", "Claude Code", File.Exists(ClaudeCodeConfig) || Directory.Exists(Path.Combine(_home, ".claude")) || OnPath("claude"),
            JsonHasServer(ClaudeCodeConfig, exePath), ClaudeCodeConfig),
        new("codex", "Codex", Directory.Exists(Path.Combine(_home, ".codex")) || OnPath("codex"),
            File.Exists(CodexConfig) && File.ReadAllText(CodexConfig).Contains($"[mcp_servers.{ServerName}]"), CodexConfig)
    ];

    public IReadOnlyList<string> RegisterAll(string exePath, string skillMarkdown)
    {
        var done = new List<string>();
        foreach (var client in Detect(exePath).Where(c => c.Detected))
        {
            switch (client.Id)
            {
                case "claude-desktop":
                    UpsertJson(ClaudeDesktopConfig, exePath);
                    break;
                case "claude-code":
                    UpsertJson(ClaudeCodeConfig, exePath);
                    WriteSkill(Path.Combine(_home, ".claude", "skills", "todox-video"), skillMarkdown);
                    break;
                case "codex":
                    UpsertCodex(exePath);
                    WriteSkill(Path.Combine(_home, ".codex", "skills", "todox-video"), skillMarkdown);
                    break;
            }

            done.Add(client.Name);
        }

        return done;
    }

    public void UnregisterAll()
    {
        RemoveJson(ClaudeDesktopConfig);
        RemoveJson(ClaudeCodeConfig);
        if (File.Exists(CodexConfig))
        {
            File.WriteAllText(CodexConfig, RemoveTomlSection(File.ReadAllText(CodexConfig), $"mcp_servers.{ServerName}"));
        }

        foreach (var dir in new[] { Path.Combine(_home, ".claude", "skills", "todox-video"), Path.Combine(_home, ".codex", "skills", "todox-video") })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    internal static string UpsertTomlSection(string toml, string exePath)
    {
        var cleaned = RemoveTomlSection(toml, $"mcp_servers.{ServerName}").TrimEnd();
        var escaped = exePath.Replace("\\", "\\\\");
        var section = $"[mcp_servers.{ServerName}]{Environment.NewLine}command = \"{escaped}\"{Environment.NewLine}args = [\"mcp\"]{Environment.NewLine}";
        return (cleaned.Length > 0 ? cleaned + Environment.NewLine + Environment.NewLine : string.Empty) + section;
    }

    internal static string RemoveTomlSection(string toml, string section)
    {
        var lines = toml.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        var skipping = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                skipping = trimmed == $"[{section}]";
            }

            if (!skipping)
            {
                output.Append(line).Append('\n');
            }
        }

        return output.ToString().TrimEnd('\n') + (toml.Length > 0 ? "\n" : string.Empty);
    }

    internal static string UpsertJsonText(string? json, string exePath)
    {
        var root = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)) as JsonObject ?? new JsonObject();
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        servers[ServerName] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = exePath,
            ["args"] = new JsonArray("mcp")
        };
        return root.ToJsonString(Pretty);
    }

    private void UpsertCodex(string exePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexConfig)!);
        var existing = File.Exists(CodexConfig) ? File.ReadAllText(CodexConfig) : string.Empty;
        Backup(CodexConfig);
        File.WriteAllText(CodexConfig, UpsertTomlSection(existing, exePath));
    }

    private static void UpsertJson(string path, string exePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        Backup(path);
        File.WriteAllText(path, UpsertJsonText(existing, exePath));
    }

    private static void RemoveJson(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root || root["mcpServers"] is not JsonObject servers || !servers.ContainsKey(ServerName))
        {
            return;
        }

        servers.Remove(ServerName);
        File.WriteAllText(path, root.ToJsonString(Pretty));
    }

    private static bool JsonHasServer(string path, string exePath)
    {
        try
        {
            return File.Exists(path)
                && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                && root["mcpServers"]?[ServerName]?["command"]?.GetValue<string>() is { } cmd
                && string.Equals(cmd, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteSkill(string dir, string markdown)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), markdown);
    }

    private static void Backup(string path)
    {
        if (File.Exists(path))
        {
            File.Copy(path, path + ".todox-backup", overwrite: true);
        }
    }

    private static bool OnPath(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo("where.exe", exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
