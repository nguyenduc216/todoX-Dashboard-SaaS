using System.Text;
using System.Text.Json;

namespace TodoX.EditVideoTool.Projects;

/// <summary>
/// One line of a project's history: a command the assistant ran (Kind = tool name, no points) or a paid step
/// (Kind = video/image/music/voice) with the points it costs and the balance left after it.
/// </summary>
public sealed record HistoryEntry(DateTimeOffset At, string Kind, string Summary, decimal? Points, decimal? Vnd, decimal? Balance, string Answer, string? QuoteId,
    IReadOnlyList<string>? Files = null)
{
    public bool IsPaid => Points is not null;

    public bool Charged => Answer is ProjectHistory.Accepted or ProjectHistory.AcceptedForProject or ProjectHistory.Auto;
}

/// <summary>
/// Per project: the history of every command the assistant ran and every paid command (".todox-lich-su.jsonl" in the project folder) and whether the
/// person allowed all paid commands of this project without asking again (kept in the app's own data).
/// The history is for the person and the TodoX window only; the AI assistant is never given the points.
/// </summary>
public static class ProjectHistory
{
    public const string HistoryFile = ".todox-lich-su.jsonl";

    /// <summary>Answers written to the history.</summary>
    public const string Accepted = "đồng ý", AcceptedForProject = "đồng ý cả dự án", Auto = "tự đồng ý (dự án đã cho phép)",
        Declined = "hủy", NoAnswer = "không trả lời", Unavailable = "không hiện được cửa sổ";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object Gate = new();

    /// <summary>
    /// The "allow the whole project" choice lives in this app's own data (not the project folder), protected with
    /// Windows DPAPI for this Windows user, so only a click in TodoX's window can create it.
    /// </summary>
    private static string GrantPath(string projectDir)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Normal(projectDir))))[..24];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoX", "EditVideoTool", "grants", key + ".bin");
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("todox-editvideo-project-grant-v1");

    private static string Normal(string projectDir) => Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    private static string GrantText(string projectDir) => "grant|" + Normal(projectDir);

    public static bool IsGranted(string projectDir)
    {
        try
        {
            var path = GrantPath(projectDir);
            if (!File.Exists(path))
            {
                return false;
            }

            var text = Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser));
            return text.StartsWith(GrantText(projectDir) + "|", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return false; // anything unreadable means: ask again
        }
    }

    public static void Grant(string projectDir, string by)
    {
        var path = GrantPath(projectDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes($"{GrantText(projectDir)}|{by}|{DateTimeOffset.Now:O}"), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }

    public static void Revoke(string projectDir)
    {
        var path = GrantPath(projectDir);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void Add(string projectDir, HistoryEntry entry)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(projectDir);
                File.AppendAllText(Path.Combine(projectDir, HistoryFile), JsonSerializer.Serialize(entry, Json) + "\n", Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a record for the person; a locked file must not stop the paid command itself.
        }
    }

    /// <summary>Newest first.</summary>
    public static IReadOnlyList<HistoryEntry> Read(string projectDir)
    {
        var path = Path.Combine(projectDir, HistoryFile);
        if (!File.Exists(path))
        {
            return [];
        }

        var list = new List<HistoryEntry>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<HistoryEntry>(line, Json) is { } e)
                {
                    list.Add(e);
                }
            }
            catch (JsonException)
            {
            }
        }

        list.Reverse();
        return list;
    }
}

/// <summary>
/// The project the current tool call works in. The MCP call filter opens a box per call (<see cref="Begin"/>); the
/// tool fills it when it knows the project, and paid server calls and the filter read it.
/// </summary>
public static class PaidScope
{
    public sealed class Box
    {
        public string? Name { get; set; }
        public string? Dir { get; set; }
    }

    private static readonly AsyncLocal<Box?> Current = new();

    public static (string Name, string Dir)? Project => Current.Value is { Name: { } n, Dir: { } d } ? (n, d) : null;

    public static Box Begin()
    {
        var box = new Box();
        Current.Value = box;
        return box;
    }

    public static void Set(string? name, string? dir)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        if (Current.Value is { } box)
        {
            box.Name = name.Trim();
            box.Dir = dir;
        }
        else
        {
            Current.Value = new Box { Name = name.Trim(), Dir = dir };
        }
    }
}

/// <summary>Writes each assistant command (tool call) to the history of the project it worked in.</summary>
public static class CommandHistory
{
    public static void Record(IServiceProvider? services, PaidScope.Box scope, string? tool,
        IDictionary<string, System.Text.Json.JsonElement>? args, bool isError, string? resultText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tool))
            {
                return;
            }

            if (scope.Dir is null && args is not null && args.TryGetValue("project", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                && services?.GetService(typeof(ProjectFolders)) is ProjectFolders folders && folders.Find(p.GetString() ?? "", create: false).Folder is { } f)
            {
                scope.Name = f.Name;
                scope.Dir = f.Path;
            }

            if (scope.Dir is null)
            {
                return; // not tied to a project (e.g. listing projects)
            }

            var detail = string.Join(" · ", (args ?? new Dictionary<string, System.Text.Json.JsonElement>())
                .Where(a => a.Key is not "project" && a.Value.ValueKind is not System.Text.Json.JsonValueKind.Null)
                .Select(a => $"{a.Key}: {Short(a.Value.ValueKind == System.Text.Json.JsonValueKind.String ? a.Value.GetString() : a.Value.GetRawText(), 70)}"));
            var answer = isError ? "lỗi: " + Short(resultText, 120) : "xong";
            var files = isError ? [] : MediaFiles(resultText);
            ProjectHistory.Add(scope.Dir, new HistoryEntry(DateTimeOffset.Now, tool, Short(detail, 300), null, null, null, answer, null,
                files.Count > 0 ? files : null));
        }
        catch (Exception)
        {
            // History never breaks a command.
        }
    }

    private static readonly string[] MediaExtensions = [".mp4", ".mov", ".webm", ".png", ".jpg", ".jpeg", ".webp", ".mp3", ".wav", ".m4a", ".aac"];

    /// <summary>Image / video / voice / music files on this computer named in a tool's answer (they open from the history).</summary>
    internal static IReadOnlyList<string> MediaFiles(string? text)
    {
        var found = new List<string>();
        void Consider(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 400 || !Path.IsPathFullyQualified(value)
                || !MediaExtensions.Contains(Path.GetExtension(value).ToLowerInvariant()) || found.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(value))
            {
                found.Add(value);
            }
        }

        void Walk(System.Text.Json.JsonElement e)
        {
            switch (e.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value);
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var i in e.EnumerateArray()) Walk(i);
                    break;
                case System.Text.Json.JsonValueKind.String:
                    Consider(e.GetString());
                    break;
            }
        }

        var t = text?.Trim() ?? "";
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(t);
                Walk(doc.RootElement);
                return found;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t,
                     @"[A-Za-z]:\\[^""\r\n<>|*?]+?\.(mp4|mov|webm|png|jpe?g|webp|mp3|wav|m4a|aac)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Consider(m.Value);
        }

        return found;
    }

    private static string Short(string? text, int max)
    {
        var t = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }
}
