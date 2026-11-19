using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;
using TodoX.ReupVideo.TikTok;

namespace TodoX.ReupVideo.Core;

public sealed record RunSummary(int NewLinks, int Posted, int Failed, int Inactive, bool Cancelled)
{
    public override string ToString() =>
        $"{NewLinks} link mới · đăng thành công {Posted} · lỗi {Failed} · link không còn active {Inactive}{(Cancelled ? " · đã dừng" : "")}";
}

/// <param name="Trigger">Shown in the history: "Bấm nút", "Theo lịch", "Reup lại".</param>
/// <param name="OnlyVideoIds">Reup just these links (manual retry / selected links).</param>
public sealed record RunOptions(string Trigger = "Bấm nút", IReadOnlyCollection<string>? OnlyVideoIds = null);

/// <summary>
/// One run: update links from the ticked TikTok channels, then for up to N not-yet-posted links: check the link is
/// still active, download it once, and post it (video + TikTok text/hashtags) to every ticked fanpage that has not got it.
/// </summary>
public sealed class ReupRunner
{
    private readonly AppPaths _paths;
    private readonly VideoLibrary _library;
    private readonly ITikTokClient _tiktok;
    private readonly IFacebookPublisher _facebook;
    private readonly Func<string, string> _unprotect;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly RunHistory? _history;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ReupRunner(AppPaths paths, VideoLibrary library, ITikTokClient tiktok, IFacebookPublisher facebook,
        Func<string, string>? unprotect = null, Func<TimeSpan, CancellationToken, Task>? delay = null, RunHistory? history = null)
    {
        _history = history;
        _paths = paths;
        _library = library;
        _tiktok = tiktok;
        _facebook = facebook;
        _unprotect = unprotect ?? SecretProtector.Unprotect;
        _delay = delay ?? Task.Delay;
    }

    public bool IsRunning => _running.CurrentCount == 0;

    /// <summary>Scans the ticked channels only (button "Cập nhật link"). Returns the number of new links.</summary>
    public async Task<int> UpdateLinksAsync(AppSettings settings, IProgress<string> log, CancellationToken ct)
    {
        var added = 0;
        var sources = settings.TikTokSources.Where(s => s.Enabled).ToList();
        if (sources.Count == 0)
        {
            log.Report("Chưa tick kênh TikTok nào để lấy video.");
            return 0;
        }

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            log.Report($"Quét {source}...");
            try
            {
                var links = await _tiktok.ListChannelAsync(source.Username, settings.Post.ScanLimitPerChannel, ct);
                var count = _library.AddDiscovered(links.Select(l => new VideoItem
                {
                    VideoId = l.VideoId,
                    Url = l.Url,
                    Author = string.IsNullOrEmpty(l.Author) ? source.Username : l.Author,
                    SourceId = source.Id,
                    Description = l.Description,
                    PublishedAt = l.PublishedAt
                }));
                added += count;
                source.LastScanAt = DateTime.Now;
                source.LastScanResult = $"{links.Count} link, {count} mới";
                log.Report($"  {source}: {links.Count} link, {count} link mới.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                source.LastScanAt = DateTime.Now;
                source.LastScanResult = "Lỗi: " + ex.Message;
                log.Report($"  {source}: lỗi quét kênh — {ex.Message}");
            }
        }

        return added;
    }

    /// <summary>
    /// Full run: update links, then post. Only one run at a time; a second call returns null.
    /// With <see cref="RunOptions.OnlyVideoIds"/> ("Reup lại" / reup selected links) no channel is scanned, the per-run
    /// limit and the retry limit are ignored, and only those links are posted.
    /// </summary>
    public async Task<RunSummary?> RunAsync(AppSettings settings, IProgress<string> log, CancellationToken ct, RunOptions? options = null)
    {
        options ??= new RunOptions();
        if (!await _running.WaitAsync(0, ct))
        {
            log.Report("Đang có một lượt chạy khác, bỏ qua.");
            return null;
        }

        var run = new RunRecord { Trigger = options.Trigger };
        var manualPick = options.OnlyVideoIds is { Count: > 0 };
        var maxAttempts = manualPick ? int.MaxValue : settings.Post.MaxAttempts;
        try
        {
            log.Report($"===== Bắt đầu lượt reup ({run.Trigger}) =====");
            if (!manualPick)
            {
                run.NewLinks = await UpdateLinksAsync(settings, log, ct);
            }

            var pages = settings.FacebookPages.Where(p => p.Enabled).ToList();
            if (pages.Count == 0)
            {
                log.Report("Chưa tick fanpage Facebook nào để đăng.");
                run.Error = "Chưa chọn fanpage";
                return Summary(run);
            }

            var tokens = pages.ToDictionary(p => p.PageId, p => _unprotect(p.ProtectedToken));
            foreach (var page in pages.Where(p => tokens[p.PageId].Length == 0))
            {
                log.Report($"Fanpage {page.Name}: chưa có token (hoặc token lưu ở máy/tài khoản Windows khác) — bỏ qua.");
            }

            foreach (var page in pages.Where(p => p.TokenExpired))
            {
                log.Report($"Fanpage {page.Name}: token đã hết hạn — bỏ qua cho tới khi cập nhật token (Cài đặt → Fanpage Facebook).");
            }

            pages = pages.Where(p => tokens[p.PageId].Length > 0 && !p.TokenExpired).ToList();
            if (pages.Count == 0)
            {
                log.Report("Không còn fanpage nào dùng được token để đăng.");
                run.Error = "Token fanpage hết hạn";
                return Summary(run);
            }

            foreach (var source in ReupTargets.Unmapped(settings))
            {
                log.Report($"Kênh {source} chưa chọn fanpage để đăng (Cài đặt → Kênh → Fanpage) — bỏ qua.");
            }

            var queue = manualPick
                ? _library.Snapshot().Where(v => options.OnlyVideoIds!.Contains(v.VideoId) && v.State is not (VideoState.Inactive or VideoState.Removed)
                        && ReupTargets.For(v, settings, pages).Any(p => v.NeedsPostTo(p.PageId, maxAttempts))).ToList()
                : Candidates(settings, pages, DateTime.Now);
            var limit = manualPick ? int.MaxValue : Math.Max(1, settings.Post.MaxVideosPerRun);
            log.Report(manualPick
                ? $"Reup {queue.Count} link đã chọn."
                : $"Có {queue.Count} link chờ reup; lượt này đăng tối đa {limit} video.");

            var done = 0;
            var firstPost = true;
            foreach (var item in queue)
            {
                if (done >= limit)
                {
                    break;
                }

                ct.ThrowIfCancellationRequested();
                var targets = ReupTargets.For(item, settings, pages).Where(p => item.NeedsPostTo(p.PageId, maxAttempts)).ToList();
                if (targets.Count == 0)
                {
                    continue;
                }

                log.Report($"Kiểm tra link {item.Url}");
                var check = await _tiktok.CheckAsync(item.Url, ct);
                if (check.State == LinkCheck.Unavailable)
                {
                    run.Inactive++;
                    _library.Update(item.VideoId, v => { v.State = VideoState.Inactive; v.StateNote = check.Message; v.CheckedAt = DateTime.Now; });
                    run.Entries.Add(Entry(RunEntryKind.Inactive, item, null, null, check.Message));
                    log.Report($"  Link không còn active ({check.Message}) — không đăng.");
                    continue;
                }

                if (check.State == LinkCheck.Error || check.Info is null)
                {
                    // Counted as a failed reup so the link shows in the "reup lại" list.
                    FailAll(run, item, targets, "Không kiểm tra được link TikTok: " + check.Message);
                    log.Report($"  ✗ Không kiểm tra được link ({check.Message}) — đưa vào danh sách reup lại.");
                    done++;
                    continue;
                }

                var info = check.Info;
                _library.Update(item.VideoId, v =>
                {
                    v.State = VideoState.Active;
                    v.StateNote = null;
                    v.CheckedAt = DateTime.Now;
                    v.Description = info.Description;
                    v.Tags = info.Tags.ToList();
                    v.DurationSeconds = info.DurationSeconds;
                    v.PublishedAt ??= info.PublishedAt;
                });

                var attempted = new HashSet<string>();
                string? file = null;
                try
                {
                    log.Report("  Tải video...");
                    file = await _tiktok.DownloadAsync(item.Url, item.VideoId, _paths.DownloadsDir, ct);
                    var caption = CaptionBuilder.Build(settings.Post.CaptionTemplate, info, settings.Post.ExtraHashtags);
                    foreach (var page in targets)
                    {
                        if (page.TokenExpired)
                        {
                            continue; // token found expired earlier in this run
                        }

                        if (!firstPost)
                        {
                            await _delay(TimeSpan.FromSeconds(Math.Max(0, settings.Post.DelaySecondsBetweenPosts)), ct);
                        }

                        firstPost = false;
                        log.Report($"  Đăng lên {page.Name} ({(page.PostKind == FacebookPostKind.Reels ? "Reels" : "Video")})...");
                        var result = await _facebook.PublishAsync(page.PageId, tokens[page.PageId], page.PostKind, file, caption, info.DurationSeconds, ct);
                        attempted.Add(page.PageId);
                        if (!result.Ok && FacebookClient.IsTokenError(result.Error))
                        {
                            // Not the video's fault: the link stays "chờ đăng" for this page (no retry used up),
                            // and the page is skipped until its token is renewed.
                            page.TokenExpired = true;
                            page.LastCheckAt = DateTime.Now;
                            page.LastCheckResult = "Token hết hạn";
                            pages.Remove(page);
                            run.Failed++;
                            run.Entries.Add(Entry(RunEntryKind.Failed, item, page, null, "Token fanpage hết hạn — cập nhật token rồi link sẽ tự được đăng lại. " + result.Error));
                            log.Report($"  ✗ {page.Name}: token hết hạn — bỏ qua fanpage này cho tới khi cập nhật token.");
                            continue;
                        }

                        _library.RecordPost(item.VideoId, page.PageId, result.Ok, result.Id, result.Error);
                        if (result.Ok)
                        {
                            run.Posted++;
                            var note = result.UsedKind != page.PostKind ? "Video dài hơn 90 giây nên đăng dạng Video" : result.UsedKind == FacebookPostKind.Reels ? "Reels" : "Video";
                            run.Entries.Add(Entry(RunEntryKind.Posted, item, page, result.Id, note));
                            log.Report($"  ✓ Đã đăng {page.Name} ({note}) — id {result.Id}");
                        }
                        else
                        {
                            run.Failed++;
                            run.Entries.Add(Entry(RunEntryKind.Failed, item, page, null, result.Error));
                            log.Report($"  ✗ {page.Name}: {result.Error}");
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    FailAll(run, item, targets.Where(p => !attempted.Contains(p.PageId)), ex.Message);
                    log.Report($"  ✗ {ex.Message} — đưa vào danh sách reup lại.");
                }
                finally
                {
                    if (file is not null && !settings.Post.KeepDownloadedFiles)
                    {
                        TryDelete(file);
                    }
                }

                done++;
            }

            return Summary(run);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Cancelled = true;
            log.Report("Đã dừng theo yêu cầu.");
            return Summary(run);
        }
        catch (Exception ex)
        {
            run.Error = ex.Message;
            log.Report("Lỗi: " + ex.Message);
            return Summary(run);
        }
        finally
        {
            run.EndedAt = DateTime.Now;
            _history?.Save(run);
            if (!manualPick)
            {
                settings.Schedule.LastRunAt = DateTime.Now;
            }

            log.Report("===== Kết thúc lượt reup =====");
            _running.Release();
        }
    }

    private void FailAll(RunRecord run, VideoItem item, IEnumerable<FacebookPage> pages, string? error)
    {
        foreach (var page in pages)
        {
            run.Failed++;
            _library.RecordPost(item.VideoId, page.PageId, false, null, error);
            run.Entries.Add(Entry(RunEntryKind.Failed, item, page, null, error));
        }
    }

    private static RunEntry Entry(RunEntryKind kind, VideoItem item, FacebookPage? page, string? facebookId, string? message)
    {
        var text = (item.Description ?? "").Replace('\n', ' ');
        return new RunEntry
        {
            Kind = kind,
            VideoId = item.VideoId,
            Url = item.Url,
            Author = item.Author,
            Text = text.Length > 200 ? text[..200] + "…" : text,
            PageId = page?.PageId,
            PageName = page?.Name,
            FacebookId = facebookId,
            Message = message
        };
    }

    private static RunSummary Summary(RunRecord run) => new(run.NewLinks, run.Posted, run.Failed, run.Inactive, run.Cancelled);

    /// <summary>
    /// Links still to post: from a ticked channel (or added by hand), not inactive/skipped, missing on at least one
    /// ticked fanpage (failures retried up to MaxAttempts), not older than the age limit; newest or oldest first.
    /// </summary>
    internal List<VideoItem> Candidates(AppSettings settings, IReadOnlyList<FacebookPage> pages, DateTime now)
    {
        var enabledSources = settings.TikTokSources.Where(s => s.Enabled).Select(s => s.Id).ToHashSet();
        var minDate = settings.Post.SkipOlderThanDays > 0 ? now.AddDays(-settings.Post.SkipOlderThanDays) : (DateTime?)null;
        var list = _library.Snapshot()
            .Where(v => v.State is VideoState.New or VideoState.Active)
            .Where(v => v.SourceId is null || enabledSources.Contains(v.SourceId))
            .Where(v => minDate is null || v.PublishedAt is null || v.PublishedAt >= minDate)
            .Where(v => ReupTargets.For(v, settings, pages).Any(p => v.NeedsPostTo(p.PageId, settings.Post.MaxAttempts)));

        Func<VideoItem, DateTime> order = v => v.PublishedAt ?? v.DiscoveredAt;
        return (settings.Post.NewestFirst ? list.OrderByDescending(order) : list.OrderBy(order)).ToList();
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // file in use: removed on a later run
        }
    }
}
