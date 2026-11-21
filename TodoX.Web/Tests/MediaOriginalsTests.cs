using TodoX.Landing.Services;
using TodoX.Web.Services.Landing;
using TodoX.Web.Services.Media;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Uploaded videos keep their full-quality original for downloads; the sites play the 720p copy.</summary>
public sealed class MediaOriginalsTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("/media/landing/video-samples/videos/" + Id + ".mp4", "/media/landing/video-samples/originals/" + Id + ".mp4")]
    [InlineData("/media/customer-videos/videos/" + Id + ".mp4?v=2", "/media/customer-videos/originals/" + Id + ".mp4")]
    [InlineData("https://dashboard.todox.vn/media/landing/short-video/videos/" + Id + ".webm", "https://dashboard.todox.vn/media/landing/short-video/originals/" + Id + ".mp4")]
    public void Original_sits_beside_the_web_copy(string webCopy, string firstCandidate)
    {
        Assert.Equal(firstCandidate, MediaOriginals.Candidates(webCopy)[0]);
        Assert.Equal(MediaOriginals.Candidates(webCopy), LandingVideoDownload.OriginalCandidates(webCopy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/uploads/video-render/202611/final-abc.mp4")]
    [InlineData("/media/landing/video-samples/posters/" + Id + ".jpg")]
    [InlineData("/media/landing/video-samples/videos/../" + Id + ".mp4")]
    public void Other_files_have_no_original(string? url)
    {
        Assert.Empty(MediaOriginals.Candidates(url));
        Assert.Empty(LandingVideoDownload.OriginalCandidates(url));
    }

    [Fact]
    public void Download_name_follows_the_file_sent()
        => Assert.Equal("Video mẫu.webm", MediaOriginals.WithExtensionOf("Video mẫu.mp4", @"C:\m\originals\" + Id + ".webm"));

    [Fact]
    public void Todox_vn_download_prefers_the_original_and_falls_back_to_the_web_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "tdx-orig-" + Guid.NewGuid().ToString("N"));
        var videos = Path.Combine(root, "landing", "video-samples", "videos");
        var originals = Path.Combine(root, "landing", "video-samples", "originals");
        Directory.CreateDirectory(videos);
        Directory.CreateDirectory(originals);
        File.WriteAllBytes(Path.Combine(videos, Id + ".mp4"), [1]);
        var url = "/media/landing/video-samples/videos/" + Id + ".mp4";
        string? Pick() => LandingVideoDownload.OriginalCandidates(url).Select(u => LandingVideoDownload.ResolveFile(u, root, "/media")).FirstOrDefault(f => f is not null)
                          ?? LandingVideoDownload.ResolveFile(url, root, "/media");
        try
        {
            Assert.Equal(Path.Combine(videos, Id + ".mp4"), Pick());
            File.WriteAllBytes(Path.Combine(originals, Id + ".mp4"), [1, 2, 3]);
            Assert.Equal(Path.Combine(originals, Id + ".mp4"), Pick());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Keep_original_moves_the_upload_into_originals()
    {
        var root = Path.Combine(Path.GetTempPath(), "tdx-keep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, Id + ".upload");
        File.WriteAllBytes(source, [9, 9]);
        try
        {
            LandingIntroVideoMediaService.KeepOriginal(source, Path.Combine(root, "originals"), Id + ".mp4");
            Assert.False(File.Exists(source));
            Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(root, "originals", Id + ".mp4")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Dashboard_download_endpoint_and_uploads_use_the_original()
    {
        static string Read(string file) => File.ReadAllText(Path.Combine(FindWebRoot(), file));
        Assert.Contains("MediaOriginals.Candidates(ticket.Url)", Read("Services/Media/MediaAccess.cs"));
        Assert.Contains("KeepOriginal(", Read("Services/Landing/LandingIntroVideoMediaService.cs"));
        Assert.Contains("KeepOriginal(", Read("Services/CustomerVideos/CustomerVideoMediaService.cs"));
    }

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Web", "TodoX.Web.csproj"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "TodoX.Web");
    }
}
