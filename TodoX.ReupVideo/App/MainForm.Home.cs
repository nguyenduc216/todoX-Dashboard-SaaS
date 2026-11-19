using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Storage;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>Trang chính: figures, the links behind each figure, reup now / reup again, and the schedule.</summary>
public sealed partial class MainForm
{
    private enum HomeView { Pending, Failed, Posted, Inactive, All }

    private readonly Dictionary<HomeView, (Panel Card, Label Number)> _cards = [];
    private readonly Label _listTitle = new() { AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 12, 12, 0) };
    private readonly ListView _links = Theme.List();
    private readonly Label _scheduleLabel = new() { AutoSize = true, ForeColor = Ink, Font = F(10), Margin = P(4, 12, 8, 0) };
    /// <summary>What the run is doing right now (one line; the full log goes only to the log files).</summary>
    private readonly Label _activityLabel = new() { AutoSize = true, ForeColor = Muted, Font = F(9.5f), Margin = P(0, 2, 0, 6), UseMnemonic = false };
    private readonly List<Button> _runButtons = [];
    private Button _stopButton = null!;
    private Button _retryAllButton = null!;
    private HomeView _view = HomeView.Pending;

    private Control BuildHomePage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Ground, Padding = P(24, 10, 24, 12) };

        // --- run bar
        var runBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Ground };
        var run = Theme.Button("▶  Reup ngay", primary: true, big: true);
        run.Click += async (_, _) => await RunAsync(new RunOptions("Bấm nút"));
        var update = Theme.Button("Cập nhật link TikTok", big: true);
        update.Click += async (_, _) => await UpdateLinksOnlyAsync();
        _stopButton = Theme.Button("■  Dừng", big: true);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopRun();
        _runButtons.AddRange([run, update]);
        var changeSchedule = new LinkLabel { Text = "Đổi lịch", AutoSize = true, LinkColor = Yellow, ActiveLinkColor = Yellow, Font = F(10), Margin = P(0, 12, 0, 0) };
        changeSchedule.LinkClicked += (_, _) => OpenSettingsTab(SettingsTabSchedule);
        runBar.Controls.AddRange([run, update, _stopButton, _scheduleLabel, changeSchedule]);
        runBar.SetFlowBreak(changeSchedule, true);
        runBar.Controls.Add(_activityLabel);

        // --- figure cards
        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Ground, Padding = P(0, 10, 0, 0) };
        cards.Controls.Add(Card(HomeView.Pending, "Link chưa reup", Yellow));
        cards.Controls.Add(Card(HomeView.Failed, "Reup lỗi · chờ reup lại", Red));
        cards.Controls.Add(Card(HomeView.Posted, "Đã reup", Green));
        cards.Controls.Add(Card(HomeView.Inactive, "Link không còn active", Muted));
        cards.Controls.Add(Card(HomeView.All, "Tất cả link đã quét", Ink));

        // --- list toolbar
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Ground, Padding = P(0, 4, 0, 0) };
        var reupSelected = Theme.Button("Reup link đã chọn", primary: true);
        reupSelected.Click += async (_, _) => await ReupSelectedAsync();
        _retryAllButton = Theme.Button("Reup lại tất cả link lỗi");
        _retryAllButton.Click += async (_, _) => await RetryAllFailedAsync();
        var linkHistory = Theme.Button("Lịch sử link");
        linkHistory.Click += (_, _) => ShowLinkHistory();
        var open = Theme.Button("Mở link TikTok");
        open.Click += (_, _) => { foreach (var v in SelectedLinks()) OpenUrl(v.Url); };
        var skip = Theme.Button("Bỏ qua link");
        skip.Click += (_, _) => SkipSelected();
        _runButtons.AddRange([reupSelected, _retryAllButton]);
        tools.Controls.AddRange([_listTitle, reupSelected, _retryAllButton, linkHistory, open, skip]);

        // --- list
        Columns(_links, ("Ngày TikTok", 92), ("Kênh", 120), ("Nội dung", -1), ("Trạng thái", 220), ("Lần thử", 60), ("Fanpage", 170));
        _links.Dock = DockStyle.Fill;
        _links.DoubleClick += (_, _) => ShowLinkHistory();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Reup link này ngay", null, async (_, _) => await ReupSelectedAsync());
        menu.Items.Add("Xem lịch sử reup của link", null, (_, _) => ShowLinkHistory());
        menu.Items.Add("Mở link TikTok", null, (_, _) => { foreach (var v in SelectedLinks()) OpenUrl(v.Url); });
        menu.Items.Add("Mở bài đã đăng trên Facebook", null, (_, _) =>
        {
            foreach (var post in SelectedLinks().SelectMany(v => v.Posts).Where(p => p.State == PostState.Posted && p.FacebookId is not null))
            {
                OpenUrl(FacebookUrl(post.FacebookId!));
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Bỏ qua (không reup link này)", null, (_, _) => SkipSelected());
        menu.Items.Add("Cho phép reup lại từ đầu (xoá trạng thái đã đăng)", null, (_, _) =>
        {
            var selected = SelectedLinks();
            if (selected.Count > 0 && MessageBox.Show($"Xoá trạng thái đã đăng của {selected.Count} link? Lượt sau các link này sẽ được đăng lại lên mọi fanpage đã tick.", "todoX ReupVideo", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                selected.ForEach(v => _library.ResetPosts(v.VideoId));
            }
        });
        menu.Items.Add("Kiểm tra lại link (đặt về chờ reup)", null, (_, _) => SelectedLinks().ForEach(v => _library.Update(v.VideoId, x => { x.State = VideoState.New; x.StateNote = null; })));
        menu.Items.Add("Xoá khỏi danh sách", null, (_, _) => SelectedLinks().ForEach(v => _library.Remove(v.VideoId)));
        _links.ContextMenuStrip = menu;

        // Docking: the last added Top control sits highest.
        page.Controls.Add(_links);
        page.Controls.Add(tools);
        page.Controls.Add(cards);
        page.Controls.Add(runBar);
        SelectView(HomeView.Pending);
        return page;
    }

    private Panel Card(HomeView view, string title, Color accent)
    {
        var card = new Panel { Width = S(158), Height = S(84), BackColor = Surface, Margin = P(0, 0, 10, 6), Cursor = Cursors.Hand, Padding = P(12, 8, 8, 8) };
        var number = new Label { Text = "0", AutoSize = true, ForeColor = accent, Font = new Font("Segoe UI Semibold", 22), Location = new Point(S(10), S(4)) };
        var label = new Label { Text = title, AutoSize = false, ForeColor = Muted, Font = F(9), Location = new Point(S(12), S(48)), Size = new Size(S(140), S(32)) };
        card.Controls.Add(number);
        card.Controls.Add(label);
        foreach (var c in new Control[] { card, number, label })
        {
            c.Click += (_, _) => SelectView(view);
        }

        card.Paint += (_, e) =>
        {
            if (_view != view)
            {
                return;
            }

            using var pen = new Pen(Yellow, Math.Max(2, S(2)));
            e.Graphics.DrawRectangle(pen, 1, 1, card.Width - 2, card.Height - 2);
        };
        _cards[view] = (card, number);
        return card;
    }

    private void SelectView(HomeView view)
    {
        _view = view;
        foreach (var (card, _) in _cards.Values)
        {
            card.Invalidate();
        }

        _listTitle.Text = view switch
        {
            HomeView.Pending => "LINK CHƯA REUP",
            HomeView.Failed => "REUP LỖI — DANH SÁCH REUP LẠI",
            HomeView.Posted => "LINK ĐÃ REUP",
            HomeView.Inactive => "LINK KHÔNG CÒN ACTIVE (KHÔNG ĐĂNG)",
            _ => "TẤT CẢ LINK"
        };
        _retryAllButton.Visible = view == HomeView.Failed;
        RefreshHome();
    }

    private void RefreshHome()
    {
        if (_cards.Count == 0)
        {
            return;
        }

        var pages = EnabledPages();
        var all = _library.Snapshot();
        var counts = new Dictionary<HomeView, int>
        {
            [HomeView.Pending] = all.Count(v => v.IsPending(pages)),
            [HomeView.Failed] = all.Count(v => v.HasFailed(pages)),
            [HomeView.Posted] = all.Count(v => v.IsPostedAnywhere),
            [HomeView.Inactive] = all.Count(v => v.State == VideoState.Inactive),
            [HomeView.All] = all.Count
        };
        foreach (var (view, (_, number)) in _cards)
        {
            number.Text = counts[view].ToString("N0");
        }

        var items = all.Where(v => _view switch
            {
                HomeView.Pending => v.IsPending(pages),
                HomeView.Failed => v.HasFailed(pages),
                HomeView.Posted => v.IsPostedAnywhere,
                HomeView.Inactive => v.State == VideoState.Inactive,
                _ => true
            })
            .OrderByDescending(v => _view == HomeView.Failed ? v.Posts.Where(p => p.State == PostState.Failed).Max(p => (DateTime?)p.At) ?? v.DiscoveredAt : v.PublishedAt ?? v.DiscoveredAt)
            .Take(3000)
            .ToList();

        var selected = SelectedLinks().Select(v => v.VideoId).ToHashSet();
        _links.BeginUpdate();
        _links.Items.Clear();
        foreach (var v in items)
        {
            var (state, color) = Describe(v, pages);
            var failedPosts = v.Posts.Where(p => p.State == PostState.Failed).ToList();
            var postedNames = v.Posts.Where(p => p.State == PostState.Posted).Select(p => PageName(p.PageId)).ToList();
            var text = (v.Description ?? "").Replace('\n', ' ');
            var row = new ListViewItem([
                (v.PublishedAt ?? v.DiscoveredAt).ToString("dd/MM/yyyy"),
                "@" + v.Author,
                text.Length > 160 ? text[..160] + "…" : text,
                state,
                failedPosts.Count == 0 ? "" : failedPosts.Max(p => p.Attempts).ToString(),
                _view == HomeView.Failed ? string.Join(", ", failedPosts.Select(p => PageName(p.PageId))) : postedNames.Count == 0 ? "—" : string.Join(", ", postedNames)
            ]) { Tag = v, UseItemStyleForSubItems = false, Selected = selected.Contains(v.VideoId) };
            row.SubItems[3].ForeColor = color;
            _links.Items.Add(row);
        }

        _links.EndUpdate();
    }

    private (string Text, Color Color) Describe(VideoItem v, List<FacebookPage> pages)
    {
        if (v.State == VideoState.Inactive)
        {
            return ("Không còn active" + (v.StateNote is { Length: > 0 } n ? ": " + n : ""), Red);
        }

        if (v.State == VideoState.Skipped)
        {
            return ("Đã bỏ qua", Muted);
        }

        var failed = v.Posts.Where(p => p.State == PostState.Failed && pages.Any(pg => pg.PageId == p.PageId)).OrderByDescending(p => p.At).FirstOrDefault();
        if (failed is not null)
        {
            var auto = failed.Attempts < _settings.Post.MaxAttempts ? "tự reup lại lượt sau" : "hết lượt tự thử — bấm Reup";
            return ($"Lỗi ({auto}): {failed.Error}", Red);
        }

        if (v.IsPending(pages))
        {
            return (v.IsPostedAnywhere ? "Đã reup một phần · chờ fanpage còn lại" : "Chờ reup", Yellow);
        }

        return (v.IsPostedAnywhere ? "Đã reup" : "—", Green);
    }

    private List<VideoItem> SelectedLinks() =>
        _links.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<VideoItem>().ToList();

    private async Task ReupSelectedAsync()
    {
        var selected = SelectedLinks();
        if (selected.Count == 0)
        {
            MessageBox.Show("Chọn link trong danh sách trước (giữ Ctrl để chọn nhiều).", "todoX ReupVideo");
            return;
        }

        await RunAsync(new RunOptions(_view == HomeView.Failed ? "Reup lại" : "Reup link đã chọn", selected.Select(v => v.VideoId).ToList()));
    }

    private async Task RetryAllFailedAsync()
    {
        var pages = EnabledPages();
        var failed = _library.Snapshot().Where(v => v.HasFailed(pages)).Select(v => v.VideoId).ToList();
        if (failed.Count == 0)
        {
            MessageBox.Show("Không có link reup lỗi.", "todoX ReupVideo");
            return;
        }

        await RunAsync(new RunOptions("Reup lại", failed));
    }

    private void SkipSelected()
    {
        var selected = SelectedLinks();
        if (selected.Count > 0 && MessageBox.Show($"Bỏ qua {selected.Count} link? Các link này sẽ không được reup.", "todoX ReupVideo", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            selected.ForEach(v => _library.Update(v.VideoId, x => x.State = VideoState.Skipped));
        }
    }

    private void SetRunning(bool running)
    {
        OnUi(() =>
        {
            foreach (var b in _runButtons)
            {
                b.Enabled = !running;
            }

            _stopButton.Enabled = running;
            _tray.Text = running ? "todoX ReupVideo — đang reup" : "todoX ReupVideo";
            UpdateScheduleLabel();
        });
    }

    private void ShowActivity(string line)
    {
        // Only the latest step is shown; everything is kept in the daily log file.
        _activityLabel.Text = line.Length > 160 ? line[..160] + "…" : line;
        _activityLabel.ForeColor = line.Contains('✗') || line.Contains("Lỗi") ? Red : line.Contains('✓') ? Green : Muted;
    }

    private void UpdateScheduleLabel()
    {
        var s = _settings.Schedule;
        if (_runner.IsRunning)
        {
            _scheduleLabel.Text = "Đang reup...";
            _scheduleLabel.ForeColor = Yellow;
            return;
        }

        _scheduleLabel.ForeColor = Ink;
        var next = SchedulePlanner.NextRun(s, DateTime.Now);
        var mode = s.Mode switch
        {
            ScheduleMode.DailyTimes => "theo giờ " + string.Join(", ", SchedulePlanner.ParseTimes(s.DailyTimes, out _).Select(t => t.ToString(@"hh\:mm"))),
            ScheduleMode.Interval => $"mỗi {Math.Max(SchedulePlanner.MinIntervalMinutes, s.IntervalMinutes)} phút",
            _ => ""
        };
        _scheduleLabel.Text = next is null
            ? "hoặc bật lịch tự động để reup theo giờ"
            : $"hoặc chờ tới {next.Value:HH:mm}{(next.Value.Date == DateTime.Today ? " hôm nay" : next.Value.Date == DateTime.Today.AddDays(1) ? " ngày mai" : $" {next.Value:dd/MM}")} (tự động {mode})";
    }
}
