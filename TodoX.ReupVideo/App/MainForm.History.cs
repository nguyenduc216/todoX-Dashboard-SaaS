using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Storage;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>Lịch sử reup: every run (top) and what happened to each link in the selected run (bottom).</summary>
public sealed partial class MainForm
{
    private readonly ListView _runs = Theme.List();
    private readonly ListView _runEntries = Theme.List();
    private readonly Label _runTitle = new() { AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 12, 12, 0) };
    private Button _retryRunButton = null!;

    private Control BuildHistoryPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Ground, Padding = P(24, 10, 24, 12) };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Ground };
        top.Controls.Add(new Label { Text = "CÁC LẦN REUP", AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 12, 12, 6) });
        top.Controls.Add(Theme.Text("Bấm vào một lần chạy để xem từng link: đăng thành công, lỗi (kèm lý do) hoặc link không còn active. Nhấp đúp vào link để mở bài Facebook / link TikTok.", Muted, 9, 700));

        Columns(_runs, ("Thời gian", 130), ("Kiểu chạy", 130), ("Link mới", 70), ("Thành công", 85), ("Lỗi", 60), ("Không active", 90), ("Thời lượng", 80), ("Đã đăng lên fanpage", -1), ("Ghi chú", 220));
        _runs.Dock = DockStyle.Fill;
        _runs.MultiSelect = false;
        _runs.SelectedIndexChanged += (_, _) => FillRunEntries();

        var entryTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Ground };
        _retryRunButton = Theme.Button("Reup lại các link lỗi của lần này", primary: true);
        _retryRunButton.Click += async (_, _) => await RetryRunAsync();
        var openTikTok = Theme.Button("Mở link TikTok");
        openTikTok.Click += (_, _) => { foreach (var e in SelectedEntries()) OpenUrl(e.Url); };
        var openFacebook = Theme.Button("Mở bài Facebook");
        openFacebook.Click += (_, _) => { foreach (var e in SelectedEntries().Where(x => x.FacebookId is not null)) OpenUrl(FacebookUrl(e.FacebookId!)); };
        _runButtons.Add(_retryRunButton);
        entryTools.Controls.AddRange([_runTitle, _retryRunButton, openTikTok, openFacebook]);

        Columns(_runEntries, ("Giờ", 70), ("Link TikTok", 130), ("Kênh", 110), ("Nội dung", -1), ("Fanpage", 200), ("Kết quả", 110), ("Chi tiết", 260));
        _runEntries.Dock = DockStyle.Fill;
        _runEntries.DoubleClick += (_, _) =>
        {
            if (SelectedEntries().FirstOrDefault() is { } e)
            {
                OpenUrl(e.FacebookId is { } id ? FacebookUrl(id) : e.Url);
            }
        };

        // Runs on top (40%), the selected run's links below (60%).
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Ground, ColumnCount = 1, RowCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Ground, Margin = Padding.Empty };
        bottom.Controls.Add(_runEntries);
        bottom.Controls.Add(entryTools);
        _runs.Margin = P(0, 0, 0, 8);
        grid.Controls.Add(_runs, 0, 0);
        grid.Controls.Add(bottom, 0, 1);

        page.Controls.Add(grid);
        page.Controls.Add(top);
        return page;
    }

    private void RefreshHistory()
    {
        if (_runs.Columns.Count == 0)
        {
            return;
        }

        var selectedId = (_runs.SelectedItems.Count > 0 ? _runs.SelectedItems[0].Tag as RunRecord : null)?.Id;
        _runs.BeginUpdate();
        _runs.Items.Clear();
        foreach (var run in _history.Snapshot())
        {
            var duration = run.EndedAt is { } end ? end - run.StartedAt : TimeSpan.Zero;
            var note = run.Cancelled ? "Đã dừng giữa chừng" : run.Error ?? (run.Posted + run.Failed + run.Inactive == 0 ? "Không có link cần reup" : "");
            var item = new ListViewItem([
                run.StartedAt.ToString("HH:mm dd/MM/yyyy"),
                run.Trigger,
                run.NewLinks.ToString(),
                run.Posted.ToString(),
                run.Failed.ToString(),
                run.Inactive.ToString(),
                duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} ph {duration.Seconds} s" : $"{duration.Seconds} s",
                PagesPosted(run),
                note
            ]) { Tag = run, UseItemStyleForSubItems = false, Selected = run.Id == selectedId };
            item.SubItems[3].ForeColor = run.Posted > 0 ? Green : Muted;
            item.SubItems[4].ForeColor = run.Failed > 0 ? Red : Muted;
            _runs.Items.Add(item);
        }

        _runs.EndUpdate();
        if (_runs.SelectedItems.Count == 0 && _runs.Items.Count > 0)
        {
            _runs.Items[0].Selected = true;
        }

        FillRunEntries();
    }

    /// <summary>"Page A: 2 video, Page B: 1 video" for a run.</summary>
    private static string PagesPosted(RunRecord run)
    {
        var groups = run.Entries.Where(e => e.Kind == RunEntryKind.Posted)
            .GroupBy(e => e.PageName ?? e.PageId ?? "")
            .Select(g => $"{g.Key}: {g.Count()} video").ToList();
        return groups.Count == 0 ? "—" : string.Join(", ", groups);
    }

    private RunRecord? SelectedRun() => _runs.SelectedItems.Count > 0 ? _runs.SelectedItems[0].Tag as RunRecord : null;

    private List<RunEntry> SelectedEntries() =>
        _runEntries.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<RunEntry>().ToList();

    private void FillRunEntries()
    {
        var run = SelectedRun();
        _runTitle.Text = run is null ? "CHI TIẾT" : $"CHI TIẾT LẦN {run.StartedAt:HH:mm dd/MM}";
        _retryRunButton.Visible = run is not null && run.Entries.Any(e => e.Kind == RunEntryKind.Failed);
        _runEntries.BeginUpdate();
        _runEntries.Items.Clear();
        // Grouped by link (in the order the links were handled), then one row per fanpage.
        var order = (run?.Entries ?? []).Select(e => e.VideoId).Distinct().Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        string? lastVideo = null;
        foreach (var e in (run?.Entries ?? []).OrderBy(e => order[e.VideoId]).ThenBy(e => e.At))
        {
            var firstOfLink = e.VideoId != lastVideo;
            lastVideo = e.VideoId;
            var (text, color) = e.Kind switch
            {
                RunEntryKind.Posted => ("✓ Đã đăng", Green),
                RunEntryKind.Failed => ("✗ Lỗi", Red),
                _ => ("Không active", Muted)
            };
            var item = new ListViewItem([
                e.At.ToString("HH:mm:ss"),
                firstOfLink ? "…" + (e.VideoId.Length > 8 ? e.VideoId[^8..] : e.VideoId) : "  ↳",
                firstOfLink ? "@" + e.Author : "",
                firstOfLink ? e.Text ?? "" : "",
                e.PageName ?? (e.Kind == RunEntryKind.Inactive ? "(không đăng page nào)" : ""),
                text,
                e.Kind == RunEntryKind.Posted ? $"{e.Message} · {FacebookUrl(e.FacebookId ?? "")}" : e.Message ?? ""
            ]) { Tag = e, UseItemStyleForSubItems = false };
            item.SubItems[5].ForeColor = color;
            _runEntries.Items.Add(item);
        }

        _runEntries.EndUpdate();
    }

    private async Task RetryRunAsync()
    {
        if (SelectedRun() is not { } run)
        {
            return;
        }

        var ids = run.Entries.Where(e => e.Kind == RunEntryKind.Failed).Select(e => e.VideoId).Distinct().ToList();
        await RunAsync(new RunOptions("Reup lại", ids));
    }

    /// <summary>Every run entry of the selected link on the home page, newest first.</summary>
    private void ShowLinkHistory()
    {
        var link = SelectedLinks().FirstOrDefault();
        if (link is null)
        {
            return;
        }

        var entries = _history.Snapshot()
            .SelectMany(r => r.Entries.Where(e => e.VideoId == link.VideoId).Select(e => (Run: r, Entry: e)))
            .OrderByDescending(x => x.Entry.At)
            .ToList();

        using var dialog = new Form
        {
            Text = "Lịch sử reup — @" + link.Author,
            BackColor = Ground,
            ForeColor = Ink,
            Font = F(9.5f),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(S(960), S(600)),
            MinimizeBox = false,
            ShowIcon = false,
            Padding = P(16, 12, 16, 12)
        };
        var head = Theme.Text($"{link.Url}\n{(link.Description ?? "").Replace('\n', ' ')}", Muted, 9, 920);
        head.Dock = DockStyle.Top;
        var statusTitle = new Label { Text = "TÌNH TRẠNG TRÊN TỪNG FANPAGE", AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Dock = DockStyle.Top, Padding = P(0, 8, 0, 4) };
        var status = Theme.List();
        status.Dock = DockStyle.Top;
        status.Height = S(200);
        Columns(status, ("Fanpage", 220), ("Tình trạng", 250), ("Thời gian", 130), ("Chi tiết / bài Facebook", -1));
        FillPageStatus(status, _library.Find(link.VideoId) ?? link);
        status.DoubleClick += (_, _) =>
        {
            if (status.SelectedItems.Count > 0 && status.SelectedItems[0].Tag is PageReupStatus { Post.FacebookId: { } id })
            {
                OpenUrl(FacebookUrl(id));
            }
        };
        var logTitle = new Label { Text = "CÁC LẦN REUP CỦA LINK", AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Dock = DockStyle.Top, Padding = P(0, 10, 0, 4) };
        var list = Theme.List();
        list.Dock = DockStyle.Fill;
        Columns(list, ("Thời gian", 130), ("Lần chạy", 120), ("Kết quả", 100), ("Fanpage", 160), ("Chi tiết", -1));
        foreach (var (run, e) in entries)
        {
            var item = new ListViewItem([
                e.At.ToString("HH:mm dd/MM/yyyy"),
                run.Trigger,
                e.Kind switch { RunEntryKind.Posted => "✓ Đã đăng", RunEntryKind.Failed => "✗ Lỗi", _ => "Không active" },
                e.PageName ?? "",
                e.Kind == RunEntryKind.Posted ? $"{e.Message} · id {e.FacebookId}" : e.Message ?? ""
            ]) { Tag = e, UseItemStyleForSubItems = false };
            item.SubItems[2].ForeColor = e.Kind == RunEntryKind.Posted ? Green : e.Kind == RunEntryKind.Failed ? Red : Muted;
            list.Items.Add(item);
        }

        if (entries.Count == 0)
        {
            list.Items.Add(new ListViewItem(["", "", "Chưa reup lần nào", "", ""]));
        }

        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is RunEntry e)
            {
                OpenUrl(e.FacebookId is { } id ? FacebookUrl(id) : e.Url);
            }
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(logTitle);
        dialog.Controls.Add(status);
        dialog.Controls.Add(statusTitle);
        dialog.Controls.Add(head);
        dialog.ShowDialog(this);
    }
}
