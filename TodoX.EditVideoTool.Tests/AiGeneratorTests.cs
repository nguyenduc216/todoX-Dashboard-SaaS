using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Tests;

public sealed class AiGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly List<TimeSpan> _delays = [];
    private static readonly Guid TaskId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    public AiGeneratorTests()
    {
        var store = new LicenseStore(new AgentPaths(_root));
        store.Save(new StoredLicense("signed", "tdxa_token", DateTimeOffset.UtcNow.AddHours(20), DateTimeOffset.UtcNow.AddDays(7), "https://dashboard.test"));
        Generator = new AiGenerator(new TodoXServerClient(new HttpClient(_handler), store, new FakeDevice()), (d, _) => { _delays.Add(d); return Task.CompletedTask; });
    }

    private AiGenerator Generator { get; }
    private string Folder => Path.Combine(_root, "out");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string TaskJson(string status, string kind = "image", params string[] outputs) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new { id = TaskId, kind, model = "banana", status, points = 0.1m, charged = status == "succeeded", outputs, createdAt = "2026-11-05T10:00:00Z" }
    });

    [Fact]
    public async Task Local_image_is_uploaded_then_the_result_is_saved_locally_without_the_provider_link()
    {
        var input = Path.Combine(_root, "me.png");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(input, [1, 2, 3]);
        _handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"url":"https://cdn.provider.test/in/me.png"}}"""));
        _handler.Replies.Enqueue((HttpStatusCode.OK, TaskJson("running")));
        _handler.Replies.Enqueue((HttpStatusCode.OK, TaskJson("succeeded", "image", "https://cdn.provider.test/out/abc.png?full=1")));
        _handler.Replies.Enqueue((HttpStatusCode.OK, "PNGDATA"));

        var json = await Generator.CreateAsync(
            new AiCreateInput("image", "banana", "a cat", "9:16", null, null, null, [input], null, null),
            Folder, TimeSpan.FromMinutes(4), null, CancellationToken.None);

        Assert.Equal(["/api/editvideo/v1/ai/uploads", "/api/editvideo/v1/ai/images", $"/api/editvideo/v1/ai/tasks/{TaskId}", "/out/abc.png"],
            _handler.Requests.Select(r => r.Path));
        Assert.Contains(Convert.ToBase64String([1, 2, 3]), _handler.Requests[0].Body);
        Assert.Contains("https://cdn.provider.test/in/me.png", _handler.Requests[1].Body);
        Assert.Equal([AiGenerator.PollInterval], _delays);

        var result = JsonDocument.Parse(json).RootElement;
        var file = result.GetProperty("files")[0].GetString()!;
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(Path.Combine(Folder, "todox-ai-image-0f1e2d3c.png"), file);
        Assert.Equal("PNGDATA", await File.ReadAllTextAsync(file));
        Assert.DoesNotContain("provider.test", json);
    }

    [Fact]
    public async Task Unfinished_video_returns_the_task_id_to_check_later()
    {
        _handler.Replies.Enqueue((HttpStatusCode.OK, TaskJson("running", "video")));

        var json = await Generator.CreateAsync(
            new AiCreateInput("video", "veo", "move", null, null, "flash", "8", null, null, null),
            Folder, TimeSpan.Zero, null, CancellationToken.None);

        var result = JsonDocument.Parse(json).RootElement;
        Assert.Equal(TaskId, result.GetProperty("task").GetGuid());
        Assert.Equal("running", result.GetProperty("status").GetString());
        Assert.Contains("todox_video_status", result.GetProperty("next").GetString());
    }

    [Fact]
    public async Task Status_downloads_once_and_reuses_the_saved_file()
    {
        _handler.Replies.Enqueue((HttpStatusCode.OK, TaskJson("succeeded", "video", "https://cdn.provider.test/v/1.mp4")));
        _handler.Replies.Enqueue((HttpStatusCode.OK, "MP4"));
        _handler.Replies.Enqueue((HttpStatusCode.OK, TaskJson("succeeded", "video", "https://cdn.provider.test/v/1.mp4")));

        await Generator.StatusAsync(TaskId.ToString(), Folder, CancellationToken.None);
        var json = await Generator.StatusAsync(TaskId.ToString(), Folder, CancellationToken.None);

        Assert.Equal(3, _handler.Requests.Count);
        Assert.EndsWith("todox-ai-video-0f1e2d3c.mp4", JsonDocument.Parse(json).RootElement.GetProperty("files")[0].GetString());
    }

    [Fact]
    public async Task Server_refusals_are_relayed_and_bad_inputs_never_reach_the_server()
    {
        _handler.Replies.Enqueue((HttpStatusCode.BadRequest, """{"success":false,"code":"ai_option_required","message":"Cần chọn mode. Lựa chọn mode: flash, pro."}"""));
        var refused = await Generator.CreateAsync(new AiCreateInput("video", "veo", "x", null, null, null, null, null, null, null),
            Folder, TimeSpan.Zero, null, CancellationToken.None);
        Assert.Contains("flash, pro", refused);

        var insecure = await Generator.CreateAsync(new AiCreateInput("image", "m", "x", null, null, null, null, ["http://site/a.png"], null, null),
            Folder, TimeSpan.Zero, null, CancellationToken.None);
        var localVideo = Path.Combine(_root, "clip.mp4");
        await File.WriteAllBytesAsync(localVideo, [0]);
        var video = await Generator.CreateAsync(new AiCreateInput("video", "m", "x", null, null, null, null, null, null, [localVideo]),
            Folder, TimeSpan.Zero, null, CancellationToken.None);

        Assert.Contains("https", insecure);
        Assert.Contains("link https", video);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Task_list_hides_result_links()
    {
        _handler.Replies.Enqueue((HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            success = true,
            data = new { tasks = new[] { new { id = TaskId, kind = "image", model = "banana", status = "succeeded", points = 0.1m, outputs = new[] { "https://cdn.provider.test/x.png" } } } }
        })));

        var json = await Generator.TasksAsync(null, 20, CancellationToken.None);

        Assert.Contains(TaskId.ToString(), json);
        Assert.DoesNotContain("provider.test", json);
    }

    [Theory]
    [InlineData("https://cdn/x/abc.jpg?full=1", "image", 0, "todox-ai-image-0f1e2d3c.jpg")]
    [InlineData("https://cdn/x/abc", "image", 1, "todox-ai-image-0f1e2d3c-2.png")]
    [InlineData("https://cdn/x/abc.php", "video", 0, "todox-ai-video-0f1e2d3c.mp4")]
    public void Output_file_names_are_stable_per_task(string url, string kind, int index, string expected) =>
        Assert.Equal(expected, AiGenerator.FileName(TaskId, kind, index, url));
}
