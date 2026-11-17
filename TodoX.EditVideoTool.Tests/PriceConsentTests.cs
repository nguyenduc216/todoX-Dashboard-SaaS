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

    [Fact]
    public async Task Without_anyone_to_ask_nothing_is_agreed()
    {
        _client.Consent = null;
        _handler.Replies.Enqueue((HttpStatusCode.Conflict, Quote));

        var result = await _client.AiMusicAsync(new { name = "Bài test", styles = "pop" });

        Assert.Equal(TodoXServerClient.PriceDeclined, result.ErrorCode);
        Assert.Single(_handler.Requests);
    }
}
