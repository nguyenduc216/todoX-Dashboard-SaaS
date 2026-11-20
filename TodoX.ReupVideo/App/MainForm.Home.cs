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
    private readonly ListView _links = Theme.List(checkboxes: true);
    private readonly Label _scheduleLabel = new() { AutoSize = true, ForeColor = Ink, Font = F(10), Margin = P(4, 12, 8, 0) };
    /// <summary>What the run is doing right now (one line; the full log goes only to the log files).</summary>
    private readonly Label _activityLabel = new() { AutoSize = true, ForeColor = Muted, Font = F(9.5f), Margin = P(0, 2, 0, 6), UseMnemonic = false };
    private readonly List<Button> _runButtons = [];
    private Button _stopButton = null!;
    private Button _retryAllButton = null!;
    private HomeView _view = HomeView.Pending;

    // Ticked links survive refreshes (the list is rebuilt whenever the library changes).
    private readonly HashSet<string> _ticked = [];
    private readonly CheckBox _tickAll = new() { Text = "Chọn tất cả", AutoSize = true, ForeColor = Ink, Font = F(10), Margin = P(2, 12, 14, 0), Cursor = Cursors.Hand };
    private readonly Label _tickedLabel = new() { AutoSize = true, ForeColor = Muted, Font = F(9.5f), Margin = P(0, 13, 12, 0) };
    private readonly LinkLabel _noPageHint = new() { Text = "⚠ Chưa chọn fanpage nào để đăng — bấm để thêm fanpage", AutoSize = true, LinkColor = Yellow, ActiveLinkColor = Ink, Font = F(9.5f), Margin = P(8, 13, 0, 0), Visible = false };
    private bool _fillingLinks;
    private Action _fitLinkColumns = () => { };
    // Bottom panel: the highlighted link on every fanpage (posted / waiting / failed).
    private readonly ListView _linkPages = Theme.List();
    private readonly Label _linkPagesTitle = new() { AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 8, 0, 4), UseMnemonic = false };

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
        cards.Controls.Add(Card(HomeView.Pending, "Link chờ reup", Yellow));
        cards.Controls.Add(Card(HomeView.Failed, "Reup lỗi · chờ reup lại", Red));
        cards.Controls.Add(Card(HomeView.Posted, "Đã reup đủ mọi fanpage", Green));
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
        var remove = Theme.Button("Xoá khỏi danh sách");
        remove.Click += (_, _) => RemoveSelected();
        _runButtons.AddRange([reupSelected, _retryAllButton]);
        tools.Controls.AddRange([_listTitle, reupSelected, _retryAllButton, linkHistory, open, skip, remove]);
        tools.SetFlowBreak(remove, true);
        _tickAll.CheckedChanged += TickAllChanged;
        _noPageHint.LinkClicked += (_, _) => OpenSettingsTab(_noPageHint.Tag is int tab ? tab : SettingsTabPages);
        tools.Controls.AddRange([_tickAll, _tickedLabel, _clearFilters, _noPageHint]);

        // --- list
        // Column 0 holds only the tick box; "Kênh" is sized to the longest channel name in RefreshHome.
        _fitLinkColumns = Columns(_links, ("", 30), ("Ngày TikTok", 88), ("Kênh", 140), ("Nội dung", -1), ("Trạng thái", 130), ("Đã lên", 58), ("Còn chờ đăng lên fanpage", 190));
        _links.Dock = DockStyle.Fill;
        _links.ShowItemToolTips = true; // hover a row: the whole TikTok text
        BuildFilterBar();
        _links.ItemChecked += (_, e) =>
        {
            if (_fillingLinks || e.Item.Tag is not VideoItem v)
            {
                return;
            }

            if (e.Item.Checked)
            {
                _ticked.Add(v.VideoId);
            }
            else
            {
                _ticked.Remove(v.VideoId);
            }

            UpdateTickState();
        };
        _links.ColumnClick += (_, e) =>
        {
            if (e.Column == 0)
            {
                _tickAll.Checked = !_tickAll.Checked;
            }
        };
        _links.DoubleClick += (_, _) => ShowLinkHistory();
        _links.SelectedIndexChanged += (_, _) => FillLinkPages();
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
        menu.Items.Add("Xoá khỏi danh sách", null, (_, _) => RemoveSelected());
        _links.ContextMenuStrip = menu;

        // --- the highlighted link on each fanpage
        Columns(_linkPages, ("Fanpage", 230), ("Tình trạng", 250), ("Thời gian", 130), ("Chi tiết / bài Facebook (nhấp đúp để mở)", -1));
        _linkPages.Dock = DockStyle.Fill;
        _linkPages.DoubleClick += (_, _) =>
        {
            if (_linkPages.SelectedItems.Count > 0 && _linkPages.SelectedItems[0].Tag is PageReupStatus { Post.FacebookId: { } id })
            {
                OpenUrl(FacebookUrl(id));
            }
        };
        var detail = new Panel { Dock = DockStyle.Bottom, Height = S(165), BackColor = Ground, Padding = P(0, 6, 0, 0) };
        _linkPagesTitle.Dock = DockStyle.Top;
        _linkText.Dock = DockStyle.Top;
        detail.Controls.Add(_linkPages);
        detail.Controls.Add(_linkText);
        detail.Controls.Add(_linkPagesTitle);

        // Docking: the last added Top control sits highest.
        page.Controls.Add(_links);
        page.Controls.Add(detail);
        page.Controls.Add(_filterBar);
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
            HomeView.Pending => "LINK CHỜ REUP",
            HomeView.Failed => "REUP LỖI — DANH SÁCH REUP LẠI",
            HomeView.Posted => "LINK ĐÃ LÊN ĐỦ MỌI FANPAGE",
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
        var unmapped = ReupTargets.Unmapped(_settings);
        var expired = pages.Where(p => p.TokenExpired).ToList();
        _noPageHint.Visible = pages.Count == 0 || unmapped.Count > 0 || expired.Count > 0;
        _noPageHint.Tag = expired.Count > 0 || pages.Count == 0 ? SettingsTabPages : SettingsTabMapping;
        _noPageHint.Text = expired.Count > 0
            ? $"⚠ Token fanpage {string.Join(", ", expired.Take(3).Select(p => p.Name))}{(expired.Count > 3 ? $" và {expired.Count - 3} page khác" : "")} đã hết hạn — chưa đăng được, bấm để cập nhật token"
            : pages.Count == 0
                ? "⚠ Chưa chọn fanpage nào để đăng — bấm để thêm fanpage"
                : $"⚠ Kênh {string.Join(", ", unmapped.Take(3).Select(s => "@" + s.Username))}{(unmapped.Count > 3 ? $" và {unmapped.Count - 3} kênh khác" : "")} chưa chọn fanpage để đăng — bấm để chọn";
        var all = _library.Snapshot();
        // Fanpages each link goes to (ticked fanpages mapped to its TikTok channel).
        var targetsOf = all.ToDictionary(v => v.VideoId, v => ReupTargets.For(v, _settings, pages));
        var statusOf = all.ToDictionary(v => v.VideoId, v => PageReupStatuses.For(v, _settings, pages));
        bool Complete(VideoItem v) => v.State != VideoState.Removed && PageReupStatuses.IsComplete(statusOf[v.VideoId]);
        var counts = new Dictionary<HomeView, int>
        {
            [HomeView.Pending] = all.Count(v => v.IsPending(targetsOf[v.VideoId])),
            [HomeView.Failed] = all.Count(v => v.HasFailed(targetsOf[v.VideoId])),
            [HomeView.Posted] = all.Count(Complete),
            [HomeView.Inactive] = all.Count(v => v.State == VideoState.Inactive),
            [HomeView.All] = all.Count(v => v.State != VideoState.Removed)
        };
        foreach (var (view, (_, number)) in _cards)
        {
            number.Text = counts[view].ToString("N0");
        }

        var items = all.Where(v => _view switch
            {
                HomeView.Pending => v.IsPending(targetsOf[v.VideoId]),
                HomeView.Failed => v.HasFailed(targetsOf[v.VideoId]),
                HomeView.Posted => Complete(v),
                HomeView.Inactive => v.State == VideoState.Inactive,
                _ => v.State != VideoState.Removed
            })
            .OrderByDescending(v => _view == HomeView.Failed ? v.Posts.Where(p => p.State == PostState.Failed).Max(p => (DateTime?)p.At) ?? v.DiscoveredAt : v.PublishedAt ?? v.DiscoveredAt)
            .Take(3000)
            .ToList();

        var selected = _links.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<VideoItem>().Select(v => v.VideoId).ToHashSet();
        var rows = new List<ListViewItem>();
        foreach (var v in items)
        {
            var targets = targetsOf[v.VideoId];
            var status = statusOf[v.VideoId];
            var (state, color) = Describe(v, targets);
            var onTargets = status.Count(s => s.State == PageReupState.Posted);
            var text = (v.Description ?? "").Replace('\n', ' ');
            var row = new ListViewItem([
                "",
                (v.PublishedAt ?? v.DiscoveredAt).ToString("dd/MM/yyyy"),
                "@" + v.Author,
                text.Length > 300 ? text[..300] + "…" : text,
                state,
                targets.Count == 0 ? "—" : $"{onTargets}/{targets.Count}",
                WaitingColumn(status)
            ]) { Tag = v, UseItemStyleForSubItems = false, ToolTipText = text.Length > 600 ? text[..600] + "…" : text };
            if (!MatchesFilters(row))
            {
                continue;
            }

            row.SubItems[4].ForeColor = color;
            row.SubItems[5].ForeColor = targets.Count > 0 && onTargets == targets.Count ? Green : onTargets > 0 ? Yellow : Muted;
            row.SubItems[6].ForeColor = status.Any(s => s.State is PageReupState.Failed or PageReupState.GaveUp) ? Red : Ink;
            rows.Add(row);
        }

        // Ticks only on rows that are shown, so "Chọn tất cả" / "Xoá" act on what the search found.
        _ticked.IntersectWith(rows.Select(r => ((VideoItem)r.Tag!).VideoId));
        _fillingLinks = true;
        _links.BeginUpdate();
        _links.Items.Clear();
        foreach (var row in rows)
        {
            var id = ((VideoItem)row.Tag!).VideoId;
            row.Selected = selected.Contains(id);
            row.Checked = _ticked.Contains(id);
            _links.Items.Add(row);
        }

        // "Kênh" as wide as the longest channel name, so it is never cut off.
        using (var font = F(9.5f))
        {
            var longest = items.Select(v => "@" + v.Author).DefaultIfEmpty("Kênh")
                .Max(t => TextRenderer.MeasureText(t, font).Width);
            _links.Columns[2].Width = Math.Clamp(longest + S(20), S(90), S(320));
        }

        _links.EndUpdate();
        _fillingLinks = false;
        _fitLinkColumns();
        UpdateTickState();
        FillLinkPages();
    }

    /// <summary>Bottom panel: the highlighted link (or the first one) on every fanpage.</summary>
    private void FillLinkPages()
    {
        var link = _links.SelectedItems.Count > 0 ? _links.SelectedItems[0].Tag as VideoItem
            : _links.Items.Count > 0 ? _links.Items[0].Tag as VideoItem : null;
        if (link is not null)
        {
            link = _library.Find(link.VideoId) ?? link;
        }

        _linkPagesTitle.Text = link is null
            ? "TỪNG FANPAGE CỦA LINK — CHỌN MỘT LINK Ở DANH SÁCH TRÊN"
            : $"TÌNH TRẠNG TRÊN TỪNG FANPAGE · LINK CỦA @{link.Author}";
        _linkText.Text = link is null ? "" : "Nội dung: " + Short(link.Description, 1000);
        _linkText.Height = string.IsNullOrEmpty(_linkText.Text) ? 0 : TextRenderer.MeasureText(_linkText.Text, _linkText.Font, new Size(Math.Max(S(200), _linkText.Width), 0), TextFormatFlags.WordBreak).Height + S(4);
        FillPageStatus(_linkPages, link);
    }

    private static string Short(string? text, int max)
    {
        var t = (text ?? "").Replace('\n', ' ').Trim();
        return t.Length > max ? t[..max] + "…" : t;
    }

    /// <summary>Fills a list with one row per fanpage: posted (when, post link), waiting, failed (tries, error).</summary>
    private void FillPageStatus(ListView list, VideoItem? link)
    {
        list.BeginUpdate();
        list.Items.Clear();
        if (link is not null)
        {
            var live = link.State is VideoState.New or VideoState.Active;
            var rows = PageReupStatuses.For(link, _settings, EnabledPages());
            foreach (var s in rows)
            {
                var (text, color) = s.State switch
                {
                    PageReupState.Posted => ("✓ Đã đăng", Green),
                    PageReupState.Waiting when live => ("⏳ Chờ đăng (lượt reup tới)", Yellow),
                    PageReupState.Waiting => (link.State == VideoState.Inactive ? "Không đăng — link không còn active" : "Không đăng — link đã bỏ qua", Muted),
                    PageReupState.Failed => ($"✗ Lỗi lần {s.Post!.Attempts} — tự reup lại lượt sau", Red),
                    PageReupState.GaveUp => ($"✗ Lỗi {s.Post!.Attempts} lần — bấm Reup để thử lại", Red),
                    _ => ("✓ Đã đăng (page nay không còn gán kênh này)", Muted)
                };
                var item = new ListViewItem([
                    s.PageName,
                    text,
                    s.Post is { } p ? p.At.ToString("HH:mm dd/MM/yyyy") : "",
                    s.Post switch
                    {
                        { State: PostState.Posted, FacebookId: { } id } => FacebookUrl(id),
                        { State: PostState.Failed, Error: { } e } => e,
                        _ => ""
                    }
                ]) { Tag = s, UseItemStyleForSubItems = false };
                item.SubItems[1].ForeColor = color;
                list.Items.Add(item);
            }

            if (rows.Count == 0)
            {
                list.Items.Add(new ListViewItem(["—", "Kênh này chưa được gán fanpage nào (Cài đặt → Kênh → Fanpage)", "", ""]));
            }
        }

        list.EndUpdate();
    }

    private void UpdateTickState()
    {
        var total = _links.Items.Count;
        var filtered = _filters.Values.Any(b => b.Text.Trim().Length > 0) ? " tìm thấy" : "";
        _tickedLabel.Text = _ticked.Count == 0 ? $"{total:N0} link{filtered}" : $"Đã chọn {_ticked.Count:N0}/{total:N0} link{filtered}";
        _tickedLabel.ForeColor = _ticked.Count == 0 ? Muted : Yellow;
        _tickAll.CheckedChanged -= TickAllChanged;
        _tickAll.CheckState = _ticked.Count == 0 ? CheckState.Unchecked : _ticked.Count >= total ? CheckState.Checked : CheckState.Indeterminate;
        _tickAll.CheckedChanged += TickAllChanged;
    }

    private void TickAllChanged(object? sender, EventArgs e) => TickAll(_tickAll.Checked);

    private void TickAll(bool tick)
    {
        _fillingLinks = true;
        _links.BeginUpdate();
        foreach (ListViewItem item in _links.Items)
        {
            item.Checked = tick;
            if (item.Tag is VideoItem v)
            {
                if (tick)
                {
                    _ticked.Add(v.VideoId);
                }
                else
                {
                    _ticked.Remove(v.VideoId);
                }
            }
        }

        _links.EndUpdate();
        _fillingLinks = false;
        UpdateTickState();
    }

    private void RemoveSelected()
    {
        var selected = SelectedLinks();
        if (selected.Count == 0)
        {
            MessageBox.Show("Tick chọn link cần xoá trước (hoặc \"Chọn tất cả\").", "todoX ReupVideo");
            return;
        }

        if (MessageBox.Show($"Xoá {selected.Count:N0} link khỏi danh sách?\nCác link này sẽ không được reup và không bị thêm lại ở lần quét sau.",
                "todoX ReupVideo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _library.MarkRemoved(selected.Select(v => v.VideoId).ToList());
        _ticked.Clear();
        _log.Report($"Đã xoá {selected.Count} link khỏi danh sách.");
    }

    /// <summary>Fanpages the link has not reached yet: waiting by name, failed ones with "✗".</summary>
    private static string WaitingColumn(List<PageReupStatus> status)
    {
        var names = status.Where(s => s.State is PageReupState.Waiting or PageReupState.Failed or PageReupState.GaveUp)
            .Select(s => (s.State == PageReupState.Waiting ? "" : "✗ ") + s.PageName).ToList();
        if (names.Count > 0)
        {
            return string.Join(", ", names);
        }

        return status.Any(s => s.State == PageReupState.Posted) ? "Đã lên đủ fanpage" : status.Count == 0 ? "Chưa gán fanpage" : "—";
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

        if (pages.Count == 0 && !v.IsPostedAnywhere)
        {
            return ("Chưa có fanpage", Muted);
        }

        if (v.IsPending(pages))
        {
            var done = pages.Count(p => v.IsPostedTo(p.PageId));
            return (done > 0 ? "Chờ page còn lại" : "Chờ reup", Yellow);
        }

        return (v.IsPostedAnywhere ? "Đã reup" : "—", Green);
    }

    /// <summary>Ticked links; when none is ticked, the highlighted rows.</summary>
    private List<VideoItem> SelectedLinks()
    {
        var rows = _ticked.Count > 0
            ? _links.Items.Cast<ListViewItem>().Where(i => i.Checked)
            : _links.SelectedItems.Cast<ListViewItem>();
        return rows.Select(i => i.Tag).OfType<VideoItem>().ToList();
    }

    private async Task ReupSelectedAsync()
    {
        var selected = SelectedLinks();
        if (selected.Count == 0)
        {
            MessageBox.Show("Tick chọn link trong danh sách trước (hoặc \"Chọn tất cả\").", "todoX ReupVideo");
            return;
        }

        await RunAsync(new RunOptions(_view == HomeView.Failed ? "Reup lại" : "Reup link đã chọn", selected.Select(v => v.VideoId).ToList()));
    }

    private async Task RetryAllFailedAsync()
    {
        var pages = EnabledPages();
        var failed = _library.Snapshot().Where(v => v.HasFailed(ReupTargets.For(v, _settings, pages))).Select(v => v.VideoId).ToList();
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
