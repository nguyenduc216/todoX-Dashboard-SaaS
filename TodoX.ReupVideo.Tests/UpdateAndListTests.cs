using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Tests;

public class AppUpdaterTests
{
    private const string Sha = "01FB9D8913AA0000000000000000000000000000000000000000000000000000";

    private static string Json(string version, string fileUrl = "/downloads/reupvideo/todoX-ReupVideo-1.0.2.exe", string sha = Sha, string file = "todoX-ReupVideo-1.0.2.exe") =>
        $$"""{"version":"{{version}}","size":52358500,"fileName":"{{file}}","fileUrl":"{{fileUrl}}","sha256":"{{sha}}"}""";

    [Fact]
    public void NewerVersionIsOffered()
    {
        var info = AppUpdater.Parse(Json("1.0.2"), new Version(1, 0, 1, 0), "https://todox.vn");
        Assert.NotNull(info);
        Assert.Equal("https://todox.vn/downloads/reupvideo/todoX-ReupVideo-1.0.2.exe", info!.Url);
        Assert.Equal(new Version(1, 0, 2), info.Version);
    }

    [Theory]
    [InlineData("1.0.1")]
    [InlineData("1.0.0")]
    [InlineData("not-a-version")]
    public void SameOrOlderOrBrokenVersionIsIgnored(string version) =>
        Assert.Null(AppUpdater.Parse(Json(version), new Version(1, 0, 1, 0), "https://todox.vn"));

    [Fact]
    public void OnlyTodoXFilesWithHashAreAccepted()
    {
        var current = new Version(1, 0, 1);
        Assert.Null(AppUpdater.Parse(Json("2.0.0", fileUrl: "https://evil.example/x.exe"), current, "https://todox.vn"));
        Assert.Null(AppUpdater.Parse(Json("2.0.0", sha: "abc"), current, "https://todox.vn"));
        Assert.Null(AppUpdater.Parse(Json("2.0.0", file: "../evil.exe"), current, "https://todox.vn"));
        Assert.Null(AppUpdater.Parse("""{"version":"2.0.0"}""", current, "https://todox.vn"));
    }
}

public class LinkListTests
{
    [Fact]
    public void WithoutFanpageUnpostedLinksStillCountAsPending()
    {
        var item = new VideoItem { VideoId = "1", State = VideoState.New };
        Assert.True(item.IsPending([]));
        item.Posts.Add(new PostRecord { PageId = "P", State = PostState.Posted });
        Assert.False(item.IsPending([]));
    }

    [Fact]
    public void RemovedLinksAreNotAddedBackOrPosted()
    {
        var library = VideoLibrary.InMemory();
        library.AddDiscovered([new VideoItem { VideoId = "1" }, new VideoItem { VideoId = "2" }]);
        library.MarkRemoved(["1"]);

        Assert.Equal(0, library.AddDiscovered([new VideoItem { VideoId = "1" }]));
        Assert.Equal(VideoState.Removed, library.Find("1")!.State);
        Assert.False(library.Find("1")!.IsPending([]));
        Assert.True(library.Find("2")!.IsPending([]));
    }
}
