using System.Net;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Tests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _http = new();
    private readonly CountingNotifier _notifier = new();
    private readonly Updater _updater;

    public UpdaterTests()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var server = new TodoXServerClient(new HttpClient(_http), new LicenseStore(paths), new FakeDevice());
        _updater = new Updater(server, paths, _notifier);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Manifest(string version, string minSupported, string notes = "") =>
        _http.Replies.Enqueue((HttpStatusCode.OK,
            $$$"""{"success":true,"data":{"version":"{{{version}}}","minSupported":"{{{minSupported}}}","publishedAt":"2026-11-11T00:00:00Z","notes":"{{{notes}}}","files":[]}}"""));

    [Fact]
    public async Task Newer_version_gives_the_assistant_one_notice_and_does_not_block_tools()
    {
        Manifest("99.0.0", "0.4.0", "99.0.0: Thông báo cập nhật");

        var info = await _updater.CheckAsync(CancellationToken.None);

        Assert.True(info.UpdateAvailable);
        Assert.False(info.BelowMinimum);
        Assert.Null(_updater.MinimumProblem());
        var notice = _updater.TakeAssistantNotice();
        Assert.NotNull(notice);
        Assert.Contains("99.0.0 (Thông báo cập nhật)", notice);
        Assert.Contains(Updater.RestartHint, notice);
        Assert.Null(_updater.TakeAssistantNotice()); // once per session
    }

    [Fact]
    public async Task Version_below_minimum_stops_tools_with_a_restart_message()
    {
        Manifest("99.0.0", "98.0.0");

        await _updater.CheckAsync(CancellationToken.None);

        var problem = _updater.MinimumProblem();
        Assert.NotNull(problem);
        Assert.Contains(StatusService.Version, problem);
        Assert.Contains("98.0.0", problem);
        Assert.Null(_updater.TakeAssistantNotice()); // the stop message already says it
    }

    [Fact]
    public async Task Same_version_reports_nothing()
    {
        Manifest(StatusService.Version, "0.4.0");

        var info = await _updater.CheckAsync(CancellationToken.None);

        Assert.False(info.UpdateAvailable);
        Assert.Null(_updater.TakeAssistantNotice());
        Assert.Null(_updater.MinimumProblem());
    }

    [Fact]
    public async Task Offline_check_never_blocks()
    {
        _http.Replies.Enqueue((HttpStatusCode.OK, null));

        var info = await _updater.CheckAsync(CancellationToken.None);

        Assert.Null(info.Latest);
        Assert.Null(_updater.MinimumProblem());
    }

    [Fact]
    public async Task Not_installed_copy_never_stages_or_notifies()
    {
        Manifest("99.0.0", "0.4.0");

        Assert.Null(await _updater.CheckAndStageAsync(CancellationToken.None, notify: true));
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public void Toast_drops_the_version_prefix_and_keeps_the_restart_hint_under_the_windows_limit()
    {
        var manifest = new ReleaseManifest("1.2.0", "0.4.0", DateTimeOffset.UtcNow, "1.2.0: " + new string('x', 400), []);

        var text = Updater.ToastText(manifest);

        Assert.StartsWith("xxx", text);
        Assert.EndsWith(Updater.RestartHint, text);
        Assert.True(text.Length <= 250, $"length {text.Length}");
    }
}
