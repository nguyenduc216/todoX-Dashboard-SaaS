using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Tests;

/// <summary>Paid commands run only after the person accepts TodoX's price in the confirmation window.</summary>
public sealed class PriceConsentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly FakeConsent _consent = new();
    private readonly TodoXServerClient _client;

    private sealed class FakeConsent : IPriceConsent
    {
        public ConsentAnswer Answer { get; set; } = ConsentAnswer.Accepted;
        public List<PriceQuote> Asked { get; } = [];
        public string Method => "test_dialog";

        public Task<ConsentAnswer> AskAsync(PriceQuote quote, CancellationToken ct)
        {
            Asked.Add(quote);
            return Task.FromResult(Answer);
        }
    }

    public PriceConsentTests()
    {
        var store = new LicenseStore(new AgentPaths(_root));
        store.Save(new StoredLicense("signed", "tdxa_token", DateTimeOffset.UtcNow.AddHours(20), DateTimeOffset.UtcNow.AddDays(7), "https://dashboard.test"));
        _client = new TodoXServerClient(new HttpClient(_handler), store, new FakeDevice(), _consent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string Quote = """
        {"success":false,"code":"confirm_required","message":"Cần xác nhận giá",
         "detail":{"quoteId":"q1","kind":"video","model":"veo_omni","summary":"Video AI veo_omni · 720p · 8 giây","points":4.8,"vnd":4800,"balance":20,"expiresInSeconds":600}}
        """;

    [Fact]
    public async Task Accepted_price_is_sent_back_with_the_same_request()
    {
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));
        _handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"id":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","status":"running"}}"""));

        var result = await _client.AiCreateAsync("video", new { model = "veo_omni", prompt = "a cat", duration = "8", clientKey = "exe-1" });

        Assert.True(result.Ok);
        var asked = Assert.Single(_consent.Asked);
        Assert.Equal(4.8m, asked.Points);
        Assert.Contains("4.800", asked.Text.Replace(',', '.'));
        Assert.Equal(2, _handler.Requests.Count);
        var first = JsonDocument.Parse(_handler.Requests[0].Body!).RootElement;
        var second = JsonDocument.Parse(_handler.Requests[1].Body!).RootElement;
        Assert.False(first.TryGetProperty("confirmQuoteId", out _));
        Assert.Equal("q1", second.GetProperty("confirmQuoteId").GetString());
        Assert.Equal("test_dialog", second.GetProperty("consentMethod").GetString());
        Assert.Equal(first.GetProperty("clientKey").GetString(), second.GetProperty("clientKey").GetString());
        Assert.Equal(first.GetProperty("prompt").GetString(), second.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task Refused_price_runs_nothing()
    {
        _consent.Answer = ConsentAnswer.Declined;
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));

        var result = await _client.SubmitTtsAsync("Xin chào", null, null);

        Assert.Equal(TodoXServerClient.PriceDeclined, result.ErrorCode);
        Assert.Contains("bấm Hủy", result.Message);
        Assert.Contains("không trừ điểm", result.Message);
        Assert.Single(_handler.Requests);
    }

    private sealed class FakeWindow : IAppWindow
    {
        public bool IsRunning { get; set; }
        public bool AutoOpen { get; set; }
        public int Opened { get; private set; }

        public Task<bool> OpenAsync(CancellationToken ct)
        {
            Opened++;
            IsRunning = true;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Closed_app_without_permission_asks_the_user_first()
    {
        var window = new FakeWindow();
        _client.Window = window;
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));

        var result = await _client.SubmitTtsAsync("Xin chào", null, null);

        Assert.Equal(TodoXServerClient.AppNotRunning, result.ErrorCode);
        Assert.Contains("todox_app", result.Message);
        Assert.Empty(_consent.Asked);
        Assert.Equal(0, window.Opened);
        Assert.DoesNotContain("4.8", result.Message!.Replace(',', '.'));
    }

    [Fact]
    public async Task Closed_app_with_permission_is_opened_then_asked()
    {
        var window = new FakeWindow { AutoOpen = true };
        _client.Window = window;
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));
        _handler.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":{"id":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","status":"running"}}"""));

        var result = await _client.AiCreateAsync("video", new { model = "veo_omni", prompt = "a cat" });

        Assert.True(result.Ok);
        Assert.Equal(1, window.Opened);
        Assert.Single(_consent.Asked);
    }

    [Fact]
    public async Task Without_anyone_to_ask_nothing_is_agreed()
    {
        _client.Consent = null;
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));

        var result = await _client.AiMusicAsync(new { name = "Bài test", styles = "pop" });

        Assert.Equal(TodoXServerClient.PriceDeclined, result.ErrorCode);
        Assert.Single(_handler.Requests);
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<string> Shown { get; } = [];
        public void Show(string title, string text, bool isError = false) => Shown.Add(title + ": " + text);
    }

    private const string Ok = """{"success":true,"data":{"id":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","status":"running"}}""";

    [Fact]
    public async Task Allowing_the_whole_project_stops_asking_and_keeps_its_history()
    {
        var dir = Path.Combine(_root, "Du an A");
        Directory.CreateDirectory(dir);
        var notifier = new FakeNotifier();
        _client.Notifier = notifier;
        _consent.Answer = ConsentAnswer.AcceptedForProject;
        try
        {
            Projects.PaidScope.Set("Dự án A", dir);
            _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));
            _handler.Replies.Enqueue((HttpStatusCode.OK, Ok));
            Assert.True((await _client.AiCreateAsync("video", new { model = "veo_omni", prompt = "a" })).Ok);
            Assert.Equal("Dự án A", Assert.Single(_consent.Asked).Project);
            Assert.True(Projects.ProjectHistory.IsGranted(dir));

            // Same project: no window, only a notice of the points; the server still gets an accepted quote.
            _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));
            _handler.Replies.Enqueue((HttpStatusCode.OK, Ok));
            Assert.True((await _client.SubmitTtsAsync("Xin chào", null, null)).Ok);
            Assert.Single(_consent.Asked);
            Assert.Contains("4,8 điểm", Assert.Single(notifier.Shown));
            var last = JsonDocument.Parse(_handler.Requests[^1].Body!).RootElement;
            Assert.Equal("project_grant", last.GetProperty("consentMethod").GetString());

            var history = Projects.ProjectHistory.Read(dir);
            Assert.Equal(2, history.Count);
            Assert.Equal(Projects.ProjectHistory.Auto, history[0].Answer);
            Assert.Equal(Projects.ProjectHistory.AcceptedForProject, history[1].Answer);
            Assert.Equal(4.8m, history[0].Points);
            Assert.Equal(20m - 4.8m, history[0].Balance); // balance left after the deduction

            // Every assistant command of the project is kept too, with the files it produced (opened from the history).
            var video = Path.Combine(dir, "ket-qua.mp4");
            File.WriteAllText(video, "");
            var box = Projects.PaidScope.Begin();
            box.Name = "Dự án A";
            box.Dir = dir;
            Projects.CommandHistory.Record(null, box, "todox_export", new Dictionary<string, JsonElement> { ["edit"] = JsonDocument.Parse("\"e1\"").RootElement },
                false, JsonSerializer.Serialize(new { output = video, seconds = 30 }));
            var command = Projects.ProjectHistory.Read(dir)[0];
            Assert.Equal("todox_export", command.Kind);
            Assert.Null(command.Points);
            Assert.Equal(video, Assert.Single(command.Files!));
        }
        finally
        {
            Projects.ProjectHistory.Revoke(dir);
        }

        Assert.False(Projects.ProjectHistory.IsGranted(dir));
    }
}
