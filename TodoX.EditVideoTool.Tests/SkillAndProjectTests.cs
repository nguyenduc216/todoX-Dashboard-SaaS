using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Tests;

public sealed class SkillAndProjectTests : IDisposable
{
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

    private static string Reply(params (string Slug, string Title, string Markdown)[] skills) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new { skills = skills.Select(s => new { slug = s.Slug, title = s.Title, version = 1, markdown = s.Markdown }) }
    });

    [Fact]
    public async Task Sync_installs_new_skills_reports_updates_and_removes_dropped_ones()
    {
        var (sync, http, notifier) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("todox-bds-ve-ban-do", "TodoX BĐS – vẽ bản đồ", "v1"), ("todox-bds-nha-pho", "TodoX BĐS – nhà phố", "a"))));

        var first = await sync.SyncAsync(notify: true, CancellationToken.None);

        Assert.True(first.Ok);
        Assert.Equal(["TodoX BĐS – vẽ bản đồ", "TodoX BĐS – nhà phố"], first.Added);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(SkillsDir, "todox-bds-ve-ban-do", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(SkillsDir, SkillSync.BaseSkill, "SKILL.md")), "built-in skill is installed too");
        Assert.Single(notifier.Shown);
        Assert.Equal("/api/editvideo/v1/skills", http.Requests[0].Path);

        // Same content again: nothing to report, no notification.
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("todox-bds-ve-ban-do", "TodoX BĐS – vẽ bản đồ", "v1"), ("todox-bds-nha-pho", "TodoX BĐS – nhà phố", "a"))));
        Assert.False((await sync.SyncAsync(notify: true, CancellationToken.None)).Changed);
        Assert.Single(notifier.Shown);

        // Map skill edited, house skill dropped on the server.
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("todox-bds-ve-ban-do", "TodoX BĐS – vẽ bản đồ", "v2"))));
        var third = await sync.SyncAsync(notify: true, CancellationToken.None);

        Assert.Equal(["TodoX BĐS – vẽ bản đồ"], third.Updated);
        Assert.Equal(["TodoX BĐS – nhà phố"], third.Removed);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(SkillsDir, "todox-bds-ve-ban-do", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "todox-bds-nha-pho")));
        Assert.Contains("Đã cập nhật", notifier.Shown[^1]);
    }

    [Fact]
    public async Task Sync_ignores_unsafe_names_and_never_touches_other_skills()
    {
        var (sync, http, _) = Create();
        var foreign = Path.Combine(SkillsDir, "my-own-skill");
        Directory.CreateDirectory(foreign);
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("../evil", "x", "x"), ("other-skill", "y", "y"), ("todox-ok", "ok", "ok"))));

        var result = await sync.SyncAsync(notify: false, CancellationToken.None);

        Assert.Equal(["ok"], result.Added);
        Assert.False(Directory.Exists(Path.Combine(SkillsDir, "other-skill")));
        Assert.False(File.Exists(Path.Combine(_root, "evil", "SKILL.md")));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public async Task Offline_sync_keeps_installed_skills()
    {
        var (sync, http, _) = Create();
        http.Replies.Enqueue((HttpStatusCode.OK, Reply(("todox-a", "A", "a"))));
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
