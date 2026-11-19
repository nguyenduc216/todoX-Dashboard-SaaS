using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TodoX.Landing.Data;
using TodoX.Landing.Models;
using TodoX.Landing.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 32 * 1024;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddDataProtection();
builder.Services.AddSingleton<LandingConnectionFactory>();
builder.Services.AddScoped<LandingContactRepository>();
builder.Services.AddScoped<IndustrySolutionRepository>();
builder.Services.AddScoped<IntroVideoRepository>();
builder.Services.AddScoped<ShortVideoRepository>();
builder.Services.AddScoped<VideoSampleRepository>();
builder.Services.Configure<SharedMediaOptions>(builder.Configuration.GetSection(SharedMediaOptions.SectionName));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("contact-leads", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: https://todox.vn https://chat-api.genstaff.net; " +
        "media-src 'self' https://chat-api.genstaff.net; style-src 'self' 'unsafe-inline' https://chat-api.genstaff.net; script-src 'self' https://chat-api.genstaff.net; " +
        "font-src 'self' data: https://chat-api.genstaff.net; connect-src 'self' https://chat-api.genstaff.net wss://chat-api.genstaff.net; " +
        "frame-src https://chat-api.genstaff.net; frame-ancestors 'self'; base-uri 'self'; form-action 'self'";

    await next();
});

app.UseForwardedHeaders();
app.UseRateLimiter();
// Video files under /media play only inside todox.vn pages (no direct links, downloads or hotlinking).
app.UseLandingMediaGuard(app.Configuration, app.Configuration["SharedMedia:RequestPath"] ?? "/media");
// Pages must show a new deploy at once: browsers re-check HTML with the server (ETag) on every visit,
// unless the endpoint chose its own lifetime (share pages). CSS/JS/images keep the 7-day cache and use ?v=.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
            && !context.Response.Headers.ContainsKey("Cache-Control"))
        {
            context.Response.Headers.CacheControl = "no-cache";
        }
        return Task.CompletedTask;
    });
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.File.Name;
        if (path is not null && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
        else if (path is not null && path.Contains('.'))
        {
            context.Context.Response.Headers.CacheControl = "public,max-age=604800";
        }
    }
});

var sharedMediaOptions = app.Services.GetRequiredService<IOptions<SharedMediaOptions>>().Value;
var sharedMediaRoot = sharedMediaOptions.StorageRoot;
if (!string.IsNullOrWhiteSpace(sharedMediaRoot))
{
    try
    {
        var physicalRoot = Path.GetFullPath(sharedMediaRoot);
        Directory.CreateDirectory(physicalRoot);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(physicalRoot),
            RequestPath = "/" + (sharedMediaOptions.RequestPath ?? "/media").Trim('/'),
            // Uploaded media gets a unique file name, so repeat views can come straight from the browser cache.
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public,max-age=604800"
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "SharedMedia static file mapping is not ready.");
    }
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "TodoX.Landing",
    status = "ok"
}));

app.MapGet("/health/ready", async (
    LandingContactRepository contacts,
    IndustrySolutionRepository industries,
    IOptions<SharedMediaOptions> mediaOptions,
    CancellationToken ct) =>
{
    var contactReady = await contacts.IsReadyAsync(ct);
    var industryReady = await industries.IsReadyAsync(ct);
    var mediaRoot = mediaOptions.Value.StorageRoot;
    var mediaReady = !string.IsNullOrWhiteSpace(mediaRoot) && Directory.Exists(mediaRoot);
    var ready = contactReady && industryReady && mediaReady;

    var payload = new
    {
        service = "TodoX.Landing",
        status = ready ? "ready" : "not-ready",
        databaseReady = contactReady && industryReady,
        contactReady,
        industryReady,
        sharedMedia = new
        {
            configured = !string.IsNullOrWhiteSpace(mediaRoot),
            readable = mediaReady,
            requestPath = mediaOptions.Value.RequestPath
        }
    };

    return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/industry-solutions", async (IndustrySolutionRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.ListPublicAsync(ct)));

app.MapGet("/api/landing/intro-video", async (IntroVideoRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPublicAsync(ct)));

app.MapGet("/api/landing/short-video", async (ShortVideoRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPublicAsync(ct)));

app.MapGet("/api/landing/video-samples", async (VideoSampleRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPublicAsync(ct)));

app.MapPost("/api/contact-leads", async (
    HttpContext http,
    ContactLeadCreateRequest request,
    LandingContactRepository repository,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("LandingContactLeads");
    var requestId = Activity.Current?.Id ?? http.TraceIdentifier;
    var started = Stopwatch.GetTimestamp();

    static ContactLeadCreateResponse Success(string? leadCode = null) => new()
    {
        Success = true,
        LeadCode = leadCode,
        Message = "TodoX đã nhận thông tin. Chúng tôi sẽ liên hệ tư vấn trong thời gian sớm nhất."
    };

    if (!string.IsNullOrWhiteSpace(request.Website))
    {
        logger.LogInformation("Landing lead honeypot ignored. requestId={RequestId}", requestId);
        return Results.Ok(Success());
    }

    var (validation, lead) = ContactLeadValidator.Validate(request);
    if (!validation.IsValid || lead is null)
    {
        logger.LogInformation("Landing lead validation failed. requestId={RequestId}", requestId);
        return Results.ValidationProblem(validation.Errors);
    }

    try
    {
        var leadCode = await repository.InsertAsync(lead, new ContactLeadInsertContext
        {
            RequestId = requestId,
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString()
        }, ct);

        logger.LogInformation("Landing lead created. requestId={RequestId} leadCode={LeadCode} elapsedMs={ElapsedMs}",
            requestId, leadCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return Results.Ok(Success(leadCode));
    }
    catch (LandingSchemaUnavailableException)
    {
        logger.LogWarning("Landing lead database unavailable. requestId={RequestId}", requestId);
        return Results.Problem(
            title: "Landing contact service is temporarily unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Landing lead insert failed. requestId={RequestId}", requestId);
        return Results.Problem(
            title: "Unable to receive contact lead.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("contact-leads");

// Readable share link per sample video, server-rendered so Facebook/Zalo/Messenger previews
// show its title, description and thumbnail instead of a bare .mp4 URL.
app.MapGet("/video/{slug}", async (
    string slug,
    HttpContext http,
    IndustrySolutionRepository repository,
    IConfiguration configuration,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    CancellationToken ct) =>
{
    var match = await repository.FindVideoBySlugAsync(slug, ct);
    if (match is not { } found || string.IsNullOrWhiteSpace(found.Video.VideoUrl))
        return Results.Redirect("/affiliate-video");

    if (!string.Equals(slug, found.Video.Slug, StringComparison.Ordinal))
        return Results.Redirect(found.Video.SharePath, permanent: true);

    http.Response.Headers.CacheControl = "public,max-age=300";
    var baseUrl = VideoSharePage.ResolveBaseUrl(http.Request, configuration);
    return Results.Content(VideoSharePage.Render(VideoSharePage.FromIndustry(found.Video, found.Industry), baseUrl,
        LandingVideoDownload.CreateToken(protection, found.Video.Slug)), "text/html; charset=utf-8");
});

// Same for the sample library: /mau-video/{slug} is the shareable page of one sample.
app.MapGet("/mau-video/{slug}", async (
    string slug,
    HttpContext http,
    VideoSampleRepository repository,
    IConfiguration configuration,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    CancellationToken ct) =>
{
    var match = await repository.FindBySlugAsync(slug, ct);
    if (match is not { } found)
        return Results.Redirect("/mau-video");

    if (!string.Equals(slug, found.Sample.Slug, StringComparison.Ordinal))
        return Results.Redirect(found.Sample.SharePath, permanent: true);

    http.Response.Headers.CacheControl = "public,max-age=300";
    var baseUrl = VideoSharePage.ResolveBaseUrl(http.Request, configuration);
    return Results.Content(VideoSharePage.Render(VideoSharePage.FromSample(found.Sample, found.Catalog), baseUrl,
        LandingVideoDownload.CreateToken(protection, found.Sample.Slug)), "text/html; charset=utf-8");
});

// "Tải xuống" on the share pages: the raw /media file stays blocked, the page's button gets it with a readable name.
app.MapGet("/video/{slug}" + LandingVideoDownload.RouteSuffix, async (
    string slug,
    HttpContext http,
    IndustrySolutionRepository repository,
    IOptions<SharedMediaOptions> media,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    CancellationToken ct) =>
{
    var match = await repository.FindVideoBySlugAsync(slug, ct);
    if (match is not { } found || string.IsNullOrWhiteSpace(found.Video.VideoUrl))
        return Results.Redirect("/affiliate-video");
    return LandingVideoDownload.Serve(http, found.Video.VideoUrl, found.Video.Slug, media.Value, found.Video.SharePath, protection);
});

app.MapGet("/mau-video/{slug}" + LandingVideoDownload.RouteSuffix, async (
    string slug,
    HttpContext http,
    VideoSampleRepository repository,
    IOptions<SharedMediaOptions> media,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    CancellationToken ct) =>
{
    var match = await repository.FindBySlugAsync(slug, ct);
    if (match is not { } found)
        return Results.Redirect("/mau-video");
    return LandingVideoDownload.Serve(http, found.Sample.VideoUrl, found.Sample.Slug, media.Value, found.Sample.SharePath, protection);
});

// Serve the marketing deep link as its own document, including on refresh.
app.MapGet("/bat-dong-san", () => Results.File(Path.Combine(app.Environment.WebRootPath, "bat-dong-san.html"), "text/html; charset=utf-8"));
app.MapGet("/video-ngan", () => Results.File(Path.Combine(app.Environment.WebRootPath, "video-ngan.html"), "text/html; charset=utf-8"));
app.MapGet("/affiliate-video", () => Results.File(Path.Combine(app.Environment.WebRootPath, "affiliate-video.html"), "text/html; charset=utf-8"));
app.MapGet("/mau-video", () => Results.File(Path.Combine(app.Environment.WebRootPath, "mau-video.html"), "text/html; charset=utf-8"));
app.MapGet("/editvideo", () => Results.File(Path.Combine(app.Environment.WebRootPath, "editvideo.html"), "text/html; charset=utf-8"));
app.MapGet("/reupvideo", () => Results.File(Path.Combine(app.Environment.WebRootPath, "reupvideo.html"), "text/html; charset=utf-8"));

// "Tải app": desktop apps whose builds sit in {Downloads:Root}/{app} on the server (see AppDownloads).
var appDownloadsRoot = app.Configuration["Downloads:Root"] is { Length: > 0 } configuredRoot ? configuredRoot : AppDownloads.DefaultRoot;
const string downloadNotReady = "Bản cài đặt đang được cập nhật, vui lòng thử lại sau ít phút hoặc gọi 0366 699 961.";
app.MapGet("/api/landing/apps/{app}", (string app) =>
    AppDownloads.Latest(appDownloadsRoot, app) is { } release
        // fileName/fileUrl/sha256 let the desktop app update itself (it checks the hash before replacing itself).
        ? Results.Json(new
        {
            release.Version,
            release.PublishedAt,
            release.Size,
            Url = $"/downloads/{release.App}/latest",
            release.FileName,
            FileUrl = $"/downloads/{release.App}/{Uri.EscapeDataString(release.FileName)}",
            release.Sha256
        })
        : Results.NotFound());
app.MapGet("/downloads/{app}/latest", (string app, HttpContext http) =>
{
    http.Response.Headers.CacheControl = "no-store";
    return AppDownloads.Latest(appDownloadsRoot, app) is { } release
        ? Results.Redirect($"/downloads/{release.App}/{Uri.EscapeDataString(release.FileName)}")
        : Results.Text(downloadNotReady, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
});
app.MapGet("/downloads/{app}/{name}", (string app, string name) =>
    AppDownloads.Latest(appDownloadsRoot, app) is { } release && string.Equals(release.FileName, name, StringComparison.Ordinal)
        ? Results.File(release.FilePath, "application/octet-stream", release.FileName, enableRangeProcessing: true)
        : Results.Text(downloadNotReady, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("index.html");
app.Run();
