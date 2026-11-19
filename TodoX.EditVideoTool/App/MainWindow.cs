using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// The window customers see when they open TodoX EditVideo: a top bar with the license state and Settings, and two
/// main areas — Edit video and Create AI video — each on its own screen. Everything else (license key, resource
/// folder, assistants, skills, system check, updates) lives in <see cref="SettingsForm"/>. The window is resizable
/// and can be maximised. Claude/Codex never see it — they start the exe with "mcp".
/// </summary>
public sealed partial class MainWindow : Form
{
    public static readonly Color Ground = ColorTranslator.FromHtml("#0B0D10");
    public static readonly Color Panel = ColorTranslator.FromHtml("#16181E");
    public static readonly Color Yellow = ColorTranslator.FromHtml("#FFBE2E");
    public static readonly Color Ink = Color.White;
    public static readonly Color Muted = ColorTranslator.FromHtml("#A9AFBD");
    public const string SupportUrl = "https://todox.vn";
    public const string DashboardCreateUrl = "https://dashboard.todox.vn/create";

    private enum Screen
    {
        Home,
        Edit,
        Ai,
        Library
    }

    private readonly IServiceProvider _services;
    private readonly LicenseGuard _license;
    private readonly HealthCheck _health;
    private readonly Updater _updater;
    private readonly SkillSync _skills;
    private readonly ProjectFolders _projects;
    private readonly AgentPaths _paths;
    private readonly CancellationTokenSource _closing = new();

    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ground, Padding = new Padding(36, 24, 36, 16) };
    private readonly Dictionary<Screen, Control> _screens = [];
    // Account chip: the account name on top, package · days · points smaller below; both stay inside the border.
    private readonly Panel _licenseChip = new() { Height = 48, Width = 420, Cursor = Cursors.Hand, BackColor = Panel };
    private readonly LineIcon _licenseIcon = new(TileArt.Key, 22) { Location = new Point(12, 13), BackColor = Color.Transparent };
    private readonly Label _licenseText = new() { AutoSize = false, Location = new Point(42, 4), Height = 40, Width = 368, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10, FontStyle.Bold), BackColor = Color.Transparent, Cursor = Cursors.Hand, UseMnemonic = false };
    private readonly Label _licensePoints = new() { AutoSize = false, Height = 40, Width = 150, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", 13), ForeColor = Yellow, BackColor = Color.Transparent, Cursor = Cursors.Hand, UseMnemonic = false, Visible = false };
    private readonly Label _licenseSub = new() { AutoSize = false, Location = new Point(42, 25), Height = 19, Width = 368, AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft, Font = new Font("Segoe UI", 8.5f), BackColor = Color.Transparent, Cursor = Cursors.Hand, UseMnemonic = false, Visible = false };

    /// <summary>One line (status) or two lines (account name + package details), always inside the chip's border.</summary>
    private void SetChipLines(string main, string? sub, string? points = null)
    {
        var two = !string.IsNullOrWhiteSpace(sub);
        _licenseText.Text = main;
        _licenseSub.Text = sub ?? "";
        _licenseSub.Visible = two;
        _licensePoints.Text = points ?? "";
        _licensePoints.Visible = !string.IsNullOrWhiteSpace(points);
        // Points sit on the right, large and yellow; the account name and package share the space left of them.
        var pointsWidth = _licensePoints.Visible ? TextRenderer.MeasureText(_licensePoints.Text, _licensePoints.Font).Width + 8 : 0;
        _licensePoints.SetBounds(_licenseChip.Width - 12 - pointsWidth, 4, pointsWidth, _licenseChip.Height - 8);
        var textWidth = _licenseChip.Width - 52 - (pointsWidth > 0 ? pointsWidth + 8 : 0);
        _licenseText.TextAlign = two ? ContentAlignment.BottomLeft : ContentAlignment.MiddleLeft;
        _licenseText.SetBounds(42, 4, textWidth, two ? 21 : _licenseChip.Height - 8);
        _licenseSub.SetBounds(42, 25, textWidth, 19);
    }
    private readonly ToolTip _tips = new();
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Padding = new Padding(36, 0, 0, 0) };
    private readonly Panel _updateBanner = new() { Dock = DockStyle.Top, Height = 0, BackColor = ColorTranslator.FromHtml("#3A2E0A"), Visible = false, Padding = new Padding(36, 8, 36, 8) };
    private readonly Label _updateText = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = ColorTranslator.FromHtml("#F5DFA6"), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    private readonly Button _updateButton = Ui.Button("Cập nhật ngay", primary: true);
    private readonly UpdateOverlay _updateOverlay = new();
    private readonly Dictionary<Screen, ListView> _projectLists = [];
    private readonly Dictionary<Screen, ListView> _videoLists = [];
    private bool _syncing;
    private bool _licensed;
    private bool _updateStaged;

    private MainWindow(IServiceProvider services)
    {
        _services = services;
        _license = services.GetRequiredService<LicenseGuard>();
        _health = services.GetRequiredService<HealthCheck>();
        _updater = services.GetRequiredService<Updater>();
        _skills = services.GetRequiredService<SkillSync>();
        _projects = services.GetRequiredService<ProjectFolders>();
        _paths = services.GetRequiredService<AgentPaths>();

        Text = "TodoX EditVideo";
        Icon = WindowsNotifier.AppIcon();
        BackColor = Ground;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 640);
        var area = System.Windows.Forms.Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(1180, area.Width - 60), Math.Min(760, area.Height - 60));

        _screens[Screen.Home] = HomeScreen();
        _screens[Screen.Edit] = WorkScreen(Screen.Edit);
        _screens[Screen.Ai] = WorkScreen(Screen.Ai);
        _screens[Screen.Library] = LibraryScreen();
        foreach (var screen in _screens.Values)
        {
            screen.Dock = DockStyle.Fill;
            screen.Visible = false;
            _content.Controls.Add(screen);
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Ui.Bar };
        footer.Controls.Add(_status);

        Controls.Add(_content);
        Controls.Add(UpdateBanner());
        Controls.Add(TopBar());
        Controls.Add(footer);
        Controls.Add(_updateOverlay);
        Go(Screen.Home);

        Shown += async (_, _) =>
        {
            if (ShotsDir is { } shots)
            {
                await ShotsAsync(shots);
                return;
            }

            await RefreshLicenseAsync();
            _ = StartupSyncAsync();
            _ = SkillLoopAsync();
            _ = RefreshUpdateBannerAsync();
            _ = _services.GetRequiredService<InstallLogUploader>().UploadPendingAsync(_closing.Token);
            await RunHealthAsync();
        };
        Activated += (_, _) => RefreshProjects();
        FormClosing += (_, _) => _closing.Cancel();
    }

    // Checks only: TODOX_WINDOW_SHOTS=<folder> saves each screen as a PNG drawn by the window itself (off screen),
    // without background work (skills, updates, uploads), then closes.
    private static readonly string? ShotsDir = Environment.GetEnvironmentVariable("TODOX_WINDOW_SHOTS") is { Length: > 0 } d ? d : null;

    private async Task ShotsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        Location = new Point(-6000, 0);
        void Shot(Control window, string name)
        {
            using var bmp = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bmp, new Rectangle(0, 0, window.Width, window.Height));
            bmp.Save(Path.Combine(dir, name + ".png"));
        }

        await RefreshLicenseAsync();
        await Task.Delay(300);
        Shot(this, "1-home");
        Go(Screen.Edit);
        await Task.Delay(300);
        Shot(this, "2-edit");
        Go(Screen.Ai);
        await Task.Delay(300);
        Shot(this, "3-ai");
        Go(Screen.Library);
        await LoadLibraryAsync(refresh: false);
        foreach (var tab in Enum.GetValues<LibraryTab>())
        {
            ShowLibrary(tab);
            await Task.Delay(300);
            Shot(this, $"3-library-{(int)tab}-{tab}");
        }

        Go(Screen.Home);
        WindowState = FormWindowState.Maximized;
        await Task.Delay(500);
        Shot(this, "4-home-maximized");
        WindowState = FormWindowState.Normal;
        await Task.Delay(300);
        _updateOverlay.Show("Đang cập nhật TodoX EditVideo…");
        _updateOverlay.Report("Tải TodoX EditVideo: 42%");
        await Task.Delay(400);
        Shot(_updateOverlay, "4-updating-overlay");
        var sampleDir = Path.Combine(Path.GetTempPath(), "todox-shot-history");
        Directory.CreateDirectory(sampleDir);
        File.Delete(Path.Combine(sampleDir, ProjectHistory.HistoryFile));
        ProjectHistory.Add(sampleDir, new HistoryEntry(DateTimeOffset.Now.AddMinutes(-12), "todox_project_folder", "", null, null, null, "xong", null));
        ProjectHistory.Add(sampleDir, new HistoryEntry(DateTimeOffset.Now.AddMinutes(-9), "video", "Video AI veo_omni · 720p · 8 giây", 4.8m, 4800m, 5871.2m, ProjectHistory.AcceptedForProject, "q1"));
        ProjectHistory.Add(sampleDir, new HistoryEntry(DateTimeOffset.Now.AddMinutes(-8), "todox_video_create", "model: veo_omni · prompt: Nhà phố 4 tầng, buổi sáng · duration: 8", null, null, null, "xong", null,
            [Path.Combine(sampleDir, "ai-video-1.mp4")]));
        ProjectHistory.Add(sampleDir, new HistoryEntry(DateTimeOffset.Now.AddMinutes(-4), "voice", "Giọng đọc Vbee · 412 ký tự", 2m, 2000m, 5869.2m, ProjectHistory.Auto, "q2"));
        ProjectHistory.Add(sampleDir, new HistoryEntry(DateTimeOffset.Now.AddMinutes(-3), "todox_auto_edit", "clips: [\"06-ai\"] · voice: default · targetSeconds: 30", null, null, null, "xong", null,
            [Path.Combine(sampleDir, "video-30s.mp4"), Path.Combine(sampleDir, "voice.mp3")]));
        foreach (var sample in new[] { "ai-video-1.mp4", "video-30s.mp4", "voice.mp3" })
        {
            File.WriteAllText(Path.Combine(sampleDir, sample), "");
        }
        using (var history = HistoryForm("Nhà phố Quận 7", sampleDir, Font))
        {
            history.StartPosition = FormStartPosition.Manual;
            history.Location = new Point(-6000, 0);
            history.Show();
            await Task.Delay(300);
            Shot(history, "4-project-history");
            history.Close();
        }

        using (var consent = DesktopPriceConsent.PreviewForm(new PriceQuote("q", "video", "Video AI veo_omni · 720p · 8 giây · khung 9:16", 4.8m, 4800m, 5876m) { Project = "Nhà phố Quận 7" }))
        {
            consent.StartPosition = FormStartPosition.Manual;
            consent.Location = new Point(-6000, 0);
            consent.Show();
            await Task.Delay(300);
            Shot(consent, "4-price-consent");
            consent.Close();
        }
        var (newProject, _) = PromptForm("Dự án mới", "Tên dự án (ví dụ: Nhà phố Quận 7, Khách A – TVC tháng 12):", Font);
        using (newProject)
        {
            newProject.StartPosition = FormStartPosition.Manual;
            newProject.Location = new Point(-6000, 0);
            newProject.Show();
            await Task.Delay(300);
            Shot(newProject, "4-new-project");
            newProject.Close();
        }
        _updateOverlay.Hide();
        Location = new Point(-6000, 0);

        using var settings = new SettingsForm(_services, SettingsPage.License) { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, 0) };
        settings.Show(this);
        await Task.Delay(2500);
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            settings.Show(page);
            await Task.Delay(page == SettingsPage.System ? 6000 : 400);
            Shot(settings, $"5-settings-{(int)page}-{page}");
        }

        settings.Close();
        Close();
    }

    /// <summary>Hides the console window a double-click (or Windows sign-in) opened.</summary>
    public static void HideConsole() => FreeConsole();

    public static int Run(IServiceProvider services)
    {
        FreeConsole(); // hide the console window a double-click opened
        using var running = Server.AppWindow.Hold(); // lets Claude's TodoX tools see the window is open
        var ui = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new MainWindow(services));
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        return 0;
    }

    // ------------------------------------------------------------------ top bar

    private Control TopBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Ui.Bar };
        if (Ui.Logo() is { } logo)
        {
            bar.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(28, 18, 120, 60) });
        }

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(158, 16), BackColor = Ui.Bar };
        titles.Controls.Add(new Label { Text = "TodoX EditVideo", Font = new Font("Segoe UI Semibold", 17), ForeColor = Ink, AutoSize = true, Margin = new Padding(0) });
        var maker = new LinkLabel
        {
            Text = $"Sản phẩm của TodoX AI Automation · todox.vn · phiên bản {StatusService.Version}",
            AutoSize = true,
            ForeColor = Muted,
            LinkColor = Yellow,
            ActiveLinkColor = Ink,
            Margin = new Padding(2, 0, 0, 0)
        };
        maker.LinkArea = new LinkArea(maker.Text.IndexOf("todox.vn", StringComparison.Ordinal), "todox.vn".Length);
        maker.LinkClicked += (_, _) => Ui.Open(SupportUrl);
        titles.Controls.Add(maker);
        bar.Controls.Add(titles);

        // Right side: license chip (opens Settings › Kích hoạt) and the Settings button.
        var settings = Ui.Button("   Cài đặt");
        settings.Height = 40;
        settings.Margin = new Padding(0);
        settings.Controls.Add(new LineIcon(TileArt.Gear, 20) { Location = new Point(10, 9), BackColor = Color.Transparent, ForeColor = Ink, Enabled = false });
        settings.Click += (_, _) => OpenSettings(SettingsPage.License);

        _licenseChip.Controls.Add(_licenseIcon);
        _licenseChip.Controls.Add(_licenseText);
        _licenseChip.Controls.Add(_licenseSub);
        _licenseChip.Controls.Add(_licensePoints);
        _licenseChip.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(_licenseText.ForeColor, 1.2f);
            using var path = Ui.Rounded(new RectangleF(0.5f, 0.5f, _licenseChip.Width - 1.5f, _licenseChip.Height - 1.5f), 8);
            e.Graphics.DrawPath(pen, path);
        };
        foreach (Control c in new Control[] { _licenseChip, _licenseText, _licenseSub, _licensePoints, _licenseIcon })
        {
            c.Click += (_, _) => OpenSettings(SettingsPage.License);
        }

        SetLicense(null);
        bar.Controls.Add(_licenseChip);
        bar.Controls.Add(settings);
        bar.Layout += (_, _) =>
        {
            settings.Location = new Point(bar.ClientSize.Width - settings.Width - 28, 28);
            _licenseChip.Location = new Point(settings.Left - _licenseChip.Width - 12, settings.Top + settings.Height / 2 - _licenseChip.Height / 2);
        };
        return bar;
    }

    private Control UpdateBanner()
    {
        _updateButton.Dock = DockStyle.Right;
        _updateButton.Margin = new Padding(0);
        _updateButton.Click += async (_, _) => await UpdateOrRestartAsync();
        _updateBanner.Controls.Add(_updateText);
        _updateBanner.Controls.Add(_updateButton);
        return _updateBanner;
    }

    // ------------------------------------------------------------------ screens

    private Control HomeScreen()
    {
        var home = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, BackColor = Ground, Padding = new Padding(0) };
        home.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        home.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        home.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        home.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        home.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hello = Ui.Stack();
        hello.Controls.Add(new Label { Text = "Hôm nay bạn muốn làm gì?", Font = new Font("Segoe UI Semibold", 20), ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 4, 0, 4) });
        hello.Controls.Add(Ui.Text("Chọn một việc bên dưới. TodoX làm việc cùng Claude / Codex ngay trên máy của bạn.", 10.5f, Muted, width: 900));
        hello.Margin = new Padding(0, 0, 0, 16);
        home.Controls.Add(hello, 0, 0);
        home.SetColumnSpan(hello, 3);

        home.Controls.Add(Tile(TileArt.EditVideo, "Edit video",
            "Dựng video từ ảnh và clip của bạn: cắt ghép, chuyển cảnh, phụ đề động, nhạc nền, giọng đọc tiếng Việt, mở đầu và kết thúc theo màu thương hiệu.",
            "Mở Edit video", () => Go(Screen.Edit)), 0, 1);
        home.Controls.Add(Tile(TileArt.AiVideo, "Tạo video AI",
            "Biến một bức ảnh hoặc một ý tưởng thành cảnh quay AI: ảnh sản phẩm, cảnh mở đầu, cảnh còn thiếu, phim ngắn doanh nghiệp. Tính điểm theo từng lần tạo.",
            "Mở Tạo video AI", () => Go(Screen.Ai)), 1, 1);
        home.Controls.Add(Tile(TileArt.Skills, "Thư viện",
            "Nhân vật AI, giọng đọc, nhạc nền, nhạc AI và hiệu ứng VFX của tài khoản TodoX — tải sẵn về máy để dùng khi dựng video.",
            "Mở thư viện", () => Go(Screen.Library)), 2, 1);
        return home;
    }

    private Control Tile(TileArt art, string title, string text, string action, Action open)
    {
        var tile = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Margin = new Padding(0, 0, 16, 0), Padding = new Padding(28), Cursor = Cursors.Hand };
        var icon = new LineIcon(art, 64) { BackColor = Color.Transparent, Location = new Point(28, 28) };
        var name = new Label { Text = title, Font = new Font("Segoe UI Semibold", 20), ForeColor = Ink, AutoSize = true, BackColor = Color.Transparent, Location = new Point(28, 104) };
        var body = new Label { Text = text, Font = new Font("Segoe UI", 10.5f), ForeColor = Muted, AutoSize = true, BackColor = Color.Transparent, Location = new Point(30, 158), UseMnemonic = false };
        var button = Ui.Button(action, primary: true);
        button.Height = 42;
        tile.Controls.AddRange([icon, name, body, button]);
        tile.Layout += (_, _) =>
        {
            body.MaximumSize = new Size(Math.Max(200, tile.ClientSize.Width - 60), 0);
            button.Location = new Point(28, Math.Max(body.Bottom + 16, tile.ClientSize.Height - button.Height - 28));
        };

        void Hover(bool on) => tile.BackColor = on ? Ui.CardHover : Ui.Card;
        foreach (Control c in new Control[] { tile, icon, name, body })
        {
            c.Click += (_, _) => open();
            c.MouseEnter += (_, _) => Hover(true);
            c.MouseLeave += (_, _) => Hover(tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)));
        }

        button.Click += (_, _) => open();
        tile.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Line, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
        };
        return tile;
    }

    /// <summary>Edit video / Tạo video AI: projects on the left, how to start with Claude on the right.</summary>
    private Control WorkScreen(Screen kind)
    {
        var ai = kind == Screen.Ai;
        var screen = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, BackColor = Ground };
        screen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        screen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        screen.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        screen.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Header: back, icon, title.
        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        var back = Ui.Button("   Trang chính");
        back.Controls.Add(new LineIcon(TileArt.Back, 18) { Location = new Point(8, 9), BackColor = Color.Transparent, ForeColor = Ink, Enabled = false });
        back.Click += (_, _) => Go(Screen.Home);
        back.Margin = new Padding(0, 6, 18, 0);
        head.Controls.Add(back);
        head.Controls.Add(new LineIcon(ai ? TileArt.AiVideo : TileArt.EditVideo, 40) { Margin = new Padding(0, 2, 12, 0) });
        head.Controls.Add(new Label { Text = ai ? "Tạo video AI" : "Edit video", Font = new Font("Segoe UI Semibold", 20), ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        screen.Controls.Add(head, 0, 0);
        screen.SetColumnSpan(head, 2);

        // Left: the projects.
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(20), Margin = new Padding(0, 0, 16, 0) };
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Ui.Card,
            ForeColor = Ink,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10.5f),
            Dock = DockStyle.Fill,
            OwnerDraw = true
        };
        // Dark column headers; rows draw as usual.
        list.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Ui.Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, new Font("Segoe UI", 9.5f), Rectangle.Inflate(e.Bounds, -4, 0), Muted,
                TextFormatFlags.VerticalCenter | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) => e.DrawDefault = true;
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
        list.Columns.Add("Dự án", 260);
        list.Columns.Add("Ảnh", 66, HorizontalAlignment.Right);
        list.Columns.Add("Video", 74, HorizontalAlignment.Right);
        list.Columns.Add(ai ? "AI" : "Âm thanh", 100, HorizontalAlignment.Right);
        list.Columns.Add("Cập nhật", 150);
        // The name column takes the rest, so the header has no blank (white) end.
        list.Resize += (_, _) => list.Columns[0].Width = Math.Max(120, list.ClientSize.Width - list.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width));
        list.DoubleClick += (_, _) => OpenSelected(list);
        list.SelectedIndexChanged += (_, _) => RefreshVideos(kind);
        _projectLists[kind] = list;

        // Under the projects: the videos made in the selected one, and whether they are on the dashboard.
        var videos = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Ui.Card,
            ForeColor = Ink,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10f),
            Dock = DockStyle.Bottom,
            Height = 190,
            OwnerDraw = true
        };
        videos.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Ui.Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, new Font("Segoe UI", 9.5f), Rectangle.Inflate(e.Bounds, -4, 0), Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        videos.DrawItem += (_, e) => e.DrawDefault = true;
        videos.DrawSubItem += (_, e) => e.DrawDefault = true;
        videos.Columns.Add("Video đã làm", 260);
        videos.Columns.Add("Ngày", 130);
        videos.Columns.Add("Dashboard", 150);
        videos.Resize += (_, _) => videos.Columns[0].Width = Math.Max(120, videos.ClientSize.Width - videos.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width));
        videos.DoubleClick += (_, _) =>
        {
            if (videos.SelectedItems.Count > 0 && videos.SelectedItems[0].Tag is LocalVideo v)
            {
                Ui.Open(v.Path);
            }
        };
        _videoLists[kind] = videos;
        var videosTitle = new Label { Text = "VIDEO ĐÃ LÀM (bấm đúp để xem)", Dock = DockStyle.Bottom, Height = 40, ForeColor = Yellow, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6) };

        var leftTitle = new Label { Text = "DỰ ÁN CỦA BẠN", Dock = DockStyle.Top, Height = 28, ForeColor = Yellow, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0), BackColor = Ui.Card };
        var create = Ui.Button("Dự án mới", primary: true);
        create.Click += (_, _) => NewProject(list);
        var open = Ui.Button("Mở thư mục");
        open.Click += (_, _) => OpenSelected(list);
        var root = Ui.Button("Thư mục gốc");
        root.Click += (_, _) => OpenSettings(SettingsPage.Folders);
        var sync = Ui.Button("Đồng bộ");
        sync.Click += async (_, _) => await SyncVideosAsync(ask: false);
        _tips.SetToolTip(sync, "Đưa các video chưa đồng bộ lên Jobs của tôi trên Dashboard TodoX.");
        var history = Ui.Button("Lịch sử dự án");
        history.Click += (_, _) => ShowHistory(list);
        _tips.SetToolTip(history, "Mọi lệnh Claude đã làm trong dự án đang chọn: lệnh nào trừ điểm thì có số điểm trừ và số điểm còn; bấm đúp để mở file kết quả.");
        buttons.Controls.AddRange([create, open, sync, history]); // the resource folder is changed in Cài đặt › Thư mục
        _ = root;
        left.Controls.Add(list);
        left.Controls.Add(leftTitle);
        left.Controls.Add(videosTitle);
        left.Controls.Add(videos);
        left.Controls.Add(buttons);
        screen.Controls.Add(left, 0, 1);

        // Right: how it works and requests to copy into Claude.
        var right = Ui.Stack();
        right.Controls.Add(Ui.Section("Cách làm", top: 0));
        var steps = ai
            ? new[]
            {
                "Chọn hoặc tạo dự án, bỏ ảnh gốc vào thư mục 01-anh.",
                "Mở Claude, dán yêu cầu bên dưới (đã chép sẵn khi bấm).",
                "Claude báo giá điểm, bạn đồng ý thì TodoX tạo; kết quả nằm trong 06-ai."
            }
            : new[]
            {
                "Chọn hoặc tạo dự án, bỏ ảnh vào 01-anh, clip vào 02-video, kịch bản vào 04-kich-ban.",
                "Mở Claude, dán yêu cầu bên dưới (đã chép sẵn khi bấm).",
                "TodoX dựng video ngay trên máy; video xong nằm trong output."
            };
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            row.Controls.Add(new StatusBadge(BadgeKind.Number, 24) { Number = i + 1, Filled = true, Accent = Yellow, Margin = new Padding(0, 0, 10, 0) });
            row.Controls.Add(Ui.Text(steps[i], 10.5f, Ink, width: 410));
            right.Controls.Add(row);
        }

        right.Controls.Add(Ui.Section("Yêu cầu mẫu (bấm để chép)"));
        var prompts = ai
            ? new[]
            {
                "Tạo video AI 8 giây từ ảnh sản phẩm trong dự án {0}, khung 9:16, sản phẩm xoay nhẹ, ánh sáng studio.",
                "Biến ảnh mặt tiền trong dự án {0} thành cảnh quay flycam 6 giây cho phim ngắn doanh nghiệp.",
                "Tạo 3 ảnh AI bối cảnh cho sản phẩm trong dự án {0} để làm cảnh mở đầu."
            }
            : new[]
            {
                "Dựng video TikTok 30 giây từ các clip trong dự án {0}, phụ đề động, nhạc nền, giọng đọc nữ miền Nam.",
                "Làm video giới thiệu thương hiệu 60 giây khung 16:9 từ dự án {0}, mở đầu bằng logo trong 05-thuong-hieu.",
                "Cắt video dài nhất trong dự án {0} thành 3 video ngắn 15 giây có phụ đề."
            };
        foreach (var prompt in prompts)
        {
            var copy = new Label
            {
                Text = "“" + string.Format(prompt, "…") + "”",
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                ForeColor = Ink,
                BackColor = Ui.Card,
                Padding = new Padding(12, 9, 12, 9),
                Margin = new Padding(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            copy.MouseEnter += (_, _) => copy.BackColor = Ui.CardHover;
            copy.MouseLeave += (_, _) => copy.BackColor = Ui.Card;
            copy.Click += (_, _) => CopyRequest(prompt, list);
            right.Controls.Add(copy);
        }

        if (ai)
        {
            var web = new LinkLabel { Text = "Hoặc tạo trên Dashboard TodoX", AutoSize = true, LinkColor = Yellow, ActiveLinkColor = Ink, Margin = new Padding(0, 6, 0, 0) };
            web.LinkClicked += (_, _) => Ui.Open(DashboardCreateUrl);
            right.Controls.Add(web);
        }

        var rightHost = new ScrollHost(right, Ground) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        screen.Controls.Add(rightHost, 1, 1);
        return screen;
    }

    private void Go(Screen screen)
    {
        foreach (var (key, control) in _screens)
        {
            control.Visible = key == screen;
        }

        if (screen == Screen.Library)
        {
            _ = LoadLibraryAsync(refresh: false);
        }
        else if (screen != Screen.Home)
        {
            RefreshProjects();
            if (!_licensed)
            {
                Say("Máy chưa kích hoạt license: bạn vẫn xem được dự án, nhưng cần kích hoạt để dựng video (bấm ô Chưa kích hoạt).");
            }
        }
    }

    // ------------------------------------------------------------------ projects

    private void RefreshProjects()
    {
        IReadOnlyList<ProjectFolder> projects;
        try
        {
            projects = _projects.List();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var (kind, list) in _projectLists)
        {
            var selected = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Text : null;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in projects)
            {
                var ai = kind == Screen.Ai ? CountFiles(Path.Combine(p.Path, ProjectFolders.AiDir)) : p.Audio;
                var item = new ListViewItem(p.Name) { Tag = p.Path };
                item.SubItems.Add(p.Images.ToString());
                item.SubItems.Add(p.Videos.ToString());
                item.SubItems.Add(ai.ToString());
                item.SubItems.Add(p.LastChanged?.ToString("dd/MM/yyyy HH:mm") ?? "");
                item.Selected = item.Text == selected;
                list.Items.Add(item);
            }

            if (list.SelectedItems.Count == 0 && list.Items.Count > 0)
            {
                list.Items[0].Selected = true;
            }

            list.EndUpdate();
        }
    }

    private void RefreshVideos(Screen kind)
    {
        if (!_projectLists.TryGetValue(kind, out var projects) || !_videoLists.TryGetValue(kind, out var list))
        {
            return;
        }

        list.BeginUpdate();
        list.Items.Clear();
        if (projects.SelectedItems.Count > 0)
        {
            foreach (var v in _services.GetRequiredService<ProjectSync>().Videos(projects.SelectedItems[0].Text))
            {
                var item = new ListViewItem(v.FileName) { Tag = v };
                item.SubItems.Add(v.Rendered.ToString("dd/MM/yyyy HH:mm"));
                item.SubItems.Add(v.Synced ? "Đã đồng bộ" : "Chưa đồng bộ");
                item.ForeColor = v.Synced ? Ink : Yellow;
                list.Items.Add(item);
            }
        }

        list.EndUpdate();
    }

    /// <summary>
    /// When the window opens: the account's characters, voices and music come down; project/video information goes
    /// up; and if videos are not on the dashboard yet, the person is asked to sync them.
    /// </summary>
    private async Task StartupSyncAsync()
    {
        try
        {
            _ = _services.GetRequiredService<LibrarySync>().RefreshAsync(_closing.Token);
            await SyncVideosAsync(ask: true);
        }
        catch (Exception)
        {
            // Offline: asked again next time.
        }
    }

    /// <summary>The "Đồng bộ" button: uploads every video that is not on the dashboard yet.</summary>
    private async Task SyncVideosAsync(bool ask)
    {
        if (_syncing || !_licensed)
        {
            if (!ask && !_licensed)
            {
                Say("Kích hoạt license trước khi đồng bộ video lên Dashboard.");
            }

            return;
        }

        _syncing = true;
        try
        {
            var sync = _services.GetRequiredService<ProjectSync>();
            var (pending, error) = await sync.SyncInfoAsync(_closing.Token);
            if (error is not null)
            {
                if (!ask)
                {
                    Say("Chưa đồng bộ được: " + error);
                }

                return;
            }

            if (pending.Count == 0)
            {
                if (!ask)
                {
                    Say("Mọi video đã có trên Dashboard (Jobs của tôi).");
                }

                return;
            }

            if (ask && MessageBox.Show(this,
                    $"Có {pending.Count} video chưa đồng bộ lên Dashboard TodoX (Jobs của tôi).\n\nĐồng bộ ngay bây giờ?",
                    "TodoX EditVideo — Đồng bộ video", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                Say($"{pending.Count} video chưa đồng bộ. Bấm \"Đồng bộ lên Dashboard\" khi muốn đưa lên.");
                return;
            }

            var done = 0;
            foreach (var video in pending)
            {
                var index = done + 1;
                var failed = await sync.UploadAsync(video, new Progress<double>(p => Say($"Đang đồng bộ video {index}/{pending.Count}: {video.FileName} — {p:0}%")), _closing.Token);
                if (failed is not null)
                {
                    Say($"Đồng bộ {video.FileName} chưa xong: {failed}");
                    break;
                }

                done++;
            }

            if (done == pending.Count)
            {
                Say($"Đã đồng bộ {done} video lên Dashboard (Jobs của tôi).");
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            Say("Chưa đồng bộ được: " + ex.Message);
        }
        finally
        {
            _syncing = false;
            foreach (var kind in _videoLists.Keys)
            {
                RefreshVideos(kind);
            }
        }
    }

    private static int CountFiles(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;

    private void NewProject(ListView list)
    {
        var name = Prompt("Dự án mới", "Tên dự án (ví dụ: Nhà phố Quận 7, Khách A – TVC tháng 12):");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var (folder, created) = _projects.Find(name, create: true);
        if (folder is null)
        {
            return;
        }

        RefreshProjects();
        foreach (ListViewItem item in list.Items)
        {
            item.Selected = (string)item.Tag! == folder.Path;
        }

        ProjectFolders.Open(folder.Path);
        Say(created
            ? $"Đã tạo dự án \"{folder.Name}\" với các thư mục 01-anh … output. Bỏ tư liệu vào rồi nhờ Claude dựng."
            : $"Dự án \"{folder.Name}\" đã có, mở lại thư mục.");
    }

    private void OpenSelected(ListView list)
    {
        if (list.SelectedItems.Count == 0)
        {
            Say("Chọn một dự án trong danh sách, hoặc bấm Dự án mới.");
            return;
        }

        ProjectFolders.Open((string)list.SelectedItems[0].Tag!);
    }

    /// <summary>The paid commands of the selected project and its "allow all" choice (which can be withdrawn here).</summary>
    private void ShowHistory(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not string dir)
        {
            Say("Chọn một dự án trong danh sách để xem lịch sử.");
            return;
        }

        using var form = HistoryForm(list.SelectedItems[0].Text, dir, Font);
        form.ShowDialog(this);
    }

    internal static Form HistoryForm(string project, string dir, Font font)
    {
        var vi = new System.Globalization.CultureInfo("vi-VN");
        var form = new Form
        {
            Text = "Lịch sử dự án — " + project,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Ground,
            ForeColor = Ink,
            Font = font,
            ClientSize = new Size(1080, 560)
        };
        var entries = ProjectHistory.Read(dir);
        var used = entries.Where(e => e.IsPaid && e.Charged).ToList();
        var head = new Label
        {
            Dock = DockStyle.Top, Height = 84, Padding = new Padding(16, 12, 16, 0), ForeColor = Ink, UseMnemonic = false,
            Font = new Font("Segoe UI Semibold", 11),
            Text = $"{entries.Count(e => !e.IsPaid)} lệnh của Claude · {used.Count} lần trừ điểm, tổng {used.Sum(e => e.Points ?? 0).ToString("0.##", vi)} điểm " +
                   $"(≈ {used.Sum(e => e.Vnd ?? 0).ToString("#,0", vi)} đ) · bấm đúp dòng có kết quả để mở\n" +
                   (ProjectHistory.IsGranted(dir) ? "Dự án này đang cho phép mọi lệnh tốn điểm không cần hỏi (chỉ báo số điểm)." : "Mỗi lệnh tốn điểm đều hỏi xác nhận.")
        };
        var grid = new ListView
        {
            View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, BackColor = Ui.Card, ForeColor = Ink, BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true, OwnerDraw = true
        };
        grid.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Ui.Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, new Font("Segoe UI", 9.5f), Rectangle.Inflate(e.Bounds, -4, 0), Muted,
                TextFormatFlags.VerticalCenter | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.EndEllipsis);
        };
        grid.DrawItem += (_, e) => e.DrawDefault = true;
        grid.DrawSubItem += (_, e) => e.DrawDefault = true;
        // Columns share the window width (fractions), so nothing is cut on high-DPI screens.
        double[] shares = [0.12, 0.13, 0.30, 0.08, 0.09, 0.15, 0.13];
        grid.Resize += (_, _) =>
        {
            var w = grid.ClientSize.Width - 4;
            for (var i = 0; i < grid.Columns.Count && i < shares.Length; i++)
            {
                grid.Columns[i].Width = (int)(w * shares[i]);
            }
        };
        grid.Columns.Add("Lúc", 130);
        grid.Columns.Add("Lệnh", 130);
        grid.Columns.Add("Nội dung", 330);
        grid.Columns.Add("Điểm trừ", 80, HorizontalAlignment.Right);
        grid.Columns.Add("Điểm còn", 90, HorizontalAlignment.Right);
        grid.Columns.Add("Trả lời / kết quả", 170);
        grid.Columns.Add("File kết quả", 130);
        foreach (var e in entries)
        {
            var files = (e.Files ?? []).Where(File.Exists).ToList();
            var item = new ListViewItem([
                e.At.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                e.IsPaid ? KindName(e.Kind) : e.Kind,
                e.Summary,
                e.IsPaid ? (e.Charged ? "-" : "") + (e.Points ?? 0).ToString("0.##", vi) : "",
                e.Balance is { } b ? b.ToString("#,0.##", vi) : "",
                e.Answer,
                files.Count switch { 0 => "", 1 => "▶ " + Path.GetFileName(files[0]), _ => $"▶ {files.Count} file" }
            ])
            {
                Tag = files,
                ToolTipText = files.Count > 0 ? string.Join("\n", files) : e.Summary,
                ForeColor = e.IsPaid ? Yellow : Ink
            };
            grid.Items.Add(item);
        }

        if (entries.Count == 0)
        {
            grid.Items.Add(new ListViewItem(["", "", "Chưa có lệnh nào trong dự án này.", "", "", "", ""]));
        }

        // Double-click a row with a result: one file opens; several show a menu to pick one.
        grid.DoubleClick += (_, _) =>
        {
            if (grid.SelectedItems.Count == 0 || grid.SelectedItems[0].Tag is not List<string> { Count: > 0 } files)
            {
                return;
            }

            if (files.Count == 1)
            {
                Ui.Open(files[0]);
                return;
            }

            var menu = new ContextMenuStrip();
            foreach (var f in files)
            {
                menu.Items.Add(Path.GetFileName(f), null, (_, _) => Ui.Open(f));
            }

            menu.Show(grid, grid.PointToClient(Cursor.Position));
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 10, 12, 0), BackColor = Ground };
        var revoke = Ui.Button("Thu hồi cho phép cả dự án");
        revoke.Enabled = ProjectHistory.IsGranted(dir);
        revoke.Click += (_, _) =>
        {
            ProjectHistory.Revoke(dir);
            revoke.Enabled = false;
            head.Text = head.Text.Split('\n')[0] + "\nĐã thu hồi: từ giờ mỗi lệnh tốn điểm lại hỏi xác nhận.";
        };
        var close = Ui.Button("Đóng", primary: true);
        close.Click += (_, _) => form.Close();
        bottom.Controls.AddRange([close, revoke]);
        form.Controls.Add(grid);
        form.Controls.Add(head);
        form.Controls.Add(bottom);
        return form;
    }

    private static string KindName(string kind) => kind switch
    {
        "video" => "Trừ điểm · video",
        "image" => "Trừ điểm · ảnh",
        "music" => "Trừ điểm · nhạc",
        "voice" => "Trừ điểm · giọng",
        _ => "Trừ điểm · " + kind
    };

    private void CopyRequest(string template, ListView list)
    {
        var project = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Text : "của tôi";
        var request = string.Format(template, $"\"{project}\"");
        try
        {
            Clipboard.SetText(request);
            Say("Đã chép yêu cầu. Mở Claude và dán (Ctrl+V), sửa chi tiết theo ý bạn.");
        }
        catch (Exception)
        {
            Say(request);
        }
    }

    private string? Prompt(string title, string question)
    {
        var (form, box) = PromptForm(title, question, Font);
        using (form)
        {
            return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }
    }

    /// <summary>
    /// Asks for one line of text. Everything is placed from the measured text sizes (DPI and font safe), so the
    /// question, the box and the buttons never overlap. "Câu hỏi (ví dụ: …)" shows the example as a small hint line.
    /// </summary>
    internal static (Form Form, TextBox Box) PromptForm(string title, string question, Font font)
    {
        const int pad = 24, width = 520;
        var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Ground,
            ForeColor = Ink,
            Font = font,
            AutoScaleMode = AutoScaleMode.None,
            ClientSize = new Size(width, 200)
        };

        var text = question.Trim().TrimEnd(':').Trim();
        string? example = null;
        var open = text.IndexOf("(ví dụ", StringComparison.OrdinalIgnoreCase);
        if (open > 0 && text.EndsWith(')'))
        {
            example = "Ví dụ" + text[(open + 6)..^1];
            text = text[..open].Trim();
        }

        var inner = width - 2 * pad;
        var label = new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(inner, 0), Location = new Point(pad, 20),
            Font = new Font("Segoe UI Semibold", 11), ForeColor = Ink, UseMnemonic = false
        };
        var hint = new Label
        {
            Text = example ?? "", AutoSize = true, MaximumSize = new Size(inner, 0), ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5f), UseMnemonic = false, Visible = example is not null
        };
        var box = new TextBox { Width = inner, Font = new Font("Segoe UI", 11.5f), BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        var ok = Ui.Button(title.StartsWith("Dự án", StringComparison.OrdinalIgnoreCase) ? "Tạo dự án" : "Đồng ý", primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Ui.Button("Huỷ");
        cancel.DialogResult = DialogResult.Cancel;
        form.Controls.AddRange([label, hint, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        void Arrange()
        {
            var y = label.Bottom + 6;
            if (hint.Visible)
            {
                hint.Location = new Point(pad, y);
                y = hint.Bottom + 4;
            }

            box.Location = new Point(pad, y + 8);
            ok.Size = new Size(Math.Max(ok.PreferredSize.Width, 130), 38);
            cancel.Size = new Size(Math.Max(cancel.PreferredSize.Width, 90), 38);
            var buttonsTop = box.Bottom + 20;
            cancel.Location = new Point(width - pad - cancel.Width, buttonsTop);
            ok.Location = new Point(cancel.Left - 10 - ok.Width, buttonsTop);
            form.ClientSize = new Size(width, ok.Bottom + pad);
        }

        Arrange();
        form.Load += (_, _) => Arrange();
        form.Shown += (_, _) => box.Focus();
        return (form, box);
    }

    // ------------------------------------------------------------------ license, settings, updates, health

    private void OpenSettings(SettingsPage page)
    {
        using var settings = new SettingsForm(_services, page);
        settings.Changed += () => BeginInvoke(async () =>
        {
            await RefreshLicenseAsync();
            await RefreshUpdateBannerAsync();
            RefreshProjects();
        });
        settings.ShowDialog(this);
        _ = RefreshLicenseAsync();
        RefreshProjects();
    }

    private async Task RefreshLicenseAsync()
    {
        try
        {
            var status = await _license.EnsureAsync(_closing.Token);
            SetLicense(status);
            if (status.IsUsable && await AccountInfo.LoadAsync(_services.GetRequiredService<Server.TodoXServerClient>(), _closing.Token) is { } account)
            {
                // Package, days left and points in the top bar.
                var parts = string.Join(" · ", new[] { account.Package, account.DaysLeftText }.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (account.CustomerName is { } name)
                {
                    SetChipLines(name, parts, account.PointsText);
                }
                else
                {
                    SetChipLines(parts, null, account.PointsText);
                }

                _licenseSub.ForeColor = Muted;
                var tip = (account.CustomerName is { } who ? $"Tài khoản: {who}{Environment.NewLine}" : "") + $"Gói: {account.Package ?? "—"}{(account.Ends is { } end ? $", đến {end.ToLocalTime():dd/MM/yyyy}" : "")}{Environment.NewLine}"
                          + $"Điểm: {account.PointsText}{Environment.NewLine}Lưu trữ: {account.StorageText}{Environment.NewLine}Bấm để mở Cài đặt › Kích hoạt";
                _tips.SetToolTip(_licenseChip, tip);
                _tips.SetToolTip(_licenseText, tip);
                _tips.SetToolTip(_licenseSub, tip);
                _tips.SetToolTip(_licensePoints, tip);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetLicense(LicenseStatus? status)
    {
        _licensed = status?.IsUsable == true;
        var (title, detail) = status is null ? ("Đang kiểm tra license...", "") : SettingsForm.LicenseText(status);
        var color = status is null ? Muted : _licensed ? Ui.Green : Ui.Red;
        SetChipLines(title, null);
        _licenseText.ForeColor = color;
        _licenseIcon.ForeColor = color;
        _licenseChip.BackColor = status is null || _licensed ? Panel : ColorTranslator.FromHtml("#2A1416");
        _licenseText.BackColor = _licenseSub.BackColor = _licensePoints.BackColor = _licenseChip.BackColor;
        _licenseIcon.Invalidate();
        _licenseChip.Invalidate();
        var tip = string.IsNullOrEmpty(detail) ? title : $"{title}{Environment.NewLine}{detail}{Environment.NewLine}Bấm để mở Cài đặt › Kích hoạt";
        _tips.SetToolTip(_licenseChip, tip);
        _tips.SetToolTip(_licenseText, tip);
    }

    private async Task RefreshUpdateBannerAsync()
    {
        UpdateInfo info;
        try
        {
            info = await _updater.CheckAsync(_closing.Token);
        }
        catch (Exception)
        {
            return;
        }

        if (!info.UpdateAvailable)
        {
            _updateBanner.Visible = false;
            return;
        }

        _updateStaged = info.Staged;
        var head = info.BelowMinimum
            ? $"Bản {info.Current} đã cũ — cần cập nhật lên {info.Latest} để tiếp tục dùng."
            : $"Có bản mới TodoX EditVideo {info.Latest}.";
        var notes = Updater.CleanNotes(info.Latest, info.Notes);
        _updateText.Text = head + (notes.Length > 0 ? " " + notes : "") + (info.Staged ? " " + Updater.RestartHint : "");
        _updateButton.Text = info.Staged ? "Mở lại TodoX EditVideo" : "Cập nhật ngay";
        _updateBanner.BackColor = ColorTranslator.FromHtml(info.BelowMinimum ? "#4A1A1A" : "#3A2E0A");
        _updateBanner.Height = 56;
        _updateBanner.Visible = true;
    }

    private async Task UpdateOrRestartAsync()
    {
        if (_updateStaged)
        {
            var exe = _updater.InstalledExe;
            if (File.Exists(exe))
            {
                _updateOverlay.Show("Đang mở lại TodoX EditVideo…");
                Process.Start(new ProcessStartInfo(exe, "app") { UseShellExecute = false });
                Close();
            }

            return;
        }

        _updateButton.Enabled = false;
        _updateOverlay.Show("Đang cập nhật TodoX EditVideo…");
        try
        {
            var progress = new Progress<string>(message =>
            {
                Say(message);
                _updateOverlay.Report(message);
            });
            Say(await Task.Run(() => _updater.CheckAndApplyAsync(progress, _closing.Token)));
            await RefreshUpdateBannerAsync();
        }
        finally
        {
            _updateOverlay.Hide();
            _updateButton.Enabled = true;
        }
    }

    /// <summary>Skills are checked when the window opens and then every N hours while it stays open.</summary>
    private async Task SkillLoopAsync()
    {
        try
        {
            while (!_closing.IsCancellationRequested)
            {
                var result = await Task.Run(() => _skills.SyncAsync(true, _closing.Token));
                await Task.Delay(result.Ok ? UserSettings.Load(_paths).SkillSyncInterval : TimeSpan.FromMinutes(30), _closing.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunHealthAsync()
    {
        try
        {
            // While the window is open an unreachable TodoX is retried every 15 s (with a Windows notification).
            var report = await _health.RunAsync(0, ct: _closing.Token);
            var problems = report.Items.Where(i => !i.Ok && i.Id != "license").ToList();
            Say(problems.Count == 0
                ? "Hệ thống sẵn sàng."
                : $"Cần xử lý: {string.Join(", ", problems.Select(p => p.Name))} — mở Cài đặt › Hệ thống & cập nhật.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Say(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => Say(message));
            return;
        }

        _status.Text = message;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
