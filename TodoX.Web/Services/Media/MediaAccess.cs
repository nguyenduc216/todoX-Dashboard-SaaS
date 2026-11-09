using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

using Microsoft.JSInterop;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Media;

/// <summary>
/// Protects stored video files (/uploads, /media) so a copied link cannot be opened, shared or downloaded
/// outside TodoX. A video file is served only when:
///   - the request carries the signed-in media cookie (HttpOnly, issued to a logged-in dashboard user), and
///   - the browser says the request comes from a &lt;video&gt;/&lt;audio&gt; element (Sec-Fetch-Dest), which
///     a pasted address-bar link, "open in new tab", fetch()/XHR from devtools, download managers and other
///     sites cannot produce.
/// Downloads go through <see cref="MediaDownloadService"/>: a short-lived ticket bound to the same user.
/// </summary>
public static class MediaAccessRules
{
    public const string SessionCookieName = "tdx_media";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v", ".mkv", ".avi"
    };

    /// <summary>Folders served as static files whose video files are protected.</summary>
    public static IReadOnlyList<string> ProtectedPrefixes(IConfiguration config)
    {
        var prefixes = new List<string> { "/uploads", "/media" };
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var path = value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath
                : value;
            path = "/" + path.Trim().Trim('/');
            if (path.Length > 1 && !prefixes.Contains(path, StringComparer.OrdinalIgnoreCase)) prefixes.Add(path);
        }
        Add(config["SharedMedia:RequestPath"]);
        Add(config["VideoRender:PublicBase"]);
        Add(config["Storage:PublicUploadBase"]);
        return prefixes;
    }

    public static bool IsProtectedVideoPath(string? path, IReadOnlyList<string> prefixes)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext) || !ProtectedExtensions.Contains(ext)) return false;
        if (!prefixes.Any(p => path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))) return false;
        return !IsProviderInput(path);
    }

    /// <summary>
    /// Motion videos the customer uploads for "Nhảy bán hàng" are fetched by the AI provider's servers from their
    /// public URL, so they stay reachable (they are the customer's own input, not a TodoX result).
    /// </summary>
    public static bool IsProviderInput(string path)
    {
        var name = Path.GetFileName(path);
        return path.Contains("/dance-sell/", StringComparison.OrdinalIgnoreCase)
            && (name.StartsWith("motion-upload-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("motion-tiktok-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the browser made the request for a media element on a TodoX page.</summary>
    public static bool IsMediaElementRequest(IHeaderDictionary headers)
    {
        var dest = headers["Sec-Fetch-Dest"].ToString();
        if (dest.Length > 0)
        {
            if (!dest.Equals("video", StringComparison.OrdinalIgnoreCase) && !dest.Equals("audio", StringComparison.OrdinalIgnoreCase))
                return false;
            var site = headers["Sec-Fetch-Site"].ToString();
            return site.Length == 0 || site.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                || site.Equals("same-site", StringComparison.OrdinalIgnoreCase);
        }
        // iPhone/iPad play video through the system media player (AppleCoreMedia), which sends no fetch metadata
        // but does send the site's cookies.
        return headers.UserAgent.ToString().Contains("AppleCoreMedia", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MediaDownloadTicket(Guid UserId, string Url, string FileName);

/// <summary>Signs the media cookie and the session/download tickets (ASP.NET Data Protection).</summary>
public sealed class MediaAccessTokens
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector _cookie;
    private readonly ITimeLimitedDataProtector _sessionTicket;
    private readonly ITimeLimitedDataProtector _downloadTicket;

    public MediaAccessTokens(IDataProtectionProvider provider)
    {
        _cookie = provider.CreateProtector("TodoX.Media.Session.v1").ToTimeLimitedDataProtector();
        _sessionTicket = provider.CreateProtector("TodoX.Media.SessionTicket.v1").ToTimeLimitedDataProtector();
        _downloadTicket = provider.CreateProtector("TodoX.Media.DownloadTicket.v1").ToTimeLimitedDataProtector();
    }

    public string CreateCookie(Guid userId) => _cookie.Protect(userId.ToString("N"), MediaAccessRules.SessionLifetime);
    public Guid? ReadCookie(string? value) => ReadGuid(_cookie, value);

    public string CreateSessionTicket(Guid userId) => _sessionTicket.Protect(userId.ToString("N"), TimeSpan.FromMinutes(2));
    public Guid? ReadSessionTicket(string? value) => ReadGuid(_sessionTicket, value);

    public string CreateDownloadTicket(MediaDownloadTicket ticket)
        => _downloadTicket.Protect(JsonSerializer.Serialize(ticket, Json), TimeSpan.FromMinutes(2));

    public MediaDownloadTicket? ReadDownloadTicket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<MediaDownloadTicket>(_downloadTicket.Unprotect(value), Json);
            return ticket is null || ticket.UserId == Guid.Empty || string.IsNullOrWhiteSpace(ticket.Url) ? null : ticket;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static Guid? ReadGuid(ITimeLimitedDataProtector protector, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return Guid.TryParseExact(protector.Unprotect(value), "N", out var id) && id != Guid.Empty ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Maps a stored media URL (/uploads/..., /media/...) to its file on disk.</summary>
public sealed class MediaFileLocator
{
    private readonly List<(string Prefix, string Root)> _roots = new();

    public MediaFileLocator(IConfiguration config, IWebHostEnvironment env)
    {
        // Same order as the static-file mappings in Program.cs.
        var videoRoot = config["VideoRender:StorageRoot"];
        var videoBase = config["VideoRender:PublicBase"];
        if (!string.IsNullOrWhiteSpace(videoRoot) && !string.IsNullOrWhiteSpace(videoBase))
        {
            var prefix = videoBase.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(videoBase).AbsolutePath : videoBase;
            _roots.Add(("/" + prefix.Trim('/'), Path.IsPathRooted(videoRoot) ? videoRoot : Path.Combine(env.ContentRootPath, videoRoot)));
        }
        var sharedRoot = config["SharedMedia:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(sharedRoot))
            _roots.Add(("/" + (config["SharedMedia:RequestPath"] ?? "/media").Trim('/'), sharedRoot));
        if (!string.IsNullOrWhiteSpace(env.WebRootPath))
            _roots.Add(("", env.WebRootPath));
    }

    public string? Resolve(string urlPath)
    {
        var path = Uri.UnescapeDataString(urlPath.Split('?', '#')[0]);
        foreach (var (prefix, root) in _roots)
        {
            if (prefix.Length > 0 && !path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
            var fullRoot = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(fullRoot, path[prefix.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>Circuit-side helpers: keep the media cookie in step with the signed-in user, and start downloads.</summary>
public sealed class MediaDownloadService
{
    private readonly MediaAccessTokens _tokens;
    private readonly IJSRuntime _js;
    private readonly AuthStateService _auth;
    private Guid? _syncedUser;

    public MediaDownloadService(MediaAccessTokens tokens, IJSRuntime js, AuthStateService auth)
    {
        _tokens = tokens;
        _js = js;
        _auth = auth;
    }

    /// <summary>Issues (or clears) the media cookie for the current user. Call after render.</summary>
    public async Task SyncSessionAsync()
    {
        var userId = _auth.CurrentUser?.UserId;
        if (userId == _syncedUser) return;
        _syncedUser = userId;
        if (userId is { } id && id != Guid.Empty)
            await _js.InvokeVoidAsync("todoxMedia.ensureSession", _tokens.CreateSessionTicket(id));
        else
            await _js.InvokeVoidAsync("todoxMedia.clearSession");
    }

    /// <summary>Downloads a video the user is looking at, through TodoX (the file link itself is never exposed).</summary>
    public async Task DownloadAsync(string? url, string? fileName = null)
    {
        var user = _auth.CurrentUser ?? throw new InvalidOperationException("Bạn cần đăng nhập để tải video.");
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Chưa có video để tải.");
        var name = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(url.Split('?', '#')[0]) : fileName;
        if (string.IsNullOrWhiteSpace(name)) name = "todox-video.mp4";
        await SyncSessionAsync();
        var ticket = _tokens.CreateDownloadTicket(new MediaDownloadTicket(user.UserId, url.Trim(), name));
        await _js.InvokeVoidAsync("todoxDownload.startBrowserDownload", "/api/media/download?t=" + Uri.EscapeDataString(ticket));
    }
}

public static class MediaAccessEndpoints
{
    public static IApplicationBuilder UseProtectedMedia(this IApplicationBuilder app, IConfiguration config)
    {
        if (string.Equals(config["MediaAccess:Enabled"], "false", StringComparison.OrdinalIgnoreCase)) return app;
        var prefixes = MediaAccessRules.ProtectedPrefixes(config);
        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && MediaAccessRules.IsProtectedVideoPath(request.Path.Value, prefixes))
            {
                var tokens = context.RequestServices.GetRequiredService<MediaAccessTokens>();
                var user = tokens.ReadCookie(request.Cookies[MediaAccessRules.SessionCookieName]);
                context.Response.Headers.CacheControl = "private, no-store";
                if (user is null || !MediaAccessRules.IsMediaElementRequest(request.Headers))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.ContentDisposition = "inline";
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    return Task.CompletedTask;
                });
            }
            await next();
        });
    }

    public static void MapMediaAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/media/session", (HttpContext context, MediaSessionRequest body, MediaAccessTokens tokens) =>
        {
            if (!IsSameOrigin(context.Request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var userId = tokens.ReadSessionTicket(body?.Ticket);
            if (userId is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);
            context.Response.Cookies.Append(MediaAccessRules.SessionCookieName, tokens.CreateCookie(userId.Value), new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = MediaAccessRules.SessionLifetime,
                IsEssential = true
            });
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        }).DisableAntiforgery();

        app.MapPost("/api/media/session/clear", (HttpContext context) =>
        {
            if (!IsSameOrigin(context.Request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            context.Response.Cookies.Delete(MediaAccessRules.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).DisableAntiforgery();

        app.MapGet("/api/media/download", async (HttpContext context, string? t, MediaAccessTokens tokens, MediaFileLocator files,
            IHttpClientFactory httpClients, IConfiguration config, ILoggerFactory loggers, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var ticket = tokens.ReadDownloadTicket(t);
            var cookieUser = tokens.ReadCookie(context.Request.Cookies[MediaAccessRules.SessionCookieName]);
            // The ticket only works in the browser of the user it was made for, and only for 2 minutes.
            if (ticket is null || cookieUser is null || cookieUser != ticket.UserId)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var fileName = SafeFileName(ticket.FileName);
            var localPath = TryLocalPath(ticket.Url, context.Request, config) is { } urlPath ? files.Resolve(urlPath) : null;
            if (localPath is not null)
                return Results.File(localPath, ContentType(localPath), fileName, enableRangeProcessing: true);

            if (!Uri.TryCreate(ticket.Url, UriKind.Absolute, out var remote) || remote.Scheme != Uri.UriSchemeHttps)
                return Results.NotFound();
            try
            {
                // A video still kept by the AI provider: stream it through TodoX so its address stays hidden.
                var client = httpClients.CreateClient();
                var response = await client.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return Results.StatusCode(StatusCodes.Status502BadGateway);
                context.Response.RegisterForDispose(response);
                var stream = await response.Content.ReadAsStreamAsync(ct);
                return Results.File(stream, response.Content.Headers.ContentType?.MediaType ?? "video/mp4", fileName);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                loggers.CreateLogger("MediaDownload").LogWarning(ex, "MEDIA_DOWNLOAD_REMOTE_FAILED host={Host}", remote.Host);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }
        });
    }

    /// <summary>The path of a URL served by this app (relative, or absolute on this host / the public base URL).</summary>
    internal static string? TryLocalPath(string url, HttpRequest request, IConfiguration config)
    {
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase)) return uri.AbsolutePath;
        foreach (var key in new[] { "TodoX:PublicBaseUrl", "App:PublicBaseUrl", "Storage:PublicBaseUrl" })
        {
            if (Uri.TryCreate(config[key], UriKind.Absolute, out var b) && b.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
                return uri.AbsolutePath;
        }
        return null;
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0) return site.Equals("same-origin", StringComparison.OrdinalIgnoreCase);
        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0 || (Uri.TryCreate(origin, UriKind.Absolute, out var o)
            && o.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "todox-video.mp4" : cleaned;
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".mp3" => "audio/mpeg",
        _ => "video/mp4"
    };

    public sealed record MediaSessionRequest(string? Ticket);
}
