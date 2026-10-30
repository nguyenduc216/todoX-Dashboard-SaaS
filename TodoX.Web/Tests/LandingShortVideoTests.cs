using Microsoft.Extensions.Options;
using TodoX.Web.Services.Landing;
using TodoX.Web.Services.SharedMedia;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingShortVideoTests
{
    private const string Video = "/media/landing/short-video/videos/0123456789abcdef0123456789abcdef.mp4";
    private const string Poster = "/media/landing/short-video/posters/0123456789abcdef0123456789abcdef.webp";

    [Fact]
    public void DefaultSettingsAreValidAndMatchPublishedPriceList()
    {
        var settings = new LandingShortVideoSettings();
        LandingShortVideoRepository.Validate(settings);
        Assert.True(settings.ShowOnLanding);
        Assert.False(settings.IntroEnabled);
        Assert.Collection(settings.Packages,
            p => { Assert.Equal(3_000_000, p.Price); Assert.Equal(30, p.VideoCount + p.BonusVideos); },
            p => { Assert.Equal(6_000_000, p.Price); Assert.Equal(65, p.VideoCount + p.BonusVideos); Assert.True(p.Featured); },
            p => { Assert.Equal(10_000_000, p.Price); Assert.Equal(100, p.VideoCount + p.BonusVideos); });
    }

    [Fact]
    public void EnabledIntroRequiresVideo()
        => Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings { IntroEnabled = true }));

    [Fact]
    public void GeneratedShortVideoMediaIsAccepted()
        => LandingShortVideoRepository.Validate(new LandingShortVideoSettings { IntroEnabled = true, VideoUrl = Video, PosterUrl = Poster });

    [Theory]
    [InlineData("/media/landing/intro/videos/0123456789abcdef0123456789abcdef.mp4")]
    [InlineData("https://evil.example/video.mp4")]
    [InlineData("/media/landing/short-video/videos/../secret.mp4")]
    [InlineData(Video + "?x=1")]
    [InlineData(Poster)]
    public void UntrustedOrWrongFolderVideoIsRejected(string url)
        => Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings { VideoUrl = url }));

    [Fact]
    public void PackageListBoundsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings { Packages = [] }));
        var tooMany = Enumerable.Range(0, LandingShortVideoRepository.MaxPackages + 1)
            .Select(i => new LandingShortVideoPackage { Name = $"Gói {i}", VideoCount = 1 }).ToList();
        Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings { Packages = tooMany }));
    }

    [Theory]
    [InlineData("", 1000, 10, 0)]
    [InlineData("Gói", -1, 10, 0)]
    [InlineData("Gói", 1000, 0, 0)]
    [InlineData("Gói", 1000, 10, -1)]
    public void InvalidPackageIsRejected(string name, long price, int videos, int bonus)
        => Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings
        {
            Packages = [new() { Name = name, Price = price, VideoCount = videos, BonusVideos = bonus }]
        }));

    [Fact]
    public void NormalizeTrimsTextAndDropsBlankFeatures()
    {
        var settings = new LandingShortVideoSettings
        {
            Packages = [new() { Name = "  Gói Start ", Subtitle = " x ", VideoCount = 1, Features = [" a ", "", "   ", "b"] }]
        };
        LandingShortVideoRepository.Normalize(settings);
        var package = Assert.Single(settings.Packages);
        Assert.Equal("Gói Start", package.Name);
        Assert.Equal(["a", "b"], package.Features);
        LandingShortVideoRepository.Validate(settings);
    }

    [Fact]
    public void TooManyFeaturesAreRejected()
        => Assert.Throws<ArgumentException>(() => LandingShortVideoRepository.Validate(new LandingShortVideoSettings
        {
            Packages = [new() { Name = "Gói", VideoCount = 1, Features = Enumerable.Repeat("x", LandingShortVideoRepository.MaxFeatures + 1).ToList() }]
        }));

    [Fact]
    public void StoredPackagesRoundTripAndCorruptJsonFallsBackToEmpty()
    {
        var packages = LandingShortVideoRepository.ParsePackages(
            """[{"name":"Gói A","subtitle":"s","price":1500000,"videoCount":12,"bonusVideos":3,"features":["f1"],"featured":true}]""");
        var package = Assert.Single(packages);
        Assert.Equal(("Gói A", 1_500_000L, 12, 3, true), (package.Name, package.Price, package.VideoCount, package.BonusVideos, package.Featured));
        Assert.Empty(LandingShortVideoRepository.ParsePackages("{not json"));
        Assert.Empty(LandingShortVideoRepository.ParsePackages(null));
    }

    [Fact]
    public void ShortVideoMediaIsStoredUnderItsOwnFolderAndIntroPathsAreUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"short-video-test-{Guid.NewGuid():N}");
        var paths = new SharedMediaPathService(Options.Create(new SharedMediaOptions { StorageRoot = root, RequestPath = "/media" }));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "landing", "short-video", "videos"), paths.GetLandingMediaPhysicalFolder("short-video", "videos"));
        Assert.Equal("/media/landing/short-video/posters/a.webp", paths.GetLandingMediaPublicUrl("short-video", "posters", "a.webp"));
        Assert.Equal(paths.GetIntroPhysicalFolder("videos"), paths.GetLandingMediaPhysicalFolder("intro", "videos"));
        Assert.Equal(paths.GetIntroPublicUrl("videos", "a.mp4"), paths.GetLandingMediaPublicUrl("intro", "videos", "a.mp4"));
        Assert.Throws<ArgumentException>(() => paths.GetLandingMediaPhysicalFolder("../industries", "videos"));
        Assert.Throws<ArgumentException>(() => paths.GetLandingMediaPublicUrl("short-video", "videos", "../a.mp4"));
    }

    [Fact]
    public void LandingExposesShortVideoPageAndHomepageEntry()
    {
        var landing = FindLandingRoot();
        var program = File.ReadAllText(Path.Combine(landing, "Program.cs"));
        Assert.Contains("app.MapGet(\"/video-ngan\"", program);
        Assert.Contains("\"/api/landing/short-video\"", program);

        var index = File.ReadAllText(Path.Combine(landing, "wwwroot", "index.html"));
        Assert.Contains("href=\"/video-ngan\" data-short-video-entry", index);
        Assert.Contains("Video truyền thông", index);
        Assert.Contains("js/landing-short-video.js", index);

        var page = File.ReadAllText(Path.Combine(landing, "wwwroot", "video-ngan.html"));
        Assert.Contains("id=\"pricing\"", page);
        Assert.Contains("id=\"process\"", page);
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(page, "class=\"sv-step-no\"").Count);
        Assert.Contains("/js/short-video.js", page);
    }

    [Theory]
    [InlineData("bat-dong-san.html", "real-estate.js")]
    [InlineData("video-ngan.html", "short-video.js")]
    public void DemoPlayButtonIsIconOnlyAndScriptDoesNotExpectLabel(string page, string script)
    {
        var root = Path.Combine(FindLandingRoot(), "wwwroot");
        Assert.DoesNotContain("<span>Phát video</span>", File.ReadAllText(Path.Combine(root, page)));
        var js = File.ReadAllText(Path.Combine(root, "js", script));
        Assert.DoesNotContain("play.querySelector('span')", js);
        Assert.Contains("play.setAttribute('aria-label', label)", js);
    }

    [Fact]
    public void HomepagePlayButtonUsesGoldStyle()
    {
        var css = File.ReadAllText(Path.Combine(FindLandingRoot(), "wwwroot", "css", "landing.css"));
        Assert.Contains("background:linear-gradient(135deg,var(--gold-2),var(--gold)); border:1px solid rgba(255,216,107,.85);", css);
        Assert.DoesNotContain("background:rgba(255,255,255,.88); color:#111", css);
    }

    private static string FindLandingRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TodoX.Landing");
            if (File.Exists(Path.Combine(candidate, "TodoX.Landing.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("TodoX.Landing project not found.");
    }
}
