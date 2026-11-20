using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Tests;

public class TokenKeeperTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "reup-token-" + Guid.NewGuid().ToString("N"));
    private readonly TokenHistory _history;
    private readonly FakeTokens _fb = new();

    public TokenKeeperTests()
    {
        Directory.CreateDirectory(_dir);
        _history = TokenHistory.Load(Path.Combine(_dir, "token-history.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private TokenKeeper Keeper() => new(_fb, _history, () => Now);

    private static FacebookPage Page(string id, string token) =>
        new() { PageId = id, Name = "Page " + id, ProtectedToken = SecretProtector.Protect(token) };

    [Fact]
    public async Task ShortTokenIsExchangedAndSavedWhenAppCredentialsExist()
    {
        var settings = new AppSettings { FacebookApp = { AppId = "123", ProtectedSecret = SecretProtector.Protect("sec") } };
        _fb.Tokens["short"] = new TokenDebugInfo(true, "123", "USER", Now.AddHours(1), null, null);
        _fb.Tokens["long"] = new TokenDebugInfo(true, "123", "USER", Now.AddDays(60), null, null);
        _fb.Exchange["short"] = new ExchangedToken("long", Now.AddDays(60));

        var (token, note) = await Keeper().PrepareUserTokenAsync(settings, "short", CancellationToken.None);

        Assert.Equal("long", token);
        Assert.Null(note);
        Assert.Equal("long", SecretProtector.Unprotect(settings.FacebookApp.ProtectedUserToken));
        Assert.Contains(_history.Snapshot(), e => e.Kind == TokenEventKind.Exchanged);
        Assert.DoesNotContain(_history.Snapshot(), e => e.Detail.Contains("long") || e.Detail.Contains("sec"));
    }

    [Fact]
    public async Task ShortTokenWithoutAppWarnsAndIsNotSaved()
    {
        var settings = new AppSettings();
        _fb.Tokens["short"] = new TokenDebugInfo(true, "999", "USER", Now.AddHours(1), null, null);

        var (token, note) = await Keeper().PrepareUserTokenAsync(settings, "short", CancellationToken.None);

        Assert.Equal("short", token);
        Assert.Contains("NGẮN HẠN", note);
        Assert.Equal("", settings.FacebookApp.ProtectedUserToken);
    }

    [Fact]
    public async Task ExpiringPageTokenIsRenewedWithSavedUserToken()
    {
        var page = Page("p1", "old");
        var settings = new AppSettings { FacebookPages = [page], FacebookApp = { ProtectedUserToken = SecretProtector.Protect("user") } };
        _fb.Tokens["old"] = new TokenDebugInfo(true, "1", "PAGE", Now.AddDays(2), null, null);
        _fb.Tokens["user"] = new TokenDebugInfo(true, "1", "USER", Now.AddDays(50), null, null);
        _fb.Tokens["new"] = new TokenDebugInfo(true, "1", "PAGE", null, null, null);
        _fb.Pages = [new FacebookPageInfo("p1", "Page p1", "new")];

        var report = await Keeper().CheckAsync(settings, null, new Progress<string>(), CancellationToken.None);

        Assert.Equal(1, report.Renewed);
        Assert.Empty(report.Problems);
        Assert.Equal("new", SecretProtector.Unprotect(page.ProtectedToken));
        Assert.Null(page.TokenExpiresAt);
        Assert.NotNull(page.TokenCheckedAt);
        Assert.Equal(Now, settings.FacebookApp.LastAutoCheckAt);
        Assert.Contains(_history.Snapshot(), e => e.Kind == TokenEventKind.Renewed && e.PageId == "p1" && e.NeverExpires);
    }

    [Fact]
    public async Task ExpiredPageWithoutUserTokenIsReported()
    {
        var page = Page("p1", "old");
        var settings = new AppSettings { FacebookPages = [page] };
        _fb.Tokens["old"] = new TokenDebugInfo(false, "1", "PAGE", null, null, "Session has expired");

        var report = await Keeper().CheckAsync(settings, null, new Progress<string>(), CancellationToken.None);

        Assert.True(page.TokenExpired);
        Assert.Single(report.Problems);
        Assert.Contains(_history.Snapshot(), e => e.Kind == TokenEventKind.Expired && e.PageId == "p1");
    }

    [Fact]
    public async Task UserTokenIsReExchangedBeforeItExpires()
    {
        var page = Page("p1", "old");
        var settings = new AppSettings
        {
            FacebookPages = [page],
            FacebookApp = { AppId = "123", ProtectedSecret = SecretProtector.Protect("sec"), ProtectedUserToken = SecretProtector.Protect("user") }
        };
        _fb.Tokens["old"] = new TokenDebugInfo(false, "1", "PAGE", null, null, "expired");
        _fb.Tokens["user"] = new TokenDebugInfo(true, "123", "USER", Now.AddDays(5), null, null);
        _fb.Tokens["user2"] = new TokenDebugInfo(true, "123", "USER", Now.AddDays(60), null, null);
        _fb.Tokens["new"] = new TokenDebugInfo(true, "123", "PAGE", null, null, null);
        _fb.Exchange["user"] = new ExchangedToken("user2", Now.AddDays(60));
        _fb.Pages = [new FacebookPageInfo("p1", "Page p1", "new")];

        var report = await Keeper().CheckAsync(settings, null, new Progress<string>(), CancellationToken.None);

        Assert.Equal("user2", SecretProtector.Unprotect(settings.FacebookApp.ProtectedUserToken));
        Assert.Equal("user2", _fb.ListedWith);
        Assert.False(page.TokenExpired);
        Assert.Empty(report.Problems);
    }

    [Fact]
    public void DailyCheckIsDueOncePerDay()
    {
        var settings = new AppSettings { FacebookPages = [Page("p1", "t")] };
        Assert.True(TokenKeeper.IsDue(settings, Now));
        settings.FacebookApp.LastAutoCheckAt = Now.AddHours(-3);
        Assert.False(TokenKeeper.IsDue(settings, Now));
        settings.FacebookApp.LastAutoCheckAt = Now.AddHours(-25);
        Assert.True(TokenKeeper.IsDue(settings, Now));
        Assert.False(TokenKeeper.IsDue(new AppSettings(), Now));
    }

    private sealed class FakeTokens : IFacebookTokens
    {
        public Dictionary<string, TokenDebugInfo> Tokens { get; } = [];
        public Dictionary<string, ExchangedToken> Exchange { get; } = [];
        public List<FacebookPageInfo> Pages { get; set; } = [];
        public string? ListedWith { get; private set; }

        public Task<TokenDebugInfo> DebugTokenAsync(string token, string? appAccessToken, CancellationToken ct) =>
            Task.FromResult(Tokens.TryGetValue(token, out var info) ? info : new TokenDebugInfo(false, null, null, null, null, "unknown"));

        public Task<ExchangedToken> ExchangeForLongLivedAsync(string appId, string appSecret, string token, CancellationToken ct)
        {
            Assert.Equal("sec", appSecret);
            return Exchange.TryGetValue(token, out var e) ? Task.FromResult(e) : throw new InvalidOperationException("Invalid token (mã 190)");
        }

        public Task<List<FacebookPageInfo>> ListPagesAsync(string userToken, CancellationToken ct)
        {
            ListedWith = userToken;
            return Task.FromResult(Pages);
        }
    }
}
