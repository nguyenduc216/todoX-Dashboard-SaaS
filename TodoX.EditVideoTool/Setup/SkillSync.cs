using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Setup;

public sealed record SkillSyncResult(bool Ok, string Message, IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Removed, int Installed)
{
    public bool Changed => Added.Count + Updated.Count + Removed.Count > 0;

    /// <summary>One line for a Windows notification / the window log, e.g. "Skill mới: TodoX BĐS – nhà phố v1".</summary>
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
        if (Updated.Count > 0) parts.Add("Phiên bản mới: " + string.Join(", ", Updated));
        if (Removed.Count > 0) parts.Add("Đã gỡ: " + string.Join(", ", Removed));
        return string.Join(". ", parts) + ". Mở cuộc trò chuyện mới trong Claude / Codex để dùng.";
    }
}

public sealed class SkillState
{
    public DateTimeOffset? LastSync { get; set; }
    public Dictionary<string, LocalSkill> Skills { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A TodoX skill on this machine: every version ever received, and the one the customer picked.</summary>
public sealed class LocalSkill
{
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<LocalSkillVersion> Versions { get; set; } = [];
    /// <summary>Version the customer chose as default; null = always the newest.</summary>
    public int? Pinned { get; set; }
    /// <summary>Version currently installed into Claude / Codex.</summary>
    public int? Installed { get; set; }

    public int? Latest => Versions.Count == 0 ? null : Versions.Max(v => v.Version);

    /// <summary>The version to install: the pinned one while it exists here, otherwise the newest.</summary>
    public int? Effective => Pinned is { } p && Versions.Any(v => v.Version == p) ? p : Latest;

    public LocalSkillVersion? Find(int version) => Versions.FirstOrDefault(v => v.Version == version);
}

public sealed record LocalSkillVersion(int Version, string Name, string? Notes, DateTimeOffset? CreatedAt);

/// <summary>
/// Installs TodoX's skill library (written in the dashboard, published as numbered versions) into Claude / Codex on
/// this machine. Every version received is kept in skills\&lt;slug&gt;\v&lt;N&gt;.md and never overwritten, so the customer can
/// pick the version they like as the default per skill. Only folders named todox-... that this tool installed are
/// ever touched or removed.
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

    private string CacheDir => Path.Combine(_paths.Root, "skills");

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

    /// <summary>Text of one stored version (works offline), or null when this machine does not have it.</summary>
    public string? ReadVersion(string slug, int version)
    {
        var file = VersionFile(slug, version);
        return SafeSlug().IsMatch(slug) && File.Exists(file) ? File.ReadAllText(file) : null;
    }

    /// <summary>
    /// Makes <paramref name="version"/> the default of a skill (null = always the newest) and installs it right away.
    /// Returns a message for the user.
    /// </summary>
    public (bool Ok, string Message) Pin(string slug, int? version)
    {
        using var guard = Lock();
        if (!guard.Owned)
        {
            return (false, "Đang có một lần cập nhật skill khác chạy, thử lại sau ít giây.");
        }

        var state = LoadState();
        if (!state.Skills.TryGetValue(slug, out var skill))
        {
            return (false, $"Không có skill \"{slug}\" trên máy này.");
        }

        if (version is { } v && skill.Find(v) is null)
        {
            return (false, $"Skill \"{skill.Title}\" không có phiên bản v{v}. Có: {string.Join(", ", skill.Versions.Select(x => "v" + x.Version))}.");
        }

        skill.Pinned = version;
        Install(slug, skill, Roots());
        SaveState(state);
        var effective = skill.Find(skill.Effective!.Value)!;
        return (true, version is null
            ? $"{skill.Title}: luôn dùng bản mới nhất (hiện là v{effective.Version} – {effective.Name})."
            : $"{skill.Title}: đã đặt v{effective.Version} – {effective.Name} làm mặc định.");
    }

    /// <summary>Checks TodoX now and installs/updates/removes skills. Never throws for network problems.</summary>
    public async Task<SkillSyncResult> SyncAsync(bool notify, CancellationToken ct)
    {
        // Claude Desktop, Claude Code and the window may each start a tool at the same moment; one sync at a time.
        using var guard = Lock();
        if (!guard.Owned)
        {
            return Fail("Đang có một lần cập nhật skill khác chạy.");
        }

        return await SyncLockedAsync(notify, ct);
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
        try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, recursive: true); } catch (IOException) { }
    }

    private async Task<SkillSyncResult> SyncLockedAsync(bool notify, CancellationToken ct)
    {
        var roots = Roots();
        if (roots.Count == 0)
        {
            return Fail("Chưa thấy Claude Desktop / Claude Code / Codex trên máy nên chưa cài skill.");
        }

        var response = await _server.SkillsAsync(ct);
        if (!response.Ok)
        {
            // Not activated / offline: keep whatever is installed, make sure the basic skill is there.
            WriteBaseSkill(roots, serverSlugs: new HashSet<string>());
            return Fail(response.ErrorCode == "not_activated"
                ? "Chưa kích hoạt license nên chưa tải được bộ skill TodoX."
                : $"Chưa tải được skill từ TodoX: {response.Message ?? response.ErrorCode}");
        }

        var remote = ParseSkills(response.Value);
        var state = LoadState();
        var added = new List<string>();
        var updated = new List<string>();
        var removed = new List<string>();

        foreach (var skill in remote)
        {
            var isNew = !state.Skills.TryGetValue(skill.Slug, out var local);
            local ??= new LocalSkill();
            local.Title = skill.Title;
            local.Category = skill.Category;
            var fresh = new List<RemoteVersion>();
            foreach (var version in skill.Versions)
            {
                // A version is frozen on the server; the first copy received is kept as is.
                var file = VersionFile(skill.Slug, version.Version);
                if (!File.Exists(file))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, version.Markdown);
                }

                if (local.Find(version.Version) is null)
                {
                    local.Versions.Add(new LocalSkillVersion(version.Version, version.Name, version.Notes, version.CreatedAt));
                    fresh.Add(version);
                }
            }

            local.Versions.Sort((a, b) => a.Version.CompareTo(b.Version));
            state.Skills[skill.Slug] = local;
            Install(skill.Slug, local, roots);

            var newest = fresh.MaxBy(v => v.Version);
            if (isNew)
            {
                added.Add($"{skill.Title} v{local.Latest}");
            }
            else if (newest is not null)
            {
                updated.Add(local.Pinned is { } pinned && pinned != newest.Version
                    ? $"{skill.Title} v{newest.Version} \"{newest.Name}\" (bạn đang dùng v{local.Effective})"
                    : $"{skill.Title} v{newest.Version} \"{newest.Name}\"");
            }
        }

        // The basic skill is never removed: without a TodoX version it falls back to the copy built into the exe.
        var serverSlugs = remote.Select(s => s.Slug).ToHashSet(StringComparer.Ordinal);
        foreach (var (slug, local) in state.Skills.Where(s => !serverSlugs.Contains(s.Key)).ToList())
        {
            if (slug != BaseSkill)
            {
                foreach (var root in AllRoots())
                {
                    DeleteSkill(root, slug);
                }
            }

            state.Skills.Remove(slug);
            removed.Add(local.Title);
        }

        WriteBaseSkill(roots, serverSlugs);
        state.LastSync = DateTimeOffset.Now;
        SaveState(state);

        var result = new SkillSyncResult(true, "ok", added, updated, removed, state.Skills.Count + (serverSlugs.Contains(BaseSkill) ? 0 : 1));
        if (notify && result.Changed)
        {
            _notifier.Show("TodoX EditVideo – skill", result.Describe());
        }

        return result;
    }

    /// <summary>Writes the effective version of a skill into every assistant's skill folder.</summary>
    private void Install(string slug, LocalSkill skill, IEnumerable<string> roots)
    {
        if (skill.Effective is not { } version || ReadVersion(slug, version) is not { } markdown)
        {
            return;
        }

        foreach (var root in roots)
        {
            WriteIfChanged(Path.Combine(root, slug, "SKILL.md"), markdown);
        }

        skill.Installed = version;
    }

    internal sealed record RemoteVersion(int Version, string Name, string? Notes, DateTimeOffset? CreatedAt, string Markdown);

    internal sealed record RemoteSkill(string Slug, string Title, string? Category, IReadOnlyList<RemoteVersion> Versions);

    internal static IReadOnlyList<RemoteSkill> ParseSkills(JsonElement data)
    {
        var list = new List<RemoteSkill>();
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("skills", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in items.EnumerateArray())
        {
            var slug = Str(item, "slug");
            // The slug becomes a folder name: only todox-... with safe characters is accepted.
            if (slug is null || !SafeSlug().IsMatch(slug))
            {
                continue;
            }

            var versions = new List<RemoteVersion>();
            if (item.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in vs.EnumerateArray())
                {
                    if (v.TryGetProperty("version", out var n) && n.TryGetInt32(out var no) && no > 0 && Str(v, "markdown") is { } md)
                    {
                        versions.Add(new RemoteVersion(no, Str(v, "name") ?? $"v{no}", Str(v, "notes"),
                            v.TryGetProperty("createdAt", out var c) && c.TryGetDateTimeOffset(out var at) ? at : null, md));
                    }
                }
            }

            if (versions.Count == 0)
            {
                continue;
            }

            var title = Str(item, "title");
            list.Add(new RemoteSkill(slug, string.IsNullOrWhiteSpace(title) ? slug : title, Str(item, "category"), versions));
        }

        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private string VersionFile(string slug, int version) => Path.Combine(CacheDir, slug, $"v{version}.md");

    private void SaveState(SkillState state)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SkillsStateFile, JsonSerializer.Serialize(state, Json));
    }

    private IReadOnlyList<string> Roots() => _fixedRoots ?? _clients.SkillRoots();

    private static void WriteBaseSkill(IEnumerable<string> roots, IReadOnlySet<string> serverSlugs)
    {
        if (serverSlugs.Contains(BaseSkill))
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

    private static SkillSyncResult Fail(string message) => new(false, message, [], [], [], 0);

    [GeneratedRegex("^todox-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SafeSlug();

    private static LockGuard Lock() => new(new Semaphore(1, 1, @"Local\TodoX.EditVideo.SkillSync"));

    /// <summary>
    /// Cross-process lock shared by every running copy of the tool. A semaphore, not a mutex: the sync awaits while
    /// holding it and may resume on another thread.
    /// </summary>
    private sealed class LockGuard : IDisposable
    {
        private readonly Semaphore _semaphore;

        public LockGuard(Semaphore semaphore)
        {
            _semaphore = semaphore;
            Owned = semaphore.WaitOne(TimeSpan.FromSeconds(30));
        }

        public bool Owned { get; }

        public void Dispose()
        {
            if (Owned)
            {
                _semaphore.Release();
            }

            _semaphore.Dispose();
        }
    }
}
