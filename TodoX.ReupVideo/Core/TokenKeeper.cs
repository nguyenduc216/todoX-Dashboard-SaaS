using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Core;

public sealed record TokenProblem(FacebookPage Page, string Text);

public sealed record TokenCheckReport(int Checked, int Renewed, List<TokenProblem> Problems);

/// <summary>
/// Keeps fanpage tokens alive:
/// <list type="bullet">
/// <item>a short-lived token pasted by the user is exchanged for a long-lived one (needs the user's App ID + Secret);</item>
/// <item>every day each page token's expiry is asked from Facebook;</item>
/// <item>a page token that expired or expires within <see cref="WarnBefore"/> is replaced by a fresh one fetched with the
/// saved long-lived user token, and that user token is itself re-exchanged before it expires;</item>
/// <item>whatever cannot be renewed by itself is reported so the user is warned before posting fails.</item>
/// </list>
/// Every step is written to <see cref="TokenHistory"/> (never the token itself).
/// </summary>
public sealed class TokenKeeper
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    public static readonly TimeSpan WarnBefore = TimeSpan.FromDays(7);
    /// <summary>The saved user token is re-exchanged when less than this is left.</summary>
    public static readonly TimeSpan RenewUserBefore = TimeSpan.FromDays(15);
    private const string Account = "Tài khoản Facebook";

    private readonly IFacebookTokens _fb;
    private readonly TokenHistory _history;
    private readonly Func<DateTime> _now;

    public TokenKeeper(IFacebookTokens fb, TokenHistory history, Func<DateTime>? now = null)
    {
        _fb = fb;
        _history = history;
        _now = now ?? (() => DateTime.Now);
    }

    public static bool IsDue(AppSettings settings, DateTime now) =>
        settings.FacebookPages.Count > 0 &&
        (settings.FacebookApp.LastAutoCheckAt is not { } last || now - last >= CheckEvery || last > now);

    public static bool ExpiresSoon(DateTime? at, DateTime now) => at is { } a && a - now < WarnBefore;

    /// <summary>Expired, or the token / data access ends within <see cref="WarnBefore"/>.</summary>
    public static bool NeedsRenew(FacebookPage page, DateTime now) =>
        page.TokenExpired || ExpiresSoon(page.TokenExpiresAt, now) || ExpiresSoon(page.DataAccessExpiresAt, now);

    private static string? AppToken(AppSettings settings) =>
        settings.FacebookApp.HasCredentials ? $"{settings.FacebookApp.AppId}|{SecretProtector.Unprotect(settings.FacebookApp.ProtectedSecret)}" : null;

    /// <summary>
    /// A user token pasted in "Cách 1": exchanged for a long-lived one when App ID + Secret are saved, then kept
    /// (encrypted) so page tokens can be renewed later. Returns the token to list pages with, and a warning when it is
    /// still short-lived.
    /// </summary>
    public async Task<(string Token, string? Note)> PrepareUserTokenAsync(AppSettings settings, string token, CancellationToken ct)
    {
        var app = settings.FacebookApp;
        var now = _now();
        var result = token;
        string? exchangeError = null;
        if (app.HasCredentials)
        {
            try
            {
                var exchanged = await _fb.ExchangeForLongLivedAsync(app.AppId, SecretProtector.Unprotect(app.ProtectedSecret), token, ct);
                result = exchanged.AccessToken;
                _history.Add(TokenEventKind.Exchanged, null, Account, $"Đổi token ngắn hạn → token dài hạn (App ID {app.AppId}).", exchanged.ExpiresAt, exchanged.ExpiresAt is null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                exchangeError = ex.Message;
                _history.Add(TokenEventKind.Failed, null, Account, "Không đổi được sang token dài hạn: " + ex.Message + " — token phải được tạo từ đúng ứng dụng có App ID này.");
            }
        }

        TokenDebugInfo? info = null;
        try
        {
            info = await _fb.DebugTokenAsync(result, AppToken(settings), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // expiry unknown: the pages are still imported
        }

        if (info is { IsValid: true } && (info.ExpiresAt is null || info.ExpiresAt - now > TimeSpan.FromDays(2)))
        {
            app.ProtectedUserToken = SecretProtector.Protect(result);
            app.UserTokenExpiresAt = info.ExpiresAt;
            if (!app.HasCredentials || exchangeError is not null)
            {
                _history.Add(TokenEventKind.Updated, null, Account, "Lưu token tài khoản dài hạn để tự gia hạn page token.", info.ExpiresAt, info.ExpiresAt is null);
            }
        }

        string? note = null;
        if (info is { IsValid: true, ExpiresAt: { } at } && at - now < TimeSpan.FromDays(1))
        {
            note = $"Token vừa dán là token NGẮN HẠN, hết hạn lúc {at:HH:mm dd/MM}: page token lấy từ nó cũng hết hạn lúc đó. " +
                   (app.HasCredentials
                       ? "Không đổi được sang dài hạn: " + exchangeError + " Hãy chọn đúng ứng dụng của bạn (Meta App) trong Graph API Explorer rồi tạo token lại."
                       : "Nhập App ID + App Secret (mục \"Ứng dụng Facebook của bạn\") để app tự đổi sang dài hạn, hoặc gia hạn bằng Access Token Debugger rồi dán lại.");
        }

        return (result, note);
    }

    /// <summary>Asks Facebook every page token's expiry and renews what expires soon. <paramref name="only"/> = null: all pages (daily check).</summary>
    public async Task<TokenCheckReport> CheckAsync(AppSettings settings, IReadOnlyCollection<FacebookPage>? only, IProgress<string> log, CancellationToken ct)
    {
        var now = _now();
        var appToken = AppToken(settings);
        var pages = (only ?? settings.FacebookPages).ToList();
        var problems = new List<FacebookPage>();
        var checkedCount = 0;
        foreach (var page in pages)
        {
            var token = SecretProtector.Unprotect(page.ProtectedToken);
            if (token.Length == 0)
            {
                problems.Add(page);
                continue;
            }

            TokenDebugInfo info;
            try
            {
                info = await _fb.DebugTokenAsync(token, appToken, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Report($"Fanpage {page.Name}: chưa kiểm tra được token ({ex.Message}).");
                continue;
            }

            checkedCount++;
            page.TokenCheckedAt = now;
            page.LastCheckAt = now;
            if (!info.IsValid)
            {
                if (!page.TokenExpired)
                {
                    _history.Add(TokenEventKind.Expired, page.PageId, page.Name, "Token không còn dùng được: " + info.Error);
                }

                page.TokenExpired = true;
                page.LastCheckResult = "Token hết hạn";
            }
            else
            {
                page.TokenExpired = false;
                page.TokenExpiresAt = info.ExpiresAt;
                page.DataAccessExpiresAt = info.DataAccessExpiresAt;
                page.LastCheckResult = "OK";
            }

            if (NeedsRenew(page, now))
            {
                problems.Add(page);
            }
        }

        var renewed = 0;
        if (problems.Count > 0 && await FreshUserTokenAsync(settings, now, appToken, ct) is { } userToken)
        {
            try
            {
                var fresh = await _fb.ListPagesAsync(userToken, ct);
                foreach (var page in problems.ToList())
                {
                    if (fresh.FirstOrDefault(p => p.Id == page.PageId && !string.IsNullOrEmpty(p.AccessToken)) is not { } found)
                    {
                        continue;
                    }

                    var info = await _fb.DebugTokenAsync(found.AccessToken!, appToken, ct);
                    if (!info.IsValid)
                    {
                        continue;
                    }

                    page.ProtectedToken = SecretProtector.Protect(found.AccessToken!);
                    page.TokenExpired = false;
                    page.TokenCheckedAt = now;
                    page.TokenExpiresAt = info.ExpiresAt;
                    page.DataAccessExpiresAt = info.DataAccessExpiresAt;
                    page.LastCheckAt = now;
                    page.LastCheckResult = "OK (tự gia hạn)";
                    renewed++;
                    _history.Add(TokenEventKind.Renewed, page.PageId, page.Name, "Tự lấy page token mới bằng token tài khoản đã lưu.", info.ExpiresAt, info.ExpiresAt is null);
                    log.Report($"Fanpage {page.Name}: đã tự gia hạn token ({Describe(info.ExpiresAt)}).");
                    if (!NeedsRenew(page, now))
                    {
                        problems.Remove(page);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _history.Add(TokenEventKind.Failed, null, Account, "Không tự gia hạn được page token: " + ex.Message);
            }
        }

        var reported = new List<TokenProblem>();
        foreach (var page in problems)
        {
            var text = ProblemText(page);
            reported.Add(new TokenProblem(page, text));
            if (!page.TokenExpired)
            {
                _history.Add(TokenEventKind.Warning, page.PageId, page.Name, text + " — chưa tự gia hạn được.", page.TokenExpiresAt ?? page.DataAccessExpiresAt);
            }
        }

        _history.Add(TokenEventKind.Checked, null, only is null ? "Kiểm tra hằng ngày" : "Kiểm tra token",
            $"Kiểm tra {checkedCount}/{pages.Count} token: {renewed} tự gia hạn, {reported.Count} cần xử lý.");
        if (only is null)
        {
            settings.FacebookApp.LastAutoCheckAt = now;
        }

        return new TokenCheckReport(checkedCount, renewed, reported);
    }

    /// <summary>The saved long-lived user token, re-exchanged first when it expires within <see cref="RenewUserBefore"/>.</summary>
    private async Task<string?> FreshUserTokenAsync(AppSettings settings, DateTime now, string? appToken, CancellationToken ct)
    {
        var app = settings.FacebookApp;
        var user = SecretProtector.Unprotect(app.ProtectedUserToken);
        if (user.Length == 0)
        {
            return null;
        }

        TokenDebugInfo info;
        try
        {
            info = await _fb.DebugTokenAsync(user, appToken, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return user; // network trouble: try with it anyway
        }

        if (!info.IsValid)
        {
            app.ProtectedUserToken = "";
            app.UserTokenExpiresAt = null;
            _history.Add(TokenEventKind.Expired, null, Account, "Token tài khoản đã lưu hết hạn — dán token mới ở Cách 1 để app tiếp tục tự gia hạn. " + info.Error);
            return null;
        }

        app.UserTokenExpiresAt = info.ExpiresAt;
        if (app.HasCredentials && info.ExpiresAt is { } at && at - now < RenewUserBefore)
        {
            try
            {
                var exchanged = await _fb.ExchangeForLongLivedAsync(app.AppId, SecretProtector.Unprotect(app.ProtectedSecret), user, ct);
                if (exchanged.ExpiresAt is null || exchanged.ExpiresAt > at)
                {
                    app.ProtectedUserToken = SecretProtector.Protect(exchanged.AccessToken);
                    app.UserTokenExpiresAt = exchanged.ExpiresAt;
                    _history.Add(TokenEventKind.Renewed, null, Account, "Gia hạn token tài khoản.", exchanged.ExpiresAt, exchanged.ExpiresAt is null);
                    return exchanged.AccessToken;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _history.Add(TokenEventKind.Failed, null, Account, "Không gia hạn được token tài khoản: " + ex.Message);
            }
        }

        return user;
    }

    public static string ProblemText(FacebookPage page) =>
        page.ProtectedToken.Length == 0 ? "Chưa có token"
        : page.TokenExpired ? "Token đã hết hạn"
        : page.TokenExpiresAt is { } at && (page.DataAccessExpiresAt is not { } d || at <= d) ? $"Token hết hạn {at:HH:mm dd/MM/yyyy}"
        : page.DataAccessExpiresAt is { } da ? $"Facebook cần đăng nhập lại trước {da:dd/MM/yyyy}"
        : "Token cần gia hạn";

    /// <summary>"Không hết hạn" / "hết hạn 20/01/2027" for the UI.</summary>
    public static string Describe(DateTime? expiresAt) => expiresAt is { } at ? $"hết hạn {at:HH:mm dd/MM/yyyy}" : "không hết hạn";
}
