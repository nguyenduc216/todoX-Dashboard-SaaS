using System.Net;
using System.Text;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Tests;

internal sealed class CountingNotifier : INotifier
{
    public List<string> Shown { get; } = [];
    public void Show(string title, string text, bool isError = false) => Shown.Add(text);
}

public sealed class ExeFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TodoXServerClient Server(FakeHandler handler) =>
        new(new HttpClient(handler), new LicenseStore(new AgentPaths(_root)), new FakeDevice());

    [Fact]
    public async Task Unreachable_server_notifies_once_and_retries_every_15_seconds()
    {
        var handler = new FakeHandler();
        handler.Replies.Enqueue((HttpStatusCode.OK, null));
        handler.Replies.Enqueue((HttpStatusCode.OK, null));
        handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"serverTime":"2026-11-04T00:00:00Z"}}"""));
        var notifier = new CountingNotifier();
        var delays = new List<TimeSpan>();
        var connectivity = new ServerConnectivity(Server(handler), notifier, (d, _) => { delays.Add(d); return Task.CompletedTask; });
        var status = new List<string>();

        var ok = await connectivity.EnsureConnectedAsync(maxAttempts: 4, status.Add);

        Assert.True(ok);
        Assert.Single(notifier.Shown);
        Assert.Contains("15 giây", notifier.Shown[0]);
        Assert.Equal([TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15)], delays);
        Assert.Equal(2, status.Count);
    }

    [Fact]
    public async Task Bounded_attempts_give_up()
    {
        var handler = new FakeHandler();
        for (var i = 0; i < 3; i++)
        {
            handler.Replies.Enqueue((HttpStatusCode.OK, null));
        }

        var connectivity = new ServerConnectivity(Server(handler), new CountingNotifier(), (_, _) => Task.CompletedTask);

        Assert.False(await connectivity.EnsureConnectedAsync(maxAttempts: 3));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void Signed_plan_maps_ids_to_local_files_and_rejects_forgeries()
    {
        using var signer = new TestSigner();
        var plan = new AutoEditPlan(1080, 1920, 30, [new PlanClip("c0", 1, 4, false)], 0.5, null, null, [], new PlanAudio("voiceover", 1), null, "#FFBE2E",
            new PlanStyle("todox", "TodoX", "#FFBE2E", "#0B0D10", "#FFFFFF", "Arial", "karaoke", "mix", 0.08), 4);
        var body = JsonSerializer.SerializeToUtf8Bytes(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var doc = SignBytes(signer, body);
        var media = new Dictionary<string, string> { ["c0"] = @"D:\v\a.mp4", ["voiceover"] = @"D:\v\voice.mp3" };

        var opened = PlanDocument.Open(doc, signer.Verifier, media);

        Assert.NotNull(opened);
        Assert.Equal(@"D:\v\a.mp4", opened!.Clips[0].Src);
        Assert.Equal(@"D:\v\voice.mp3", opened.Voiceover!.Src);

        using var other = new TestSigner();
        Assert.Null(PlanDocument.Open(SignBytes(other, body), signer.Verifier, media));
        Assert.Throws<InvalidOperationException>(() => PlanDocument.Open(doc, signer.Verifier, new Dictionary<string, string> { ["c0"] = "x" }));
    }

    [Theory]
    [InlineData("h264", 1920, 1080, 0, false)]
    [InlineData("h264", 1080, 1920, 0, false)]
    [InlineData("hevc", 1920, 1080, 0, true)]
    [InlineData("h264", 3840, 2160, 0, true)]
    [InlineData("h264", 1080, 1920, 90, true)]
    [InlineData("prores", 1920, 1080, 0, true)]
    public void Phone_footage_gets_a_proxy_when_needed(string codec, int w, int h, int rotation, bool expected) =>
        Assert.Equal(expected, ClipNormalizer.NeedsProxy(new MediaInfo("x.mp4", 10, w, h, 30, true, true, codec, rotation)));

    [Fact]
    public void Codex_toml_section_is_added_once_and_other_settings_kept()
    {
        const string existing = "model = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"x\"\n\n[mcp_servers.todox]\ncommand = \"old\"\nargs = []\n";
        var updated = ClientRegistrar.UpsertTomlSection(existing, @"C:\Users\a\todox-editvideo.exe");

        Assert.Contains("model = \"gpt-5\"", updated);
        Assert.Contains("[mcp_servers.other]", updated);
        Assert.Single(updated.Split("[mcp_servers.todox]")[1..]);
        Assert.Contains(@"command = ""C:\\Users\\a\\todox-editvideo.exe""", updated);
        Assert.DoesNotContain("\"old\"", updated);
        Assert.DoesNotContain("[mcp_servers.todox]", ClientRegistrar.RemoveTomlSection(updated, "mcp_servers.todox"));
    }

    [Fact]
    public void Claude_json_keeps_other_servers()
    {
        var json = ClientRegistrar.UpsertJsonText("""{"theme":"dark","mcpServers":{"github":{"command":"gh"}}}""", @"C:\t.exe");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("dark", doc.RootElement.GetProperty("theme").GetString());
        Assert.Equal("gh", doc.RootElement.GetProperty("mcpServers").GetProperty("github").GetProperty("command").GetString());
        Assert.Equal(@"C:\t.exe", doc.RootElement.GetProperty("mcpServers").GetProperty("todox").GetProperty("command").GetString());
        Assert.Equal("mcp", doc.RootElement.GetProperty("mcpServers").GetProperty("todox").GetProperty("args")[0].GetString());
        Assert.Contains("todox", ClientRegistrar.UpsertJsonText(null, @"C:\t.exe"));
    }

    [Theory]
    [InlineData("0.5.0", "0.4.0", true)]
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.3.9", "0.4.0", false)]
    [InlineData(null, "0.4.0", false)]
    [InlineData("garbage", "0.4.0", false)]
    public void Version_comparison(string? candidate, string current, bool newer) =>
        Assert.Equal(newer, HealthCheck.IsNewer(candidate, current));

    [Fact]
    public void Release_file_checksum_is_verified()
    {
        Directory.CreateDirectory(_root);
        var f = Path.Combine(_root, "a.zip");
        File.WriteAllText(f, "hello");
        Assert.True(ComponentInstaller.Sha256Matches(f, "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824"));
        Assert.False(ComponentInstaller.Sha256Matches(f, new string('0', 64)));
    }

    [Fact]
    public void Probe_parses_ffprobe_json_including_codec_and_rotation()
    {
        const string json = """
            {"streams":[{"codec_type":"video","codec_name":"hevc","width":1920,"height":1080,"avg_frame_rate":"30000/1001",
              "side_data_list":[{"rotation":-90}]},{"codec_type":"audio"}],
             "format":{"duration":"12.345"}}
            """;

        var info = MediaProbe.Parse(@"C:\x.mp4", json);

        Assert.Equal(12.345, info.DurationSeconds, 3);
        Assert.Equal((1080, 1920), (info.Width, info.Height));
        Assert.Equal(29.97, info.Fps, 2);
        Assert.Equal("hevc", info.VideoCodec);
        Assert.Equal(90, info.Rotation);
        Assert.True(info.HasVideo && info.HasAudio);
    }

    [Fact]
    public void Render_events_are_parsed_and_noise_ignored()
    {
        Assert.Equal(42, RemotionRenderer.ParseEvent("""{"type":"progress","percent":42}""")!.Percent);
        Assert.Equal("boom", RemotionRenderer.ParseEvent("""{"type":"error","message":"boom"}""")!.Message);
        Assert.Null(RemotionRenderer.ParseEvent("Downloading Chrome..."));
        Assert.Null(RemotionRenderer.ParseEvent("{not json"));
    }

    [Fact]
    public void Skill_is_embedded()
    {
        var skill = Installer.ReadSkill();
        Assert.Contains("todox_auto_edit", skill);
        Assert.StartsWith("---", skill);
    }

    [Fact]
    public void Whisper_json_is_parsed_into_timed_segments()
    {
        const string json = """
            {"transcription":[
              {"offsets":{"from":0,"to":2480},"text":" Xin chào, đây là video"},
              {"offsets":{"from":2480,"to":6500},"text":" dựng tự động bằng TodoX."},
              {"offsets":{"from":6500,"to":6600},"text":"  "}]}
            """;

        var t = Transcriber.Parse(json);

        Assert.Equal(2, t.Segments.Count);
        Assert.Equal(2.48, t.Segments[1].Start);
        Assert.Equal(6.5, t.Seconds);
        Assert.Equal("Xin chào, đây là video dựng tự động bằng TodoX.", t.Text);
    }

    [Fact]
    public void Voice_check_passes_a_faithful_read_and_flags_missing_words()
    {
        const string script = "Xin chào, đây là video dựng tự động bằng TodoX EditVideo. Video được xử lý ngay trên máy tính của bạn.";

        var good = Transcriber.Compare(script, "xin chao day la video dung tu dong bang todox editvideo video duoc xu ly ngay tren may tinh cua ban");
        Assert.True(good.Ok);
        Assert.Equal(1.0, good.Score);

        var slip = Transcriber.Compare(script, "Xin chào đây là video dựng tự động bằng tu đu ếch Edit Video. Video được xử lý ngay trên máy tính của bạn.");
        Assert.True(slip.Score > 0.8);

        var bad = Transcriber.Compare(script, "Xin chào đây là video.");
        Assert.False(bad.Ok);
        Assert.Contains("ban", bad.Missing);
        Assert.Contains("dung", bad.Missing);
    }

    [Fact]
    public void Vietnamese_words_are_normalised_without_tone_marks()
    {
        Assert.Equal(["duong", "dien", "bien", "phu"], Transcriber.Words("Đường Điện, Biên-Phủ!"));
    }

    private static string SignBytes(TestSigner signer, byte[] body) => signer.SignRaw(body);
}
