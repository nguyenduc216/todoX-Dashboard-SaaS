using TodoX.ReupVideo;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;
using TodoX.ReupVideo.TikTok;

namespace TodoX.ReupVideo.Tests;

public class TikTokLinksTests
{
    [Theory]
    [InlineData("@todox.vn", "todox.vn")]
    [InlineData("todox_shop", "todox_shop")]
    [InlineData("https://www.tiktok.com/@todox.vn?lang=vi", "todox.vn")]
    [InlineData("https://www.tiktok.com/@abc/video/7300000000000000000", "abc")]
    [InlineData("not a channel!", null)]
    [InlineData("", null)]
    public void ParsesUsernames(string input, string? expected) => Assert.Equal(expected, TikTokLinks.ParseUsername(input));

    [Fact]
    public void ParsesVideoUrl()
    {
        var parsed = TikTokLinks.ParseVideoUrl("https://www.tiktok.com/@shop.a/video/7311111111111111111?is_from_webapp=1");
        Assert.Equal(("shop.a", "7311111111111111111"), parsed);
        Assert.Null(TikTokLinks.ParseVideoUrl("https://vt.tiktok.com/ZSabc/"));
    }

    [Fact]
    public void ExtractsAndStripsHashtags()
    {
        const string text = "Món ngon mỗi ngày #ănvặt #food #FOOD giá chỉ 20k&#39; #fyp";
        Assert.Equal(["ănvặt", "food", "fyp"], TikTokLinks.ExtractHashtags(text));
        Assert.Equal("Món ngon mỗi ngày giá chỉ 20k&#39;", TikTokLinks.StripHashtags(text));
    }
}

public class CaptionBuilderTests
{
    private static TikTokVideoInfo Info(string description, params string[] tags) =>
        new("1", "https://www.tiktok.com/@a/video/1", "a", "A", description, tags, 30, null);

    [Fact]
    public void DefaultTemplateKeepsTikTokTextAndHashtags()
    {
        Assert.Equal("Xin chào #todox #video", CaptionBuilder.Build("{caption}", Info("Xin chào #todox #video", "todox", "video"), ""));
    }

    [Fact]
    public void AddsTagsMissingFromTextAndExtraHashtagsWithoutDuplicates()
    {
        var caption = CaptionBuilder.Build("{caption}", Info("Xin chào #todox", "todox", "review"), "todox, #ban_hang");
        Assert.Equal("Xin chào #todox\n\n#review\n#ban_hang", caption);
    }

    [Fact]
    public void TemplatePlaceholders()
    {
        var caption = CaptionBuilder.Build("{text}\nNguồn: {author}\n{hashtags}", Info("Hay quá #a #b"), "");
        Assert.Equal("Hay quá\nNguồn: @a\n#a #b", caption);
    }
}

public class SchedulePlannerTests
{
    [Fact]
    public void ParsesTimes()
    {
        var times = SchedulePlanner.ParseTimes(["20:00, 8:00; 8h30 25:00 abc"], out var invalid);
        Assert.Equal([new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(20, 0, 0)], times);
        Assert.Equal(["25:00", "abc"], invalid);
    }

    [Fact]
    public void DailyNextRunAndDue()
    {
        var schedule = new ScheduleSettings { Mode = ScheduleMode.DailyTimes, DailyTimes = ["08:00", "20:00"] };
        var morning = new DateTime(2026, 11, 19, 7, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 19, 8, 0, 0), SchedulePlanner.NextRun(schedule, morning));
        Assert.False(SchedulePlanner.IsDue(schedule, morning));

        var justAfter = new DateTime(2026, 11, 19, 8, 0, 30);
        Assert.True(SchedulePlanner.IsDue(schedule, justAfter));
        schedule.LastRunAt = justAfter;
        Assert.False(SchedulePlanner.IsDue(schedule, justAfter.AddSeconds(20)));

        var night = new DateTime(2026, 11, 19, 21, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 20, 8, 0, 0), SchedulePlanner.NextRun(schedule, night));
    }

    [Fact]
    public void IntervalAndManual()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0);
        var schedule = new ScheduleSettings { Mode = ScheduleMode.Interval, IntervalMinutes = 60, LastRunAt = now.AddMinutes(-30) };
        Assert.Equal(now.AddMinutes(30), SchedulePlanner.NextRun(schedule, now));
        Assert.False(SchedulePlanner.IsDue(schedule, now));
        Assert.True(SchedulePlanner.IsDue(schedule, now.AddMinutes(31)));

        Assert.Null(SchedulePlanner.NextRun(new ScheduleSettings { Mode = ScheduleMode.Manual }, now));
        Assert.False(SchedulePlanner.IsDue(new ScheduleSettings { Mode = ScheduleMode.Manual }, now));
    }
}

public class YtDlpParsingTests
{
    [Fact]
    public void ParsesFlatPlaylist()
    {
        const string json = """
            {"_type":"playlist","entries":[
              {"_type":"url","id":"7311111111111111111","url":"https://www.tiktok.com/@shop/video/7311111111111111111","title":"Hello #a","timestamp":1763500000},
              {"_type":"url","id":"7322222222222222222"},
              {"_type":"url","id":"notanid"}
            ]}
            """;
        var links = YtDlpClient.ParsePlaylist(json, "shop");
        Assert.Equal(2, links.Count);
        Assert.Equal("Hello #a", links[0].Description);
        Assert.NotNull(links[0].PublishedAt);
        Assert.Equal("https://www.tiktok.com/@shop/video/7322222222222222222", links[1].Url);
    }

    [Fact]
    public void ParsesVideoInfoWithHashtags()
    {
        const string json = """
            {"id":"7311111111111111111","description":"Ngon #ănvặt #food","uploader":"shop","channel":"Shop A",
             "duration":42.5,"timestamp":1763500000,"tags":["food","sale"],
             "webpage_url":"https://www.tiktok.com/@shop/video/7311111111111111111"}
            """;
        var info = YtDlpClient.ParseInfo(json, "https://www.tiktok.com/@shop/video/7311111111111111111")!;
        Assert.Equal("shop", info.Author);
        Assert.Equal("Shop A", info.AuthorName);
        Assert.Equal(42.5, info.DurationSeconds);
        Assert.Equal(["food", "sale", "ănvặt"], info.Tags);
    }

    [Theory]
    [InlineData("[TikTok] 1: Video not available, status code 10204", LinkCheck.Unavailable)]
    [InlineData("This video is private", LinkCheck.Unavailable)]
    [InlineData("Your IP address is blocked from accessing this post", LinkCheck.Error)]
    [InlineData("Unable to download webpage: timed out", LinkCheck.Error)]
    [InlineData("Unable to extract universal data", LinkCheck.Error)]
    public void ClassifiesErrors(string error, LinkCheck expected) => Assert.Equal(expected, YtDlpClient.ClassifyError(error));

    [Fact]
    public void ShortErrorTakesLastErrorLine() =>
        Assert.Equal("boom", YtDlpClient.ShortError("[info] x\nWARNING: y\nERROR: boom\n"));
}

public class ReupRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reup-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (Exception) { }
    }

    private sealed class FakeTikTok : ITikTokClient
    {
        public Dictionary<string, List<TikTokLink>> Channels { get; } = [];
        public HashSet<string> Dead { get; } = [];
        public List<string> Downloaded { get; } = [];

        public Task<IReadOnlyList<TikTokLink>> ListChannelAsync(string username, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TikTokLink>>(Channels.GetValueOrDefault(username, []).Take(limit).ToList());

        public Task<TikTokCheckResult> CheckAsync(string url, CancellationToken ct)
        {
            var id = url.Split('/')[^1];
            return Task.FromResult(Dead.Contains(id)
                ? new TikTokCheckResult(LinkCheck.Unavailable, null, "Video not available")
                : new TikTokCheckResult(LinkCheck.Active, new TikTokVideoInfo(id, url, "shop", null, $"Video {id} #tag{id}", [], 20, null), null));
        }

        public Task<string> DownloadAsync(string url, string videoId, string folder, CancellationToken ct)
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, videoId + ".mp4");
            File.WriteAllText(file, "x");
            Downloaded.Add(videoId);
            return Task.FromResult(file);
        }
    }

    private sealed class FakeFacebook : IFacebookPublisher
    {
        public List<(string Page, string Caption)> Posts { get; } = [];
        public HashSet<string> FailingPages { get; } = [];

        public Task<FacebookPostResult> PublishAsync(string pageId, string pageToken, FacebookPostKind kind, string videoFile, string description, double? durationSeconds, CancellationToken ct)
        {
            Assert.Equal("token-" + pageId, pageToken);
            if (FailingPages.Contains(pageId))
            {
                return Task.FromResult(new FacebookPostResult(false, null, "denied", kind));
            }

            Posts.Add((pageId, description));
            return Task.FromResult(new FacebookPostResult(true, "fb-" + Posts.Count, null, kind));
        }
    }

    private static TikTokLink Link(string id, int daysAgo) =>
        new(id, TikTokLinks.VideoUrlOf("shop", id), "shop", "d", DateTime.Now.AddDays(-daysAgo));

    private (ReupRunner Runner, VideoLibrary Library, AppSettings Settings, FakeTikTok TikTok, FakeFacebook Facebook) Setup()
    {
        var paths = new AppPaths(_root);
        var library = VideoLibrary.Load(paths.LibraryFile);
        var tiktok = new FakeTikTok();
        tiktok.Channels["shop"] = [Link("1001", 1), Link("1002", 2), Link("1003", 3)];
        tiktok.Channels["other"] = [Link("2001", 0)];
        var facebook = new FakeFacebook();
        var settings = new AppSettings
        {
            TikTokSources = [new TikTokSource { Username = "shop", PageIds = ["P1", "P2"] }, new TikTokSource { Username = "other", Enabled = false, PageIds = ["P1", "P2"] }],
            FacebookPages = [new FacebookPage { PageId = "P1", Name = "Page 1", ProtectedToken = "P1" }, new FacebookPage { PageId = "P2", Name = "Page 2", ProtectedToken = "P2" }],
            Post = { MaxVideosPerRun = 2, DelaySecondsBetweenPosts = 0 }
        };
        var runner = new ReupRunner(paths, library, tiktok, facebook, unprotect: p => "token-" + p, delay: (_, _) => Task.CompletedTask);
        return (runner, library, settings, tiktok, facebook);
    }

    [Fact]
    public async Task PostsNewestActiveLinksOnceToEachTickedPage()
    {
        var (runner, library, settings, tiktok, facebook) = Setup();
        tiktok.Dead.Add("1001");

        var first = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(3, first!.NewLinks); // unticked channel "other" is not scanned
        Assert.Equal(1, first.Inactive);
        Assert.Equal(4, first.Posted); // 1002 and 1003, each to P1 and P2
        Assert.Equal(VideoState.Inactive, library.Find("1001")!.State);
        Assert.Contains(facebook.Posts, p => p.Caption == "Video 1002 #tag1002");
        Assert.Null(library.Find("2001"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "downloads")));

        // second run: nothing left to post, posted links are filtered out
        var second = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        Assert.Equal(0, second!.Posted);
        Assert.Equal(4, facebook.Posts.Count);

        // reloading from disk keeps the history
        var reloaded = VideoLibrary.Load(Path.Combine(_root, "library.json"));
        Assert.True(reloaded.Find("1002")!.IsPostedTo("P1"));
    }

    [Fact]
    public async Task NewPageGetsOldLinksAndUntickedPageGetsNothing()
    {
        var (runner, _, settings, _, facebook) = Setup();
        settings.FacebookPages[1].Enabled = false;
        settings.Post.MaxVideosPerRun = 10;
        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        Assert.All(facebook.Posts, p => Assert.Equal("P1", p.Page));
        Assert.Equal(3, facebook.Posts.Count);

        settings.FacebookPages[1].Enabled = true;
        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        Assert.Equal(3, facebook.Posts.Count(p => p.Page == "P2"));
    }

    [Fact]
    public async Task FailedPostsRetryUntilMaxAttempts()
    {
        var (runner, library, settings, _, facebook) = Setup();
        facebook.FailingPages.Add("P2");
        settings.Post.MaxAttempts = 2;
        settings.Post.MaxVideosPerRun = 10;

        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        var third = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);

        Assert.Equal(0, third!.Failed);
        Assert.Equal(2, library.Find("1002")!.PostFor("P2")!.Attempts);
        Assert.Equal(3, facebook.Posts.Count(p => p.Page == "P1"));
    }

    [Fact]
    public async Task HistoryRecordsEachRunAndManualRetryIgnoresAttemptLimit()
    {
        var paths = new AppPaths(_root);
        var history = RunHistory.Load(paths.HistoryFile);
        var library = VideoLibrary.Load(paths.LibraryFile);
        var tiktok = new FakeTikTok();
        tiktok.Channels["shop"] = [Link("1001", 1)];
        tiktok.Dead.Add("9999");
        var facebook = new FakeFacebook();
        facebook.FailingPages.Add("P1");
        var settings = new AppSettings
        {
            TikTokSources = [new TikTokSource { Username = "shop", PageIds = ["P1"] }],
            FacebookPages = [new FacebookPage { PageId = "P1", Name = "Page 1", ProtectedToken = "P1" }],
            Post = { MaxAttempts = 1, DelaySecondsBetweenPosts = 0 }
        };
        var runner = new ReupRunner(paths, library, tiktok, facebook, p => "token-" + p, (_, _) => Task.CompletedTask, history);

        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None, new RunOptions("Theo lịch"));
        var pages = settings.FacebookPages;
        Assert.True(library.Find("1001")!.HasFailed(pages));
        Assert.False(library.Find("1001")!.IsPending(pages));

        // automatic runs stop after MaxAttempts...
        var again = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);
        Assert.Equal(0, again!.Failed);

        // ...but "Reup lại" retries the failed link and it succeeds
        facebook.FailingPages.Clear();
        var retry = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None, new RunOptions("Reup lại", ["1001"]));
        Assert.Equal(1, retry!.Posted);
        Assert.False(library.Find("1001")!.HasFailed(pages));

        var runs = RunHistory.Load(paths.HistoryFile).Snapshot();
        Assert.Equal(3, runs.Count);
        Assert.Equal("Reup lại", runs[0].Trigger);
        Assert.Equal(RunEntryKind.Posted, runs[0].Entries.Single().Kind);
        Assert.Equal("Theo lịch", runs[2].Trigger);
        Assert.Equal("denied", runs[2].Entries.Single().Message);
    }

    [Fact]
    public async Task EachChannelGoesOnlyToItsMappedFanpages()
    {
        var (runner, _, settings, tiktok, facebook) = Setup();
        settings.TikTokSources[0].PageIds = ["P1"];           // shop → Page 1 only
        settings.TikTokSources[1].Enabled = true;
        settings.TikTokSources[1].PageIds = ["P2"];           // other → Page 2 only
        settings.Post.MaxVideosPerRun = 10;
        tiktok.Channels["other"] = [new TikTokLink("2001", TikTokLinks.VideoUrlOf("other", "2001"), "other", "d", DateTime.Now)];

        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);

        Assert.Equal(3, facebook.Posts.Count(p => p.Page == "P1"));
        Assert.All(facebook.Posts.Where(p => p.Page == "P1"), p => Assert.Contains("100", p.Caption));
        Assert.Single(facebook.Posts, p => p.Page == "P2");
        Assert.Contains("2001", facebook.Posts.Single(p => p.Page == "P2").Caption);
    }

    [Fact]
    public async Task ChannelWithoutFanpageIsNotPosted()
    {
        var (runner, library, settings, _, facebook) = Setup();
        settings.TikTokSources[0].PageIds = [];
        var summary = await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);

        Assert.Equal(3, summary!.NewLinks);
        Assert.Empty(facebook.Posts);
        Assert.Single(ReupTargets.Unmapped(settings));
        Assert.Empty(ReupTargets.For(library.Find("1001")!, settings, settings.FacebookPages));
    }

    [Fact]
    public async Task SkipsLinksOlderThanLimitAndSkippedLinks()
    {
        var (runner, library, settings, _, facebook) = Setup();
        settings.Post.SkipOlderThanDays = 2;
        settings.Post.MaxVideosPerRun = 10;
        await runner.UpdateLinksAsync(settings, new Progress<string>(), CancellationToken.None);
        library.Update("1002", v => v.State = VideoState.Skipped);

        await runner.RunAsync(settings, new Progress<string>(), CancellationToken.None);

        Assert.All(facebook.Posts, p => Assert.Contains("1001", p.Caption));
    }
}
