using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoX.Landing.Services;
using TodoX.Web.Services.Media;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Video files cannot be opened, shared or downloaded through their direct link.</summary>
public sealed class MediaAccessTests
{
    private static readonly IReadOnlyList<string> Prefixes = new[] { "/uploads", "/media" };

    [Theory]
    [InlineData("/uploads/video-render/202611/final-abc.mp4", true)]
    [InlineData("/media/landing/video-samples/videos/47ce377b3a654d6eb43c66a33984d02f.mp4", true)]
    [InlineData("/uploads/video-render/scene.WEBM", true)]
    [InlineData("/uploads/video-render/scene.png", false)]
    [InlineData("/css/site.mp4", false)]
    [InlineData("/uploads/dance-sell/abc/202611/motion-upload-0123.mp4", false)] // fetched by the AI provider
    [InlineData("/uploads/dance-sell/abc/202611/result-0123.mp4", true)]
    public void Video_files_under_media_folders_are_protected(string path, bool expected)
        => Assert.Equal(expected, MediaAccessRules.IsProtectedVideoPath(path, Prefixes));

    [Theory]
    [InlineData("video", "same-origin", null, true)]
    [InlineData("audio", "same-origin", null, true)]
    [InlineData("document", "none", null, false)]          // link pasted in the address bar / opened in a new tab
    [InlineData("empty", "same-origin", null, false)]      // fetch()/XHR from devtools
    [InlineData("video", "cross-site", null, false)]       // hotlinked from another site
    [InlineData(null, null, "curl/8.4", false)]            // download tools
    [InlineData(null, null, "AppleCoreMedia/1.0.0.21 (iPhone; U; CPU OS 17_0 like Mac OS X)", true)]
    public void Only_media_element_requests_are_served(string? dest, string? site, string? userAgent, bool expected)
    {
        IHeaderDictionary headers = new HeaderDictionary();
        if (dest is not null) headers["Sec-Fetch-Dest"] = dest;
        if (site is not null) headers["Sec-Fetch-Site"] = site;
        if (userAgent is not null) headers.UserAgent = userAgent;
        Assert.Equal(expected, MediaAccessRules.IsMediaElementRequest(headers));
    }

    [Fact]
    public void Tokens_round_trip_and_reject_tampering()
    {
        var tokens = new MediaAccessTokens(new EphemeralDataProtectionProvider());
        var user = Guid.NewGuid();
        Assert.Equal(user, tokens.ReadCookie(tokens.CreateCookie(user)));
        Assert.Equal(user, tokens.ReadSessionTicket(tokens.CreateSessionTicket(user)));
        Assert.Null(tokens.ReadCookie(tokens.CreateSessionTicket(user))); // a ticket is not a cookie
        Assert.Null(tokens.ReadCookie("forged"));

        var ticket = tokens.CreateDownloadTicket(new MediaDownloadTicket(user, "/uploads/video-render/a.mp4", "a.mp4"));
        Assert.Equal("/uploads/video-render/a.mp4", tokens.ReadDownloadTicket(ticket)!.Url);
        Assert.Null(tokens.ReadDownloadTicket(ticket[..^4] + "AAAA"));
    }

    [Fact]
    public async Task Direct_link_is_refused_and_video_element_with_session_is_served()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<MediaAccessTokens>();
        var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var app = new ApplicationBuilder(provider);
        MediaAccessEndpoints.UseProtectedMedia(app, config);
        app.Run(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
        var pipeline = app.Build();
        var cookie = provider.GetRequiredService<MediaAccessTokens>().CreateCookie(Guid.NewGuid());

        async Task<int> Send(string? dest, string? cookieValue)
        {
            var ctx = new DefaultHttpContext { RequestServices = provider };
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/uploads/video-render/final.mp4";
            if (dest is not null) { ctx.Request.Headers["Sec-Fetch-Dest"] = dest; ctx.Request.Headers["Sec-Fetch-Site"] = "same-origin"; }
            if (cookieValue is not null) ctx.Request.Headers.Cookie = $"{MediaAccessRules.SessionCookieName}={cookieValue}";
            await pipeline(ctx);
            return ctx.Response.StatusCode;
        }

        Assert.Equal(403, await Send("document", cookie));   // copied link opened in the browser
        Assert.Equal(403, await Send("video", null));        // another browser / logged out
        Assert.Equal(403, await Send(null, cookie));         // download manager reusing the cookie
        Assert.Equal(200, await Send("video", cookie));      // TodoX page's <video>
    }

    [Fact]
    public void File_locator_never_leaves_its_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "todox-media-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(web, "uploads"));
        File.WriteAllText(Path.Combine(web, "uploads", "a.mp4"), "x");
        File.WriteAllText(Path.Combine(root, "secret.json"), "{}");
        try
        {
            var env = new FakeEnv { ContentRootPath = root, WebRootPath = web };
            var locator = new MediaFileLocator(new ConfigurationBuilder().Build(), env);
            Assert.NotNull(locator.Resolve("/uploads/a.mp4"));
            Assert.Null(locator.Resolve("/uploads/../../secret.json"));
            Assert.Null(locator.Resolve("/uploads/%2e%2e/%2e%2e/secret.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("/media/landing/video-samples/videos/47ce377b3a654d6eb43c66a33984d02f.mp4", "/media", true)]
    [InlineData("/media/landing/video-samples/posters/a.webp", "/media", false)]
    [InlineData("/video/abc", "/media", false)]
    public void Landing_protects_sample_video_files(string path, string requestPath, bool expected)
        => Assert.Equal(expected, LandingMediaGuard.IsProtectedVideoPath(path, requestPath));

    [Theory]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", true)]
    [InlineData("Zalo", true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130", false)]
    public void Landing_keeps_share_previews_for_link_bots(string userAgent, bool expected)
        => Assert.Equal(expected, LandingMediaGuard.IsPreviewBot(userAgent));

    [Fact]
    public void Dashboard_download_buttons_go_through_todox_download()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string Read(string rel) => File.ReadAllText(Path.Combine(root, "TodoX.Web", rel), Encoding.UTF8);

        Assert.DoesNotContain("Href=\"@p.FinalVideoUrl\"", Read("Components/Pages/RVideo/RVideoV2Result.razor"));
        Assert.DoesNotContain("Href=\"@VideoUrl\"", Read("Components/Dialogs/RVideoV2MediaPreviewDialog.razor"));
        Assert.DoesNotContain("Href=\"@_job.Workflow.FinalOutput.PublicUrl\"", Read("Components/Pages/TimelapseJobDetail.razor"));
        Assert.Contains("DownloadVideoAsync", Read("Components/Pages/RVideo/RVideoV2Result.razor"));
        Assert.Contains("MediaAccess.SyncSessionAsync", Read("Components/Layout/MainLayout.razor"));
        Assert.Contains("todox-media-guard.js", Read("Components/App.razor"));
        Assert.Contains("UseProtectedMedia", Read("Program.cs"));
    }

    private sealed class FakeEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
    }
}
