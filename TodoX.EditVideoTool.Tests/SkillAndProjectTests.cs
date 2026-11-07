using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Tests;

public sealed class SkillAndProjectTests : IDisposable
{
    private const string House = "todox-bds-nha-pho";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private string SkillsDir => Path.Combine(_root, "claude-skills");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (SkillSync Sync, FakeHandler Http, CountingNotifier Notifier) Create()
    {
        var paths = new AgentPaths(_root);
        var store = new LicenseStore(paths);
        store.Save(new StoredLicense("license", "tdxa_test", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(7), "https://todox.test"));
        var http = new FakeHandler();
        var notifier = new CountingNotifier();
        var server = new TodoXServerClient(new HttpClient(http), store, new FakeDevice());
        return (new SkillSync(paths, server, notifier, [SkillsDir]), http, notifier);
    }

    private sealed record V(int No, string Name, string Markdown);

    private static string Reply(params (string Slug, string Title, V[] Versions)[] skills) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            skills = skills.Select(s => new
            {
                slug = s.Slug,
                title = s.Title,
                latestVersion = s.Versions.Max(v => v.No),
                versions = s.Versions.Select(v => new { version = v.No, name = v.Name, markdown = v.Markdown, createdAt = "2026-11-05T00:00:00Z" })
            })
        }
    });

    private string Installed(string slug) => File.ReadAllText(Path.Combine(SkillsDir, slug, "SKILL.md"));

    [Fact]
    public async Task New_versions_are_kept_installed_and_announced()
    {
        var (sync, http, notifier) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "Bản đầu", "md1")]), ("todox-bds-ve-ban-do", "Bản đồ", [new V(1, "a", "map1")]))));

        var first = await sync.SyncAsync(notify: true, CancellationToken.None);

        Assert.True(first.Ok);
        Assert.Equal(["Nhà phố v1", "Bản đồ v1"], first.Added);
        Assert.Equal("md1", Installed(House));
        Assert.True(File.Exists(Path.Combine(SkillsDir, SkillSync.BaseSkill, "SKILL.md")), "built-in skill is installed too");
        Assert.Single(notifier.Shown);
        Assert.Equal("/api/editvideo/v1/skills", http.Requests[0].Path);

        // Same versions again: nothing to report, no notification.
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "Bản đầu", "md1")]), ("todox-bds-ve-ban-do", "Bản đồ", [new V(1, "a", "map1")]))));
        Assert.False((await sync.SyncAsync(notify: true, CancellationToken.None)).Changed);
        Assert.Single(notifier.Shown);

        // v2 published for the house skill, map skill deleted on the server.
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "Bản đầu", "md1"), new V(2, "Thêm bản đồ", "md2")]))));
        var third = await sync.SyncAsync(notify: true, CancellationToken.None);

        Assert.Equal(["Nhà phố v2 \"Thêm bản đồ\""], third.Updated);
        Assert.Equal(["Bản đồ"], third.Removed);
        Assert.Equal("md2", Installed(House));
        Assert.Equal("md1", sync.ReadVersion(House, 1));
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "todox-bds-ve-ban-do")));
        Assert.Contains("Phiên bản mới", notifier.Shown[^1]);
    }

    [Fact]
    public async Task Pinned_version_stays_installed_when_a_newer_one_arrives()
    {
        var (sync, http, _) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "a", "md1"), new V(2, "b", "md2"), new V(3, "c", "md3")]))));
        await sync.SyncAsync(notify: false, CancellationToken.None);
        Assert.Equal("md3", Installed(House));

        Assert.True(sync.Pin(House, 2).Ok);
        Assert.Equal("md2", Installed(House));
        Assert.False(sync.Pin(House, 9).Ok);

        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "a", "md1"), new V(2, "b", "md2"), new V(3, "c", "md3"), new V(4, "d", "md4")]))));
        var result = await sync.SyncAsync(notify: false, CancellationToken.None);

        Assert.Equal("md2", Installed(House));
        Assert.Contains("đang dùng v2", Assert.Single(result.Updated));
        Assert.Equal(4, sync.LoadState().Skills[House].Latest);

        sync.Pin(House, null);
        Assert.Equal("md4", Installed(House));
    }

    [Fact]
    public async Task A_received_version_is_never_overwritten()
    {
        var (sync, http, _) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "a", "original")]))));
        await sync.SyncAsync(notify: false, CancellationToken.None);
        http.Replies.Enqueue((HttpStatusCode.OK, Reply((House, "Nhà phố", [new V(1, "a", "tampered")]))));
        await sync.SyncAsync(notify: false, CancellationToken.None);

        Assert.Equal("original", sync.ReadVersion(House, 1));
        Assert.Equal("original", Installed(House));
    }

    [Fact]
    public async Task Sync_ignores_unsafe_names_and_never_touches_other_skills()
    {
        var (sync, http, _) = Create();
        var foreign = Path.Combine(SkillsDir, "my-own-skill");
        Directory.CreateDirectory(foreign);
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("../evil", "x", [new V(1, "x", "x")]), ("other-skill", "y", [new V(1, "y", "y")]), ("todox-ok", "ok", [new V(1, "ok", "ok")]))));

        var result = await sync.SyncAsync(notify: false, CancellationToken.None);

        Assert.Equal(["ok v1"], result.Added);
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "other-skill")));
        Assert.False(File.Exists(Path.Combine(_root, "evil", "SKILL.md")));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public async Task Offline_sync_keeps_installed_skills()
    {
        var (sync, http, _) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("todox-a", "A", [new V(1, "a", "a")]))));
        await sync.SyncAsync(notify: false, CancellationToken.None);
        http.Replies.Enqueue((HttpStatusCode.OK, null));

        var result = await sync.SyncAsync(notify: false, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.True(File.Exists(Path.Combine(SkillsDir, "todox-a", "SKILL.md")));
    }

    [Fact]
    public void Project_folder_matches_ignoring_case_and_accents_and_creates_when_missing()
    {
        var paths = new AgentPaths(_root);
        new UserSettings { ResourceRoot = Path.Combine(_root, "resources") }.Save(paths);
        var projects = new ProjectFolders(paths);

        var (created, isNew) = projects.Find("An Đông", create: true);
        Assert.True(isNew);
        Assert.Equal("An Đông", created!.Name);
        File.WriteAllText(Path.Combine(created.Path, "mat-tien.jpg"), "x");

        var (found, foundNew) = projects.Find("dự án an dong", create: true);
        Assert.False(foundNew);
        Assert.Equal(created.Path, found!.Path);
        Assert.Equal(1, found.Images);

        Assert.Null(projects.Find("Vinhomes", create: false).Folder);
        Assert.Equal("Dự án a b", ProjectFolders.SafeName("Dự án a/b"));
    }
}
