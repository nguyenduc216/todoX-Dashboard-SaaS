using Microsoft.Extensions.Options;
using TodoX.Web.Services.Landing;
using TodoX.Web.Services.SharedMedia;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingVideoSampleTests
{
    private const string Video = "/media/landing/video-samples/videos/0123456789abcdef0123456789abcdef.mp4";
    private const string Poster = "/media/landing/video-samples/posters/0123456789abcdef0123456789abcdef.webp";

    private static LandingVideoSample Valid() => new()
    {
        Title = "Mẫu video", Description = "Nội dung", AspectRatio = LandingVideoSampleRepository.Portrait,
        VideoUrl = Video, PosterUrl = Poster, ServiceId = Guid.NewGuid()
    };

    [Theory]
    [InlineData("9:16")]
    [InlineData("16:9")]
    public void SupportedAspectRatiosAreAccepted(string ratio)
    {
        var sample = Valid();
        sample.AspectRatio = ratio;
        LandingVideoSampleRepository.Validate(sample);
    }

    [Theory]
    [InlineData("1:1")]
    [InlineData("")]
    [InlineData("16/9")]
    public void OtherAspectRatiosAreRejected(string ratio)
    {
        var sample = Valid();
        sample.AspectRatio = ratio;
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));
    }

    [Fact]
    public void TitleIsRequiredAndTrimmed()
    {
        var sample = Valid();
        sample.Title = "   ";
        LandingVideoSampleRepository.Normalize(sample);
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));

        sample.Title = "  Tiêu đề  ";
        LandingVideoSampleRepository.Normalize(sample);
        Assert.Equal("Tiêu đề", sample.Title);
        LandingVideoSampleRepository.Validate(sample);
    }

    [Fact]
    public void LengthLimitsAreEnforced()
    {
        var title = Valid();
        title.Title = new string('x', LandingVideoSampleRepository.MaxTitleLength + 1);
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(title));

        var description = Valid();
        description.Description = new string('x', LandingVideoSampleRepository.MaxDescriptionLength + 1);
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(description));
    }

    [Fact]
    public void ServiceChoiceMapsToServiceOrOther()
    {
        var sample = Valid();
        var id = Guid.NewGuid();
        LandingVideoSampleRepository.ApplyServiceChoice(sample, id.ToString());
        Assert.Equal(id, sample.ServiceId);
        LandingVideoSampleRepository.Validate(sample);

        LandingVideoSampleRepository.ApplyServiceChoice(sample, LandingVideoSampleRepository.OtherServiceChoice);
        Assert.Null(sample.ServiceId);
        Assert.Null(sample.SampleGroup);
        Assert.Equal("Video truyền thông", sample.GroupName);
        LandingVideoSampleRepository.Validate(sample);

        LandingVideoSampleRepository.ApplyServiceChoice(sample, LandingVideoSampleRepository.TvcServiceChoice);
        Assert.Null(sample.ServiceId);
        Assert.Equal("tvc", sample.SampleGroup);
        Assert.Equal("Video TVC doanh nghiệp", sample.GroupName);
        Assert.Equal(LandingVideoSampleRepository.TvcServiceChoice, LandingVideoSampleRepository.ServiceChoiceOf(sample));
        LandingVideoSampleRepository.Validate(sample);

        // Switching back to a service clears the built-in group.
        LandingVideoSampleRepository.ApplyServiceChoice(sample, id.ToString());
        Assert.Null(sample.SampleGroup);

        sample.SampleGroup = "other";
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingServiceChoiceIsRejected(string? choice)
        => Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.ApplyServiceChoice(Valid(), choice));

    [Fact]
    public void SampleVideoLimitIs500MbAndOtherLandingLimitsAreUnchanged()
    {
        var options = new SharedMediaOptions();
        Assert.Equal(500L * 1024 * 1024, LandingIntroVideoMediaService.MaxBytes(options, LandingVideoSampleRepository.MediaArea, true));
        Assert.Equal(options.IndustrySolutions.MaxThumbnailBytes, LandingIntroVideoMediaService.MaxBytes(options, LandingVideoSampleRepository.MediaArea, false));
        Assert.Equal(200L * 1024 * 1024, LandingIntroVideoMediaService.MaxBytes(options, "intro", true));
        Assert.Equal(200L * 1024 * 1024, LandingIntroVideoMediaService.MaxBytes(options, "short-video", true));
    }

    [Fact]
    public void VideoAndPosterAreRequired()
    {
        var noVideo = Valid();
        noVideo.VideoUrl = null;
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(noVideo));

        var noPoster = Valid();
        noPoster.PosterUrl = null;
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(noPoster));
    }

    [Theory]
    [InlineData("/media/landing/short-video/videos/0123456789abcdef0123456789abcdef.mp4")]
    [InlineData("https://evil.example/video.mp4")]
    [InlineData("/media/landing/video-samples/videos/../secret.mp4")]
    [InlineData(Video + "?x=1")]
    [InlineData(Video + "\n")]
    [InlineData("/media/landing/video-samples/videos/0123456789abcdef0123456789abcdef.webp")]
    [InlineData(Poster)]
    public void UntrustedOrWrongFolderVideoIsRejected(string url)
    {
        var sample = Valid();
        sample.VideoUrl = url;
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));
    }

    [Fact]
    public void PosterInVideoFolderIsRejected()
    {
        var sample = Valid();
        sample.PosterUrl = Video;
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));
    }

    [Fact]
    public void MediaIsStoredUnderItsOwnArea()
    {
        var root = Path.Combine(Path.GetTempPath(), $"video-samples-test-{Guid.NewGuid():N}");
        var paths = new SharedMediaPathService(Options.Create(new SharedMediaOptions { StorageRoot = root, RequestPath = "/media" }));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "landing", "video-samples", "videos"), paths.GetLandingMediaPhysicalFolder(LandingVideoSampleRepository.MediaArea, "videos"));
        var url = paths.GetLandingMediaPublicUrl(LandingVideoSampleRepository.MediaArea, "videos", "0123456789abcdef0123456789abcdef.mp4");
        Assert.Equal(Video, url);
        Assert.True(LandingVideoSampleRepository.IsMediaUrl(url, "videos"));
    }

    [Fact]
    public void DashboardExposesManagementPageInLandingMenu()
    {
        var web = Path.Combine(FindRoot(), "TodoX.Web");
        var layout = File.ReadAllText(Path.Combine(web, "Components", "Layout", "MainLayout.razor"));
        Assert.Contains("Code = \"landing_video_samples\"", layout);
        Assert.Contains("Href = \"/landing/video-samples\"", layout);

        var page = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Landing", "LandingVideoSamples.razor"));
        Assert.Contains("@page \"/landing/video-samples\"", page);
        Assert.Contains("ShowAsync<LandingVideoSampleDialog>", page);
        Assert.DoesNotContain("InputFile", page);

        var dialog = File.ReadAllText(Path.Combine(web, "Components", "Dialogs", "LandingVideoSampleDialog.razor"));
        Assert.Contains("Nhóm dịch vụ video", dialog);
        Assert.Contains("LandingVideoSampleRepository.BuiltInChoices", dialog);
        Assert.Contains("ServiceChoiceOf(Source)", dialog);
        Assert.Contains("Thumbnail 9:16", dialog);
        Assert.Contains("Thumbnail 16:9", dialog);
        Assert.Contains("Ảnh chia sẻ Zalo/Facebook", dialog);
        Assert.Contains("Thông tin nội dung video", dialog);
    }

    [Fact]
    public void LandingExposesSamplePageMenuAndGroupFilter()
    {
        var landing = Path.Combine(FindRoot(), "TodoX.Landing");
        var program = File.ReadAllText(Path.Combine(landing, "Program.cs"));
        Assert.Contains("app.MapGet(\"/mau-video\"", program);
        Assert.Contains("\"/api/landing/video-samples\"", program);

        var www = Path.Combine(landing, "wwwroot");
        Assert.Contains("https://todox.vn/mau-video", File.ReadAllText(Path.Combine(www, "sitemap.xml")));

        var index = File.ReadAllText(Path.Combine(www, "index.html"));
        Assert.Contains("<a href=\"/mau-video\">Mẫu video</a>", index);
        Assert.Contains("id=\"videoSampleGroups\"", index);
        Assert.Contains("js/landing-video-samples.js", index);
        Assert.Contains("<a href=\"/mau-video\">Mẫu video</a>", File.ReadAllText(Path.Combine(www, "video-ngan.html")));
        Assert.Contains("<a href=\"/mau-video\">Mẫu video</a>", File.ReadAllText(Path.Combine(www, "bat-dong-san.html")));

        var page = File.ReadAllText(Path.Combine(www, "mau-video.html"));
        Assert.Contains("id=\"sampleFilters\"", page);
        Assert.Contains("id=\"sampleEmpty\"", page);
        Assert.Contains("id=\"sampleLead\" hidden", page);
        Assert.Contains("/js/video-samples.js", page);

        var script = File.ReadAllText(Path.Combine(www, "js", "video-samples.js"));
        Assert.Contains("video.groupCode === current", script);
        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("innerHTML", File.ReadAllText(Path.Combine(www, "js", "landing-video-samples.js")));
    }

    [Fact]
    public void ManualScriptsCreateTableAndLinkItToServiceList()
    {
        var manual = Path.Combine(FindRoot(), "database", "manual");
        var sql = File.ReadAllText(Path.Combine(manual, "todox_landing_video_samples.sql"));
        Assert.Contains("create table if not exists landing.video_samples", sql);
        Assert.Contains("aspect_ratio in ('9:16', '16:9')", sql);

        var service = File.ReadAllText(Path.Combine(manual, "todox_landing_video_samples_service.sql"));
        Assert.Contains("service_id uuid references catalog.services (id) on delete set null", service);
        Assert.Contains("alter column category_id drop not null", service);
    }

    private const string PortraitThumb = "/media/landing/video-samples/posters/11111111111111111111111111111111.jpg";
    private const string LandscapeThumb = "/media/landing/video-samples/posters/22222222222222222222222222222222.jpg";

    [Theory]
    [InlineData("9:16", PortraitThumb, LandscapeThumb, PortraitThumb)]
    [InlineData("16:9", PortraitThumb, LandscapeThumb, LandscapeThumb)]
    [InlineData("16:9", PortraitThumb, null, PortraitThumb)]
    [InlineData("9:16", null, LandscapeThumb, LandscapeThumb)]
    public void MainPosterFollowsVideoShape(string ratio, string? portrait, string? landscape, string expected)
    {
        var sample = Valid();
        sample.AspectRatio = ratio;
        sample.ThumbnailPortraitUrl = portrait;
        sample.ThumbnailLandscapeUrl = landscape;
        LandingVideoSampleRepository.Normalize(sample);
        Assert.Equal(expected, sample.PosterUrl);
        LandingVideoSampleRepository.Validate(sample);
    }

    [Fact]
    public void LegacyPosterIsKeptWhenNoThumbnailSlotIsFilled()
    {
        var sample = Valid();
        LandingVideoSampleRepository.Normalize(sample);
        Assert.Equal(Poster, sample.PosterUrl);
    }

    [Fact]
    public void ForeignImageUrlsAreRejected()
    {
        var sample = Valid();
        sample.ShareImageUrl = "https://example.com/x.jpg";
        LandingVideoSampleRepository.Normalize(sample);
        Assert.Throws<ArgumentException>(() => LandingVideoSampleRepository.Validate(sample));
    }

    [Fact]
    public void ThumbnailScriptOnlyAddsNullableColumns()
    {
        var sql = File.ReadAllText(Path.Combine(FindRoot(), LandingVideoSampleRepository.ThumbnailsScript));
        foreach (var column in new[] { "thumbnail_portrait_url", "thumbnail_landscape_url", "share_image_url" })
            Assert.Contains($"add column if not exists {column} text;", sql);
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not null", sql.Split("-- Verification")[0], StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Landing", "TodoX.Landing.csproj"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
