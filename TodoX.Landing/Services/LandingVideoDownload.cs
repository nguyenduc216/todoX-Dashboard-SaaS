using Microsoft.AspNetCore.Http;

namespace TodoX.Landing.Services;

/// <summary>
/// "Tải xuống" button of the shareable video pages (/mau-video/{slug}/tai-xuong, /video/{slug}/tai-xuong).
/// The raw /media file stays blocked (see <see cref="LandingMediaGuard"/>); the file is handed out only to a visitor
/// who opened the todox.vn page and clicked the button, saved under a readable name (the page slug).
/// </summary>
public static class LandingVideoDownload
{
    public const string RouteSuffix = "/tai-xuong";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm" };

    /// <summary>Physical file of a public media URL (e.g. /media/landing/video-samples/videos/x.mp4), kept inside the media root.</summary>
    public static string? ResolveFile(string? url, string? storageRoot, string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(storageRoot)) return null;
        var prefix = "/" + (requestPath ?? "/media").Trim('/') + "/";
        var path = url.Split('?', '#')[0];
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var relative = Uri.UnescapeDataString(path[prefix.Length..]);
        if (relative.Length == 0 || relative.Contains("..") || relative.Contains(':') || relative.Contains('\\')) return null;
        if (!VideoExtensions.Contains(Path.GetExtension(relative))) return null;
        var root = Path.GetFullPath(storageRoot);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// The click came from a todox.vn page: same-origin navigation (or, for browsers without Fetch Metadata,
    /// a Referer on this host). Pasting the download link elsewhere or hotlinking it gets the page instead.
    /// </summary>
    public static bool IsFromOwnPage(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0) return site.Equals("same-origin", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
               && string.Equals(referer.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }

    public static IResult Serve(HttpContext http, string? videoUrl, string slug, SharedMediaOptions media, string pagePath)
    {
        if (!IsFromOwnPage(http.Request)) return Results.Redirect(pagePath);
        var file = ResolveFile(videoUrl, media.StorageRoot, media.RequestPath);
        if (file is null) return Results.Redirect(pagePath);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.File(file, ext == ".webm" ? "video/webm" : "video/mp4",
            fileDownloadName: $"todox-{slug}{ext}", enableRangeProcessing: true);
    }
}
