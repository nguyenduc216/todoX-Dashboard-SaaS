using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TodoX.Landing.Services;

/// <summary>
/// Sample videos under /media play only inside todox.vn pages: a pasted or shared file link, "open in new tab",
/// download managers and other sites get 403. Every page/API response gives the visitor a signed cookie, and a
/// video file is served only to a &lt;video&gt; element on a todox.vn page that carries it. Link-preview bots
/// (Facebook, Zalo, Google...) still read the file so shared /video and /mau-video pages keep their preview.
/// </summary>
public static class LandingMediaGuard
{
    public const string CookieName = "tdx_lm";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan RenewAfter = TimeSpan.FromDays(1);
    private const string Purpose = "TodoX.Landing.MediaCookie.v1";

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v"
    };

    private static readonly string[] PreviewBots =
    {
        "facebookexternalhit", "Facebot", "Zalo", "Googlebot", "Google-InspectionTool", "bingbot",
        "Twitterbot", "TelegramBot", "LinkedInBot", "Slackbot", "Discordbot", "WhatsApp", "Applebot"
    };

    public static bool IsProtectedVideoPath(string? path, string mediaRequestPath)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var prefix = "/" + (mediaRequestPath ?? "/media").Trim('/') + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && ProtectedExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsPreviewBot(string userAgent)
        => PreviewBots.Any(b => userAgent.Contains(b, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for a request a todox.vn page's &lt;video&gt; makes (or the iPhone system player).</summary>
    public static bool IsMediaElementRequest(IHeaderDictionary headers)
    {
        var dest = headers["Sec-Fetch-Dest"].ToString();
        if (dest.Length > 0)
        {
            if (!dest.Equals("video", StringComparison.OrdinalIgnoreCase) && !dest.Equals("audio", StringComparison.OrdinalIgnoreCase))
                return false;
            var site = headers["Sec-Fetch-Site"].ToString();
            return site.Length == 0 || site.Equals("same-origin", StringComparison.OrdinalIgnoreCase);
        }
        return headers.UserAgent.ToString().Contains("AppleCoreMedia", StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseLandingMediaGuard(this IApplicationBuilder app, IConfiguration config, string mediaRequestPath)
    {
        if (string.Equals(config["MediaAccess:Enabled"], "false", StringComparison.OrdinalIgnoreCase)) return app;
        var protector = app.ApplicationServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose).ToTimeLimitedDataProtector();

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            var issuedAt = ReadCookie(protector, request.Cookies[CookieName]);

            if (IsProtectedVideoPath(request.Path.Value, mediaRequestPath))
            {
                var allowed = IsPreviewBot(request.Headers.UserAgent.ToString())
                    || (issuedAt is not null && IsMediaElementRequest(request.Headers));
                context.Response.Headers.CacheControl = "private, no-store";
                if (!allowed)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.ContentDisposition = "inline";
                    return Task.CompletedTask;
                });
                await next();
                return;
            }

            if (issuedAt is null || DateTimeOffset.UtcNow - issuedAt.Value > RenewAfter)
            {
                context.Response.Cookies.Append(CookieName,
                    protector.Protect(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), CookieLifetime),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                        MaxAge = CookieLifetime,
                        IsEssential = true
                    });
            }
            await next();
        });
    }

    private static DateTimeOffset? ReadCookie(ITimeLimitedDataProtector protector, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return long.TryParse(protector.Unprotect(value), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
