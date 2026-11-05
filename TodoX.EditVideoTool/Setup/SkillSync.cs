using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Setup;

public sealed record SkillSyncResult(bool Ok, string Message, IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Removed, int Installed)
{
    public bool Changed => Added.Count + Updated.Count + Removed.Count > 0;

    /// <summary>One line for a Windows notification / the window log, e.g. "Skill mới: TodoX BĐS – vẽ bản đồ".</summary>
    public string Describe()
    {
        if (!Ok)
        {
            return Message;
        }

        if (!Changed)
        {
            return $"Skill TodoX đã là bản mới nhất ({Installed} skill).";
        }

        var parts = new List<string>();
        if (Added.Count > 0) parts.Add("Skill mới: " + string.Join(", ", Added));
        if (Updated.Count > 0) parts.Add("Đã cập nhật: " + string.Join(", ", Updated));
        if (Removed.Count > 0) parts.Add("Đã gỡ: " + string.Join(", ", Removed));
        return string.Join(". ", parts) + ". Mở cuộc trò chuyện mới trong Claude / Codex để dùng.";
    }
}

public sealed class SkillState
{
    public DateTimeOffset? LastSync { get; set; }
    public Dictionary<string, SkillStateEntry> Skills { get; set; } = new(StringComparer.Ordinal);
}

public sealed record SkillStateEntry(string Title, int Version, string Hash);

/// <summary>
/// Installs TodoX's skill library (written by TodoX in the dashboard) into Claude / Codex on this machine and keeps
/// it current. Only folders named todox-... that this tool installed are ever touched or removed.
/// </summary>
public sealed partial class SkillSync
{
    public const string BaseSkill = "todox-video";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);

    private readonly AgentPaths _paths;
    private readonly TodoXServerClient _server;
    private readonly INotifier _notifier;
    private readonly IReadOnlyList<string>? _fixedRoots;
    private readonly ClientRegistrar _clients = new();

    /// <param name="skillRoots">Tests only: skill folders to use instead of the assistants found on this machine.</param>
    public SkillSync(AgentPaths paths, TodoXServerClient server, INotifier notifier, IReadOnlyList<string>? skillRoots = null)
    {
        _paths = paths;
        _server = server;
        _notifier = notifier;
        _fixedRoots = skillRoots;
    }

    public SkillState LoadState()
    {
        try
        {
            if (File.Exists(_paths.SkillsStateFile))
            {
                return JsonSerializer.Deserialize<SkillState>(File.ReadAllText(_paths.SkillsStateFile), Json) ?? new SkillState();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new SkillState();
    }

    /// <summary>Checks TodoX now and installs/updates/removes skills. Never throws for network problems.</summary>
    public async Task<SkillSyncResult> SyncAsync(bool notify, CancellationToken ct)
    {
        // Claude Desktop, Claude Code and the window may each start a tool at the same moment; one sync at a time.
        using var mutex = new Mutex(false, @"Local\TodoX.EditVideo.SkillSync");
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                return Fail("Đang có một lần cập nhật skill khác chạy.");
            }

            return await SyncLockedAsync(notify, ct);
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>Background loop for a running tool: sync now, then every N hours (from the window's settings).</summary>
    public async Task RunPeriodicallyAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SkillSyncResult result;
            try
            {
                result = await SyncAsync(notify: true, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Skills are an extra; a problem here must never stop the tool.
                result = Fail(ex.Message);
            }

            var wait = result.Ok ? UserSettings.Load(_paths).SkillSyncInterval : RetryAfterFailure;
            try
            {
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Removes every skill this tool installed (uninstall).</summary>
    public void RemoveAll()
    {
        var state = LoadState();
        foreach (var root in AllRoots())
        {
            foreach (var slug in state.Skills.Keys.Append(BaseSkill))
            {
                DeleteSkill(root, slug);
            }
        }

        try { File.Delete(_paths.SkillsStateFile); } catch (IOException) { }
    }

    private async Task<SkillSyncResult> SyncLockedAsync(bool notify, CancellationToken ct)
    {
        var roots = _fixedRoots ?? _clients.SkillRoots();
        if (roots.Count == 0)
        {
            return Fail("Chưa thấy Claude Desktop / Claude Code / Codex trên máy nên chưa cài skill.");
        }

        var response = await _server.SkillsAsync(ct);
        if (!response.Ok)
        {
            // Not activated / offline: keep whatever is installed, make sure the basic skill is there.
            WriteBaseSkill(roots, serverSkills: []);
            return Fail(response.ErrorCode == "not_activated"
                ? "Chưa kích hoạt license nên chưa tải được bộ skill TodoX."
                : $"Chưa tải được skill từ TodoX: {response.Message ?? response.ErrorCode}");
        }

        var skills = ParseSkills(response.Value);
        var state = LoadState();
        var added = new List<string>();
        var updated = new List<string>();
        var removed = new List<string>();
        var next = new Dictionary<string, SkillStateEntry>(StringComparer.Ordinal);

        foreach (var skill in skills)
        {
            var hash = Hash(skill.Markdown);
            foreach (var root in roots)
            {
                WriteIfChanged(Path.Combine(root, skill.Slug, "SKILL.md"), skill.Markdown);
            }

            if (!state.Skills.TryGetValue(skill.Slug, out var before))
            {
                added.Add(skill.Title);
            }
            else if (before.Hash != hash)
            {
                updated.Add(skill.Title);
            }

            next[skill.Slug] = new SkillStateEntry(skill.Title, skill.Version, hash);
        }

        // The basic skill is never removed: without a TodoX version it falls back to the copy built into the exe.
        foreach (var (slug, entry) in state.Skills.Where(s => !next.ContainsKey(s.Key) && s.Key != BaseSkill))
        {
            foreach (var root in AllRoots())
            {
                DeleteSkill(root, slug);
            }

            removed.Add(entry.Title);
        }

        WriteBaseSkill(roots, skills);
        state.Skills = next;
        state.LastSync = DateTimeOffset.Now;
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SkillsStateFile, JsonSerializer.Serialize(state, Json));

        var result = new SkillSyncResult(true, "ok", added, updated, removed, next.Count + (next.ContainsKey(BaseSkill) ? 0 : 1));
        if (notify && result.Changed)
        {
            _notifier.Show("TodoX EditVideo – skill", result.Describe());
        }

        return result;
    }

    internal sealed record RemoteSkill(string Slug, string Title, int Version, string Markdown);

    internal static IReadOnlyList<RemoteSkill> ParseSkills(JsonElement data)
    {
        var list = new List<RemoteSkill>();
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("skills", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in items.EnumerateArray())
        {
            var slug = item.TryGetProperty("slug", out var s) ? s.GetString() : null;
            var markdown = item.TryGetProperty("markdown", out var m) ? m.GetString() : null;
            // The slug becomes a folder name: only todox-... with safe characters is accepted.
            if (slug is null || markdown is null || !SafeSlug().IsMatch(slug))
            {
                continue;
            }

            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            var version = item.TryGetProperty("version", out var v) && v.TryGetInt32(out var n) ? n : 1;
            list.Add(new RemoteSkill(slug, string.IsNullOrWhiteSpace(title) ? slug : title, version, markdown));
        }

        return list;
    }

    private static void WriteBaseSkill(IEnumerable<string> roots, IReadOnlyList<RemoteSkill> serverSkills)
    {
        if (serverSkills.Any(s => s.Slug == BaseSkill))
        {
            return;
        }

        var markdown = Installer.ReadSkill();
        foreach (var root in roots)
        {
            WriteIfChanged(Path.Combine(root, BaseSkill, "SKILL.md"), markdown);
        }
    }

    private IEnumerable<string> AllRoots()
    {
        if (_fixedRoots is not null)
        {
            return _fixedRoots;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [Path.Combine(home, ".claude", "skills"), Path.Combine(home, ".codex", "skills")];
    }

    private static void WriteIfChanged(string file, string content)
    {
        if (File.Exists(file) && File.ReadAllText(file) == content)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private static void DeleteSkill(string root, string slug)
    {
        var dir = Path.Combine(root, slug);
        if (SafeSlug().IsMatch(slug) && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static SkillSyncResult Fail(string message) => new(false, message, [], [], [], 0);

    [GeneratedRegex("^todox-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SafeSlug();
}
