using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;
using TodoX.ReupVideo.TikTok;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>
/// todoX ReupVideo window. Trang chính: figures (chưa reup / reup lỗi / đã reup) with the matching links, "Reup ngay"
/// or wait for the schedule. Lịch sử reup: every run and what happened to each link. Cài đặt: fanpages, TikTok
/// channels, schedule, posting. Closing the window while a schedule is on keeps the app in the tray.
/// </summary>
public sealed partial class MainForm : Form
{
    public static string Version => typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly VideoLibrary _library;
    private readonly RunHistory _history;
    private readonly AppLog _log;
    private readonly YtDlpClient _ytdlp;
    private readonly FacebookClient _facebook;
    private readonly ReupRunner _runner;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ground };
    private readonly FlowLayoutPanel _sidebar = new() { Dock = DockStyle.Left, BackColor = Surface, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly List<(Button Button, Control Page)> _nav = [];
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _scheduler = new() { Interval = 20_000 };
    private CancellationTokenSource? _runCts;
    private bool _exiting;
    private bool _trayHintShown;
    private readonly bool _startMinimized;

    private const int PageHome = 0, PageHistory = 1, PageSettings = 2;

    public MainForm(AppPaths paths, bool startMinimized)
    {
        _paths = paths;
        _startMinimized = startMinimized;
        _settings = AppSettings.Load(paths);
        _library = VideoLibrary.Load(paths.LibraryFile);
        _history = RunHistory.Load(paths.HistoryFile);
        _log = new AppLog(paths);
        _ytdlp = new YtDlpClient(paths, () => _settings.Post.TikTokCookiesFile, _http);
        _facebook = new FacebookClient(_http);
        _runner = new ReupRunner(paths, _library, _ytdlp, _facebook, history: _history);

        Text = "todoX ReupVideo";
        Icon = AppIcon();
        BackColor = Ground;
        ForeColor = Ink;
        Font = F(9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        var screen = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(S(1100), S(780));
        ClientSize = new Size(Math.Min(S(1100), screen.Width - S(40)), Math.Min(S(780), screen.Height - S(40)));
        MinimumSize = new Size(Math.Min(S(900), screen.Width), Math.Min(S(600), screen.Height));

        _sidebar.Width = S(270);
        _sidebar.Padding = P(14, 18, 14, 14);
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11 || (e.KeyCode == Keys.Escape && _fullScreen))
            {
                e.Handled = true;
                ToggleFullScreen();
            }
        };
        Controls.Add(_content);
        Controls.Add(_sidebar);
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());

        AddPage("Trang chính", BuildHomePage());
        AddPage("Lịch sử reup", BuildHistoryPage());
        AddPage("Cài đặt", BuildSettingsPage());
        ShowPage(PageHome);

        _log.Line += line => OnUi(() => ShowActivity(line));
        _library.Changed += () => OnUi(RefreshHome);
        _history.Changed += () => OnUi(RefreshHistory);

        SetupTray();
        _scheduler.Tick += async (_, _) => await SchedulerTickAsync();
        _scheduler.Start();

        RefreshAll();
        _log.Report($"todoX ReupVideo {Version} sẵn sàng.");
    }

    protected override void SetVisibleCore(bool value)
    {
        // Started with Windows: stay in the tray until the user opens the window.
        if (_startMinimized && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }

        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(5000, "todoX ReupVideo vẫn đang chạy",
                    "Ứng dụng đã ẩn xuống khay hệ thống và vẫn reup theo lịch. Bấm vào biểu tượng todoX để mở lại; muốn tắt hẳn: chuột phải biểu tượng → Thoát.", ToolTipIcon.Info);
            }

            return;
        }

        _runCts?.Cancel();
        _scheduler.Stop();
        _tray.Visible = false;
        SaveSettings();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ shell

    public const string Hotline = "0366 699 961";
    public const string Website = "https://todox.vn";
    public const string Email = "hello@todox.vn";

    /// <summary>Bottom bar across the window: TodoX contact (hotline, website, email — clickable) and the version.</summary>
    private Control BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = S(36), BackColor = Surface, Padding = P(16, 0, 16, 0) };
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Surface };

        LinkLabel Link(string text, string target)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkColor = Yellow, ActiveLinkColor = Ink, VisitedLinkColor = Yellow, LinkBehavior = LinkBehavior.HoverUnderline, Font = F(9.5f), Margin = P(0, 9, 18, 0) };
            link.LinkClicked += (_, _) => OpenUrl(target);
            return link;
        }

        Label Plain(string text) => new() { Text = text, AutoSize = true, ForeColor = Muted, Font = F(9.5f), Margin = P(0, 9, 4, 0) };

        left.Controls.AddRange([
            new Label { Text = "TodoX", AutoSize = true, ForeColor = Ink, Font = F(9.5f, FontStyle.Bold), Margin = P(0, 9, 18, 0) },
            Plain("Hotline:"), Link(Hotline, "tel:" + Hotline.Replace(" ", "")),
            Plain("Website:"), Link("todox.vn", Website),
            Plain("Email:"), Link(Email, "mailto:" + Email)
        ]);
        var right = new Label { Text = $"todoX ReupVideo {Version} · chạy offline trên máy", AutoSize = true, Dock = DockStyle.Right, ForeColor = Muted, Font = F(9), Padding = P(0, 10, 0, 0) };
        bar.Controls.Add(left);
        bar.Controls.Add(right);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        return bar;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = S(78), BackColor = Ground, Padding = P(20, 12, 20, 8) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        if (Logo() is { } logo)
        {
            flow.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Width = S(140), Height = S(52), Margin = P(0, 0, 16, 0) });
        }

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        titles.Controls.Add(new Label { Text = "ReupVideo", Font = new Font("Segoe UI Semibold", 18), ForeColor = Ink, AutoSize = true, Margin = Padding.Empty });
        titles.Controls.Add(new Label { Text = $"TODO EXCELLENCE · TikTok → Facebook · phiên bản {Version}", ForeColor = Yellow, AutoSize = true, Font = F(9, FontStyle.Bold), Margin = Padding.Empty });
        flow.Controls.Add(titles);
        _fullScreenButton = Theme.Button("⛶  Toàn màn hình (F11)");
        _fullScreenButton.Click += (_, _) => ToggleFullScreen();
        var hide = Theme.Button("Ẩn xuống khay");
        hide.Click += (_, _) => Close();
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = P(0, 8, 0, 0) };
        right.Controls.AddRange([hide, _fullScreenButton]);
        header.Controls.Add(flow);
        header.Controls.Add(right);
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        return header;
    }

    private void AddPage(string title, Control page)
    {
        var index = _nav.Count;
        var button = new Button
        {
            Text = title,
            Width = S(242),
            Height = S(52),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = P(16, 0, 0, 0),
            ForeColor = Ink,
            BackColor = Surface,
            Cursor = Cursors.Hand,
            Margin = P(0, 0, 0, 6),
            Font = F(12),
            UseMnemonic = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(index);
        _sidebar.Controls.Add(button);
        page.Visible = false;
        _content.Controls.Add(page);
        _nav.Add((button, page));
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < _nav.Count; i++)
        {
            var active = i == index;
            _nav[i].Page.Visible = active;
            _nav[i].Button.BackColor = active ? Yellow : Surface;
            _nav[i].Button.ForeColor = active ? Ground : Ink;
            _nav[i].Button.Font = F(12, active ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void SetupTray()
    {
        _tray.Icon = AppIcon();
        _tray.Text = "todoX ReupVideo";
        _tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mở todoX ReupVideo", null, (_, _) => ShowWindow());
        menu.Items.Add("Reup ngay", null, async (_, _) => await RunAsync(new RunOptions("Bấm nút")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát todoX ReupVideo", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Maximized;
        }

        Activate();
    }

    private bool _fullScreen;
    private Button _fullScreenButton = null!;

    /// <summary>Full screen: no title bar, covers the taskbar. F11 / Esc / the header button switch back.</summary>
    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        SuspendLayout();
        if (_fullScreen)
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
        }

        _fullScreenButton.Text = _fullScreen ? "⛶  Thoát toàn màn hình (Esc)" : "⛶  Toàn màn hình (F11)";
        ResumeLayout();
    }

    private void ExitApp()
    {
        if (_runner.IsRunning && MessageBox.Show("Đang có lượt reup chạy dở. Thoát sẽ dừng lượt này. Thoát?", "todoX ReupVideo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _exiting = true;
        Close();
    }

    // ------------------------------------------------------------------ running

    private async Task SchedulerTickAsync()
    {
        UpdateScheduleLabel();
        if (_runner.IsRunning || !SchedulePlanner.IsDue(_settings.Schedule, DateTime.Now))
        {
            return;
        }

        _log.Report("Đến giờ chạy tự động.");
        await RunAsync(new RunOptions("Theo lịch"));
    }

    private async Task RunAsync(RunOptions options)
    {
        if (_runner.IsRunning)
        {
            _log.Report("Đang có lượt reup chạy rồi.");
            return;
        }

        _runCts = new CancellationTokenSource();
        SetRunning(true);
        try
        {
            var summary = await Task.Run(() => _runner.RunAsync(_settings, _log, _runCts.Token, options));
            if (summary is not null)
            {
                _log.Report("Kết quả: " + summary);
                if (!Visible && (summary.Posted > 0 || summary.Failed > 0))
                {
                    _tray.ShowBalloonTip(5000, "todoX ReupVideo", summary.ToString(), summary.Failed > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Report("Lỗi: " + ex.Message);
        }
        finally
        {
            SaveSettings();
            SetRunning(false);
            RefreshAll();
        }
    }

    private async Task UpdateLinksOnlyAsync()
    {
        if (_runner.IsRunning)
        {
            return;
        }

        _runCts = new CancellationTokenSource();
        SetRunning(true);
        try
        {
            var added = await Task.Run(() => _runner.UpdateLinksAsync(_settings, _log, _runCts.Token));
            _log.Report($"Cập nhật xong: {added} link mới.");
        }
        catch (OperationCanceledException)
        {
            _log.Report("Đã dừng.");
        }
        catch (Exception ex)
        {
            _log.Report("Lỗi: " + ex.Message);
        }
        finally
        {
            SaveSettings();
            SetRunning(false);
            RefreshAll();
        }
    }

    private void StopRun()
    {
        if (_runCts is { IsCancellationRequested: false } cts)
        {
            _log.Report("Đang dừng sau bước hiện tại...");
            cts.Cancel();
        }
    }

    // ------------------------------------------------------------------ helpers

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_paths);
        }
        catch (Exception ex)
        {
            _log.Report("Không lưu được cài đặt: " + ex.Message);
        }
    }

    private void OnUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // window closing
            }
        }
        else
        {
            action();
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser / blocked
        }
    }

    private static string FacebookUrl(string id) => "https://www.facebook.com/" + id;

    private List<FacebookPage> EnabledPages() => _settings.FacebookPages.Where(p => p.Enabled).ToList();

    private string PageName(string? pageId) =>
        pageId is null ? "" : _settings.FacebookPages.FirstOrDefault(p => p.PageId == pageId)?.Name ?? pageId;

    private void RefreshAll()
    {
        RefreshHome();
        RefreshHistory();
        RefreshChannels();
        RefreshPages();
        UpdateScheduleLabel();
    }
}
