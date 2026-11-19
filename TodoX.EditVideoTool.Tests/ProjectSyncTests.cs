using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Tests;

/// <summary>Project/video information goes up by itself; a video file only on "Đồng bộ".</summary>
public sealed class ProjectSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly ProjectSync _sync;
    private readonly ProjectFolders _projects;

    public ProjectSyncTests()
    {
        var paths = new AgentPaths(_root);
        var store = new LicenseStore(paths);
        store.Save(new StoredLicense("signed", "tdxa_token", DateTimeOffset.UtcNow.AddHours(20), DateTimeOffset.UtcNow.AddDays(7), "https://dashboard.test"));
        var settings = UserSettings.Load(paths);
        settings.ResourceRoot = Path.Combine(_root, "resources");
        settings.Save(paths);
        _projects = new ProjectFolders(paths);
        _sync = new ProjectSync(new TodoXServerClient(new HttpClient(_handler), store, new FakeDevice()), _projects);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string MakeVideo(string project, string name, int size = 20)
    {
        var folder = _projects.Find(project, create: true).Folder!;
        var file = Path.Combine(folder.Path, ProjectFolders.OutputDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bytes = new byte[size];
        "\0\0\0\u0018ftypmp42"u8.ToArray().CopyTo(bytes, 0);
        File.WriteAllBytes(file, bytes);
        File.WriteAllText(file + ".todox.json", """{"seconds":12.5,"aspect":"9:16","skill":"todox-ban-hang-tiktok","skillVersion":2}""");
        return file;
    }

    [Fact]
    public async Task Information_goes_up_without_the_file_and_the_answer_is_kept()
    {
        var file = MakeVideo("Quinter Central", "todox-1.mp4");
        var id = Guid.NewGuid();
        var clientId = _sync.Videos().Single().ClientId;
        _handler.Replies.Enqueue((HttpStatusCode.OK, JsonSerializer.Serialize(new { success = true, data = new[] { new { clientId, id, status = "local" } } })));

        var (pending, error) = await _sync.SyncInfoAsync();

        Assert.Null(error);
        var video = Assert.Single(pending);
        Assert.Equal(id, video.ServerId);
        Assert.False(video.Synced);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/api/editvideo/v1/projects/sync", request.Path);
        Assert.Contains("Quinter Central", request.Body);
        Assert.Contains("todox-ban-hang-tiktok", request.Body);
        Assert.DoesNotContain("ftyp", request.Body);
        Assert.Contains(id.ToString(), File.ReadAllText(file + ".todox.json"));
        Assert.Equal(clientId, _sync.Videos("quinter central").Single().ClientId); // stable id
    }

    [Fact]
    public async Task Sync_button_uploads_the_file_in_chunks_and_marks_it_synced()
    {
        var file = MakeVideo("Dự án A", "a.mp4", ProjectSync.ChunkBytes + 10);
        var id = Guid.NewGuid();
        var info = File.ReadAllText(file + ".todox.json").TrimEnd('}') + $",\"serverId\":\"{id}\"}}";
        File.WriteAllText(file + ".todox.json", info);
        _handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"done":false}}"""));
        _handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"done":true}}"""));

        var error = await _sync.UploadAsync(_sync.Videos().Single(), null);

        Assert.Null(error);
        Assert.Equal([$"/api/editvideo/v1/projects/videos/{id}/chunk", $"/api/editvideo/v1/projects/videos/{id}/chunk"], _handler.Requests.Select(r => r.Path));
        Assert.True(_sync.Videos().Single().Synced);
    }

    [Fact]
    public async Task Only_the_last_export_of_each_project_is_pending_and_times_go_up_in_utc()
    {
        var old = MakeVideo("Rong biển", "old.mp4");
        File.SetLastWriteTime(old, DateTime.Now.AddHours(-2));
        MakeVideo("Rong biển", "new.mp4");
        MakeVideo("Khác", "k.mp4");
        var rows = _sync.Videos().Select(v => new { clientId = v.ClientId, id = Guid.NewGuid(), status = "local" }).ToArray();
        _handler.Replies.Enqueue((HttpStatusCode.OK, JsonSerializer.Serialize(new { success = true, data = rows })));

        var (pending, error) = await _sync.SyncInfoAsync();

        Assert.Null(error);
        Assert.Equal(["k.mp4", "new.mp4"], pending.Select(v => v.FileName).Order());
        Assert.Contains("+00:00", Assert.Single(_handler.Requests).Body); // the server writes timestamptz in UTC only
        Assert.DoesNotContain("+07:00", _handler.Requests[0].Body);
    }

    [Fact]
    public void Library_folder_is_not_a_project()
    {
        Directory.CreateDirectory(Path.Combine(_projects.Root, LibrarySync.FolderName));
        _projects.Find("Thật", create: true);
        Assert.Equal(["Thật"], _projects.List().Select(p => p.Name));
    }
}
