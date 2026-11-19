using System.Text.Json;

namespace TodoX.ReupVideo.Storage;

public enum VideoState
{
    /// <summary>Found on TikTok, not checked yet.</summary>
    New,
    /// <summary>Checked: the link is live (public, not deleted).</summary>
    Active,
    /// <summary>TikTok says the video is deleted/private/unavailable: never posted.</summary>
    Inactive,
    /// <summary>The user chose not to post it.</summary>
    Skipped
}

public enum PostState
{
    Posted,
    Failed
}

public sealed class PostRecord
{
    public string PageId { get; set; } = "";
    public PostState State { get; set; }
    public string? FacebookId { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>One TikTok video link and where it has been posted.</summary>
public sealed class VideoItem
{
    public string VideoId { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>TikTok username (without "@") the link was found on.</summary>
    public string Author { get; set; } = "";
    public string? SourceId { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public double? DurationSeconds { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.Now;
    public DateTime? CheckedAt { get; set; }
    public VideoState State { get; set; } = VideoState.New;
    public string? StateNote { get; set; }
    public List<PostRecord> Posts { get; set; } = [];

    public PostRecord? PostFor(string pageId) => Posts.FirstOrDefault(p => p.PageId == pageId);

    public bool IsPostedTo(string pageId) => PostFor(pageId)?.State == PostState.Posted;

    /// <summary>"Chưa reup": live link with at least one ticked fanpage that has never received it.</summary>
    public bool IsPending(IEnumerable<FacebookPage> pages) =>
        State is VideoState.New or VideoState.Active && pages.Any(p => PostFor(p.PageId) is null);

    /// <summary>"Reup lỗi": live link whose last reup to a ticked fanpage failed — the "reup lại" list.</summary>
    public bool HasFailed(IEnumerable<FacebookPage> pages) =>
        State is VideoState.New or VideoState.Active && pages.Any(p => PostFor(p.PageId)?.State == PostState.Failed);

    public bool IsPostedAnywhere => Posts.Any(p => p.State == PostState.Posted);

    /// <summary>Whether this video still has to go to <paramref name="pageId"/> (not posted, not given up on).</summary>
    public bool NeedsPostTo(string pageId, int maxAttempts)
    {
        var post = PostFor(pageId);
        return post is null || (post.State == PostState.Failed && post.Attempts < maxAttempts);
    }
}

/// <summary>
/// Local list of every TikTok link seen, so a link is posted to each fanpage only once.
/// Stored as JSON; all access goes through one lock because the scheduler and the window share it.
/// </summary>
public sealed class VideoLibrary
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<VideoItem> _items;

    public event Action? Changed;

    private VideoLibrary(string file, List<VideoItem> items)
    {
        _file = file;
        _items = items;
    }

    public static VideoLibrary Load(string file)
    {
        List<VideoItem> items = [];
        try
        {
            if (File.Exists(file))
            {
                items = JsonSerializer.Deserialize<List<VideoItem>>(File.ReadAllText(file), AppSettings.Json) ?? [];
            }
        }
        catch (Exception)
        {
            AppSettings.TryBackup(file);
        }

        return new VideoLibrary(file, items);
    }

    public static VideoLibrary InMemory() => new("", []);

    public IReadOnlyList<VideoItem> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToList();
        }
    }

    public VideoItem? Find(string videoId)
    {
        lock (_gate)
        {
            return _items.FirstOrDefault(i => i.VideoId == videoId);
        }
    }

    /// <summary>Adds links not seen before; returns how many were new.</summary>
    public int AddDiscovered(IEnumerable<VideoItem> found)
    {
        var added = 0;
        lock (_gate)
        {
            foreach (var item in found)
            {
                if (string.IsNullOrEmpty(item.VideoId) || _items.Any(i => i.VideoId == item.VideoId))
                {
                    continue;
                }

                _items.Add(item);
                added++;
            }
        }

        if (added > 0)
        {
            Save();
        }

        return added;
    }

    /// <summary>Applies a change to one item under the lock and saves.</summary>
    public void Update(string videoId, Action<VideoItem> change)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.VideoId == videoId);
            if (item is null)
            {
                return;
            }

            change(item);
        }

        Save();
    }

    public void RecordPost(string videoId, string pageId, bool ok, string? facebookId, string? error)
    {
        Update(videoId, item =>
        {
            var post = item.PostFor(pageId);
            if (post is null)
            {
                post = new PostRecord { PageId = pageId };
                item.Posts.Add(post);
            }

            post.Attempts++;
            post.State = ok ? PostState.Posted : PostState.Failed;
            post.FacebookId = facebookId;
            post.Error = error;
            post.At = DateTime.Now;
        });
    }

    /// <summary>Clears post history for a video so it can be posted again.</summary>
    public void ResetPosts(string videoId) => Update(videoId, item =>
    {
        item.Posts.Clear();
        if (item.State == VideoState.Skipped)
        {
            item.State = VideoState.New;
        }
    });

    public void Remove(string videoId)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.VideoId == videoId);
        }

        Save();
    }

    private void Save()
    {
        if (_file.Length > 0)
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_items, AppSettings.Json);
            }

            lock (_saveGate)
            {
                var temp = _file + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _file, overwrite: true);
            }
        }

        Changed?.Invoke();
    }
}
