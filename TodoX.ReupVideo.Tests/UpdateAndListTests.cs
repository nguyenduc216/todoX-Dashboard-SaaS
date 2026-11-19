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

public class PageStatusTests
{
    private static (AppSettings Settings, List<FacebookPage> Pages) Setup()
    {
        var a = new FacebookPage { PageId = "A", Name = "Page A", Enabled = true };
        var b = new FacebookPage { PageId = "B", Name = "Page B", Enabled = true };
        var c = new FacebookPage { PageId = "C", Name = "Page C", Enabled = true };
        var settings = new AppSettings();
        settings.FacebookPages.AddRange([a, b, c]);
        settings.TikTokSources.Add(new TikTokSource { Id = "s1", Username = "kenh1", PageIds = ["A", "B"] });
        return (settings, [a, b, c]);
    }

    [Fact]
    public void LinkPostedOnOnePageStillWaitsForTheOtherMappedPage()
    {
        var (settings, pages) = Setup();
        var video = new VideoItem { VideoId = "1", SourceId = "s1", Author = "kenh1", State = VideoState.Active };
        video.Posts.Add(new PostRecord { PageId = "A", State = PostState.Posted, FacebookId = "fb1" });

        var rows = PageReupStatuses.For(video, settings, pages);
        Assert.Equal(2, rows.Count); // C is not mapped to kenh1
        Assert.Equal(PageReupState.Posted, rows.Single(r => r.PageId == "A").State);
        Assert.Equal(PageReupState.Waiting, rows.Single(r => r.PageId == "B").State);
        Assert.False(PageReupStatuses.IsComplete(rows));
        Assert.True(video.IsPending(ReupTargets.For(video, settings, pages)));
    }

    [Fact]
    public void FailuresCountTowardsRetryLimitAndOldPagesStayVisible()
    {
        var (settings, pages) = Setup();
        settings.Post.MaxAttempts = 2;
        var video = new VideoItem { VideoId = "1", SourceId = "s1", Author = "kenh1", State = VideoState.Active };
        video.Posts.Add(new PostRecord { PageId = "A", State = PostState.Posted });
        video.Posts.Add(new PostRecord { PageId = "B", State = PostState.Failed, Attempts = 2 });
        video.Posts.Add(new PostRecord { PageId = "C", State = PostState.Posted });

        var rows = PageReupStatuses.For(video, settings, pages);
        Assert.Equal(PageReupState.GaveUp, rows.Single(r => r.PageId == "B").State);
        Assert.Equal(PageReupState.NotMapped, rows.Single(r => r.PageId == "C").State);

        video.Posts.Single(p => p.PageId == "B").State = PostState.Posted;
        Assert.True(PageReupStatuses.IsComplete(PageReupStatuses.For(video, settings, pages)));
    }
}
