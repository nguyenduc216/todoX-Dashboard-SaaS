using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// The setup customers see when they open the downloaded TodoX EditVideo: 1 welcome, 2 folder and options,
/// 3 install (each step and its log), 4 license (or skip), 5 finish. An existing install is updated in its folder.
/// </summary>
public sealed class SetupWizard : Form
{
    private const int ContentWidth = 728;
    private static readonly Color Ground = MainWindow.Ground;
    private static readonly Color PanelColor = MainWindow.Panel;
    private static readonly Color Yellow = MainWindow.Yellow;
    private static readonly Color Ink = MainWindow.Ink;
    private static readonly Color Muted = MainWindow.Muted;
    private static readonly Color Green = ColorTranslator.FromHtml("#4ADE80");
    private static readonly Color Red = ColorTranslator.FromHtml("#F87171");
    private static readonly string[] StepNames = ["Giới thiệu", "Tùy chọn", "Cài đặt", "License", "Hoàn tất"];
    private static readonly string[] Spinner = ["◐", "◓", "◑", "◒"];

    // Preview (for screenshots and checks): TODOX_SETUP_PREVIEW=1 (or "new" to act as a first install) walks the
    // wizard with a simulated install that touches nothing; TODOX_SETUP_PREVIEW_PAGE jumps to a page on open.
    private static readonly string? PreviewMode = Environment.GetEnvironmentVariable("TODOX_SETUP_PREVIEW") is { Length: > 0 } p ? p : null;
    private readonly ExistingInstall? _existing = PreviewMode == "new" ? null : Installer.FindExisting();
    private readonly IReadOnlyList<ClientStatus> _assistants = new ClientRegistrar().Detect(string.Empty).Where(c => c.Detected).ToList();
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<Label> _stepLabels = [];
    private readonly List<Control> _pages = [];
    private int _page;

    // footer
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _close;

    // page 2
    private readonly TextBox _folder = new() { Width = 560, Font = new Font("Segoe UI", 10.5f), BackColor = PanelColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _folderNote = Caption("", 9.5f, Muted);
    private readonly CheckBox _claude = Check("", true);
    private readonly CheckBox _codex = Check("Kết nối vào Codex", true);
    private readonly CheckBox _startup = Check("Tự khởi động cùng Windows — tự kiểm tra bản mới và skill mỗi khi mở máy", true);
    private readonly CheckBox _desktop = Check("Tạo biểu tượng TodoX EditVideo trên Desktop", true);

    // page 3
    private readonly Label _installTitle = Caption("Đang cài đặt...", 15, Ink, bold: true);
    // Two columns of steps; the height follows the number of rows (no scroll bar of its own).
    private readonly FlowLayoutPanel _stepList = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Width = ContentWidth, Height = 26, Margin = new Padding(0, 6, 0, 8) };
    private readonly Dictionary<string, (Label Icon, Label Text)> _stepRows = [];
    private readonly RichTextBox _logBox = new() { ReadOnly = true, ScrollBars = RichTextBoxScrollBars.None, Width = ContentWidth, Height = 150, BackColor = PanelColor, ForeColor = Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9), DetectUrls = false };
    private readonly LinkLabel _logLink = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), UseMnemonic = false, LinkColor = Yellow, ActiveLinkColor = Yellow, ForeColor = Muted, Visible = false, Margin = new Padding(0, 6, 0, 0) };
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _spin = new() { Interval = 130 };
    private int _spinFrame;

    // page 4
    private readonly Label _licenseState = Caption("", 10.5f, Ink);
    private readonly TextBox _key = new() { Width = 560, Font = new Font("Segoe UI", 10.5f), BackColor = PanelColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Dán license key TodoX (tdxv_...)" };
    private readonly FlowLayoutPanel _keyRow = Row();
    private readonly CheckBox _skipLicense = Check("Bỏ qua, tôi sẽ nhập license sau", false);
    private readonly Label _licenseActivity = Caption("", 9.5f, Muted);
    private Button? _activate;
    private bool _licensed;

    // page 5
    private readonly Label _doneTitle = Caption("", 17, Ink, bold: true);
    private readonly Label _doneText = Caption("", 10.5f, Ink);
    private readonly LinkLabel _doneLog = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), LinkColor = Yellow, ActiveLinkColor = Yellow, ForeColor = Muted, Margin = new Padding(0, 4, 0, 14) };
    private readonly CheckBox _openSite = Check("Mở trang todox.vn", true);
    private readonly CheckBox _launch = Check("Mở TodoX EditVideo ngay", true);

    private ServiceProvider? _services;
    private InstallLog? _log;
    private string? _logPath;
    private bool _installing;
    private bool _installed;

    private SetupWizard()
    {
        Text = "Cài đặt TodoX EditVideo";
        Icon = WindowsNotifier.AppIcon();
        BackColor = Ground;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, Math.Min(760, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 40));

        _back = FooterButton("Quay lại", primary: false);
        _next = FooterButton("Tiếp tục", primary: true);
        _close = FooterButton("Huỷ", primary: false);
        _back.Click += (_, _) => Show(_page - 1);
        _next.Click += async (_, _) => await NextAsync();
        _close.Click += (_, _) => Close();

        // Each page fits the window; on a short screen it scrolls with TodoX's thin bar instead of being cut off.
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(36, 20, 26, 8), BackColor = Ground };
        foreach (var page in new[] { WelcomePage(), OptionsPage(), InstallPage(), LicensePage(), FinishPage() })
        {
            var host = new ScrollHost(page, Ground) { Dock = DockStyle.Fill, Visible = false };
            _pages.Add(host);
            body.Controls.Add(host);
        }

        ((ScrollHost)_pages[2]).WheelOwners.Add(_logBox);

        Controls.Add(body);
        Controls.Add(Footer());
        Controls.Add(Stepper());
        Controls.Add(Header());

        _spin.Tick += (_, _) => Spin();
        Shown += async (_, _) => await PreviewJumpAsync();
        FormClosing += OnClosing;
        FormClosed += (_, _) =>
        {
            _cancel.Cancel();
            _services?.Dispose();
        };
        Show(0);
    }

    public static int Run()
    {
        MainWindow.HideConsole();
        var ui = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new SetupWizard());
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        return 0;
    }

    private bool IsUpdate => _existing is not null;

    private string Root => _folder.Text.Trim();

    // ------------------------------------------------------------------ pages

    private Control WelcomePage()
    {
        var page = Stack();
        page.Controls.Add(Caption("Chào mừng bạn đến với TodoX EditVideo", 16, Ink, bold: true));
        page.Controls.Add(Caption(
            "TodoX EditVideo là trợ lý dựng video chạy ngay trên máy tính của bạn, do TodoX — Todo Excellence phát triển. " +
            "Bạn chỉ cần nói với Claude hoặc Codex điều mình muốn, ví dụ “Dựng video TikTok 30 giây từ các clip trong dự án " +
            "Nhà phố Quận 7”, TodoX sẽ tự cắt ghép, thêm phụ đề, nhạc nền, giọng đọc và xuất ra video hoàn chỉnh.", 10, Ink, top: 8));

        foreach (var line in new[]
                 {
                     "Cắt, ghép, chuyển cảnh, phụ đề động, tiêu đề mở đầu và kết thúc theo màu thương hiệu của bạn.",
                     "Giọng đọc tiếng Việt, tạo ảnh và video AI ngay trong cuộc trò chuyện.",
                     "Bộ skill dựng video theo từng lĩnh vực, TodoX cập nhật liên tục.",
                     "Video và hình ảnh của bạn nằm trên máy, không phải tải lên mạng để dựng."
                 })
        {
            var row = Row();
            row.Controls.Add(new Label { Text = "✦", ForeColor = Yellow, AutoSize = true, Font = new Font("Segoe UI", 10), Margin = new Padding(0, 4, 6, 0) });
            row.Controls.Add(Caption(line, 10, Ink, top: 4, width: ContentWidth - 30));
            page.Controls.Add(row);
        }

        var version = StatusService.Version;
        var detect = _existing switch
        {
            null => $"Máy này chưa có TodoX EditVideo. Bộ cài sẽ cài mới bản {version}.",
            { Version: var v } when HealthCheck.IsNewer(v, version) =>
                $"Máy đang có TodoX EditVideo {v} (mới hơn bộ cài này) tại {_existing.Root}. Bộ cài giữ bản trên máy và chỉ kiểm tra, cài thêm những phần còn thiếu.",
            { Version: var v } when v == version =>
                $"Máy đã có TodoX EditVideo {v} tại {_existing.Root}. Bộ cài sẽ kiểm tra và cài lại những phần còn thiếu — license, skill và dự án được giữ nguyên.",
            { Version: var v } =>
                $"Máy đã có TodoX EditVideo {v ?? "bản cũ"} tại {_existing.Root}. Bộ cài sẽ cập nhật lên bản {version} trong đúng thư mục đó — license, skill và dự án được giữ nguyên."
        };
        page.Controls.Add(Box(detect, IsUpdate ? "#3A2E0A" : "#14261A", IsUpdate ? "#F5DFA6" : "#BBF7D0", top: 14));
        return page;
    }

    private Control OptionsPage()
    {
        var page = Stack();
        page.Controls.Add(Section("Thư mục cài đặt", top: 0));
        var folderRow = Row();
        folderRow.Controls.Add(_folder);
        var browse = FooterButton("Chọn thư mục...", primary: false);
        browse.Margin = new Padding(8, 0, 0, 0);
        browse.Height = 30;
        browse.Click += (_, _) => BrowseFolder();
        folderRow.Controls.Add(browse);
        page.Controls.Add(folderRow);
        page.Controls.Add(_folderNote);
        _folder.Text = _existing?.Root ?? AgentPaths.DefaultInstallRoot;
        _folder.TextChanged += (_, _) => RefreshFolderNote();
        if (IsUpdate)
        {
            _folder.ReadOnly = true;
            browse.Visible = false;
        }

        RefreshFolderNote();

        page.Controls.Add(Section("Kết nối trợ lý AI"));
        var claude = _assistants.Where(c => ClientRegistrar.IsClaude(c.Id)).Select(c => c.Name).ToList();
        if (claude.Count > 0)
        {
            _claude.Text = $"Kết nối vào Claude ({string.Join(", ", claude)})";
            page.Controls.Add(_claude);
        }

        if (_assistants.Any(c => c.Id == "codex"))
        {
            page.Controls.Add(_codex);
        }

        page.Controls.Add(Caption(_assistants.Count == 0
                ? "Chưa thấy Claude hay Codex trên máy. Bạn vẫn cài được; sau khi cài Claude hoặc Codex, mở TodoX EditVideo và bấm \"Kết nối Claude / Codex\"."
                : "TodoX được thêm vào làm công cụ của trợ lý; các cài đặt khác của Claude / Codex được giữ nguyên.",
            9.5f, Muted, top: 2));

        page.Controls.Add(Section("Tùy chọn khác"));
        page.Controls.Add(_startup);
        page.Controls.Add(_desktop);
        return page;
    }

    private Control InstallPage()
    {
        var page = Stack();
        page.Controls.Add(_installTitle);
        page.Controls.Add(_stepList);
        page.Controls.Add(Section("Nhật ký cài đặt", top: 4));
        page.Controls.Add(_logBox);
        page.Controls.Add(_logLink);
        _logLink.LinkClicked += (_, _) => OpenLog();
        return page;
    }

    private Control LicensePage()
    {
        var page = Stack();
        page.Controls.Add(Caption("Nhập license key", 17, Ink, bold: true));
        page.Controls.Add(Caption("License key do TodoX cấp sau khi bạn đăng ký (bắt đầu bằng tdxv_). " + LicenseMessages.OneMachineNote, 10, Muted, top: 8));
        page.Controls.Add(_licenseState);
        _licenseState.Margin = new Padding(0, 14, 0, 8);
        _keyRow.Controls.Add(_key);
        _activate = FooterButton("Kích hoạt", primary: true);
        _activate.Margin = new Padding(8, 0, 0, 0);
        _activate.Height = 30;
        _activate.Click += async (_, _) => await ActivateAsync();
        _keyRow.Controls.Add(_activate);
        page.Controls.Add(_keyRow);
        page.Controls.Add(_skipLicense);
        _skipLicense.Margin = new Padding(0, 14, 0, 2);
        _skipLicense.CheckedChanged += (_, _) => RefreshButtons();
        page.Controls.Add(Caption("Chưa có license bạn vẫn mở được TodoX EditVideo, chỉ chưa dựng video được. Bạn nhập license bất cứ lúc nào " +
                               "trong cửa sổ TodoX EditVideo.", 9.5f, Muted, top: 2));
        page.Controls.Add(_licenseActivity);
        _licenseActivity.Margin = new Padding(0, 14, 0, 0);
        return page;
    }

    private Control FinishPage()
    {
        var page = Stack();
        var title = Row();
        title.Controls.Add(new Label { Text = "✔", ForeColor = Green, AutoSize = true, Font = new Font("Segoe UI", 26, FontStyle.Bold), Margin = new Padding(0, 0, 12, 0) });
        title.Controls.Add(_doneTitle);
        _doneTitle.Margin = new Padding(0, 12, 0, 0);
        page.Controls.Add(title);
        page.Controls.Add(_doneText);
        _doneText.Margin = new Padding(0, 10, 0, 6);
        page.Controls.Add(_doneLog);
        _doneLog.LinkClicked += (_, _) => OpenLog();
        page.Controls.Add(_openSite);
        page.Controls.Add(_launch);
        return page;
    }

    // ------------------------------------------------------------------ navigation

    private void Show(int index)
    {
        _page = Math.Clamp(index, 0, _pages.Count - 1);
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == _page;
        }

        for (var i = 0; i < _stepLabels.Count; i++)
        {
            _stepLabels[i].Text = i < _page ? $"✔ {StepNames[i]}" : $"{i + 1}  {StepNames[i]}";
            _stepLabels[i].ForeColor = i == _page ? Yellow : i < _page ? Ink : Muted;
            _stepLabels[i].Font = new Font("Segoe UI", 9.5f, i == _page ? FontStyle.Bold : FontStyle.Regular);
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        _back.Visible = _page == 1;
        _close.Visible = _page < 4;
        _close.Text = _page == 3 ? "Đóng" : "Huỷ";
        _next.Enabled = true;
        _next.Text = _page switch
        {
            0 => "Tiếp tục",
            1 => IsUpdate ? "Cập nhật" : "Cài đặt",
            2 => _installing ? "Đang cài..." : _installed ? "Tiếp tục" : "Thử lại",
            3 => "Tiếp tục",
            _ => "Hoàn tất"
        };
        if (_page == 2)
        {
            _next.Enabled = !_installing;
        }
        else if (_page == 3)
        {
            _next.Enabled = _licensed || _skipLicense.Checked;
        }
    }

    private async Task NextAsync()
    {
        switch (_page)
        {
            case 0:
                Show(1);
                break;
            case 1:
                if (PreviewMode is null && Installer.CheckFolder(Root) is { } problem)
                {
                    MessageBox.Show(this, problem, "Thư mục cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Show(2);
                await InstallAsync();
                break;
            case 2:
                if (_installed)
                {
                    await EnterLicensePageAsync();
                }
                else
                {
                    await InstallAsync();
                }

                break;
            case 3:
                _next.Enabled = false;
                await EnterFinishPageAsync();
                break;
            default:
                Finish();
                break;
        }
    }

    // ------------------------------------------------------------------ step 3: install

    private async Task InstallAsync()
    {
        _services ??= AppServices.Build(new AgentPaths(PreviewMode is null ? Root : Path.Combine(Path.GetTempPath(), "todox-setup-preview")));
        var installer = _services.GetRequiredService<Installer>();
        var options = new InstallOptions(_claude.Checked, _codex.Checked, _startup.Checked, _desktop.Checked);
        if (_log is null)
        {
            _log = new InstallLog(IsUpdate ? "update" : "install", _existing?.Version, Root, options);
            _log.StepChanged += step => OnUi(() => ShowStep(step));
            _log.Line += entry => OnUi(() => AppendLog(entry));
        }

        _installing = true;
        _installTitle.Text = IsUpdate ? "Đang cập nhật TodoX EditVideo..." : "Đang cài đặt TodoX EditVideo...";
        _installTitle.ForeColor = Ink;
        _spin.Start();
        RefreshButtons();

        bool ok;
        try
        {
            ok = PreviewMode is null
                ? await Task.Run(() => installer.InstallAsync(options, _log, _cancel.Token))
                : await SimulateInstallAsync(_log, options);
        }
        catch (OperationCanceledException)
        {
            return; // the window is closing
        }
        catch (Exception ex)
        {
            _log.Warn("Lỗi không mong đợi: " + ex.Message);
            ok = false;
        }
        finally
        {
            _installing = false;
            _spin.Stop();
        }

        _log.Finish(ok);
        SaveLog();
        _installed = ok;
        _installTitle.Text = ok
            ? IsUpdate ? "Đã cập nhật xong — bấm Tiếp tục để kiểm tra license" : "Đã cài xong — bấm Tiếp tục để nhập license"
            : "Cài đặt chưa xong. Xem dòng báo lỗi bên dưới rồi bấm \"Thử lại\".";
        _installTitle.ForeColor = ok ? Green : Red;
        RefreshButtons();
    }

    private void ShowStep(InstallStep step)
    {
        if (!_stepRows.TryGetValue(step.Id, out var row))
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Width = ContentWidth / 2, Height = 26, Margin = new Padding(0) };
            var icon = new Label { Width = 24, Height = 24, Font = new Font("Segoe UI Symbol", 10.5f), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            var text = new Label { Width = ContentWidth / 2 - 30, Height = 24, UseMnemonic = false, ForeColor = Ink, BackColor = Ground, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            panel.Controls.Add(icon);
            panel.Controls.Add(text);
            _stepList.Controls.Add(panel);
            _stepList.Height = (_stepList.Controls.Count + 1) / 2 * 26;
            row = (icon, text);
            _stepRows[step.Id] = row;
        }

        row.Icon.Text = step.State switch
        {
            StepState.Running => Spinner[_spinFrame % Spinner.Length],
            StepState.Done => "✔",
            StepState.Failed => "✖",
            StepState.Skipped => "–",
            _ => "○"
        };
        row.Icon.ForeColor = step.State switch
        {
            StepState.Running => Yellow,
            StepState.Done => Green,
            StepState.Failed => Red,
            _ => Muted
        };
        row.Icon.Tag = step.State;
        // The row shows the step; its result is in the tooltip and the log below.
        row.Text.Text = Fit(step.Title, row.Text);
        var full = step.Detail is null ? step.Title : $"{step.Title} · {step.Detail}";
        row.Text.ForeColor = step.State switch
        {
            StepState.Failed => Red,
            StepState.Skipped => Muted,
            _ => Ink
        };
        _tips.SetToolTip(row.Text, full);
    }

    private static string Fit(string text, Label label)
    {
        if (TextRenderer.MeasureText(text, label.Font).Width <= label.Width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && TextRenderer.MeasureText(cut + "…", label.Font).Width > label.Width)
        {
            cut = cut[..^1];
        }

        return cut.TrimEnd() + "…";
    }

    private void Spin()
    {
        _spinFrame++;
        foreach (var (icon, _) in _stepRows.Values)
        {
            if (icon.Tag is StepState.Running)
            {
                icon.Text = Spinner[_spinFrame % Spinner.Length];
            }
        }
    }

    private void AppendLog(InstallLogEntry entry)
    {
        var start = _logBox.TextLength;
        _logBox.AppendText($"{entry.At.ToLocalTime():HH:mm:ss}  {entry.Message}{Environment.NewLine}");
        if (entry.Level is "error" or "warn")
        {
            _logBox.Select(start, _logBox.TextLength - start);
            _logBox.SelectionColor = entry.Level == "error" ? Red : Yellow;
            _logBox.Select(_logBox.TextLength, 0);
        }

        _logBox.ScrollToCaret();
    }

    // ------------------------------------------------------------------ step 4: license

    private async Task EnterLicensePageAsync()
    {
        Show(3);
        var status = await _services!.GetRequiredService<LicenseGuard>().EnsureAsync(_cancel.Token);
        ShowLicense(status);
    }

    private void ShowLicense(LicenseStatus status)
    {
        _licensed = status.IsUsable;
        _licenseState.Text = status.IsUsable
            ? $"✔ Máy này đã kích hoạt license · gói {status.Payload?.Plan} · " +
              (status.Payload?.Exp is { } exp ? "hết hạn " + DateTimeOffset.FromUnixTimeSeconds(exp).ToLocalTime().ToString("dd/MM/yyyy") : "không thời hạn")
            : status.State == LicenseState.NotActivated ? "Máy này chưa có license." : $"✖ {status.Message}";
        _licenseState.ForeColor = status.IsUsable ? Green : status.State == LicenseState.NotActivated ? Ink : Red;
        _keyRow.Visible = !status.IsUsable;
        _skipLicense.Visible = !status.IsUsable;
        RefreshButtons();
    }

    private async Task ActivateAsync()
    {
        var key = _key.Text.Trim();
        if (key.Length == 0)
        {
            _licenseState.Text = "Dán license key TodoX vào ô bên dưới rồi bấm Kích hoạt.";
            _licenseState.ForeColor = Yellow;
            return;
        }

        _activate!.Enabled = false;
        _next.Enabled = false;
        _licenseState.Text = "Đang kích hoạt...";
        _licenseState.ForeColor = Ink;
        try
        {
            var result = await _services!.GetRequiredService<LicenseGuard>().ActivateAsync(key, ct: _cancel.Token);
            _log?.Info(result.IsUsable ? "Đã kích hoạt license." : $"Kích hoạt chưa được: {result.Message}", "license");
            if (!result.IsUsable)
            {
                _licenseState.Text = "✖ " + result.Message;
                _licenseState.ForeColor = Red;
                return;
            }

            _key.Clear();
            ShowLicense(await _services!.GetRequiredService<LicenseGuard>().EnsureAsync(_cancel.Token));
            _licenseActivity.Text = "Đang tải bộ skill TodoX vào Claude / Codex...";
            var installer = _services!.GetRequiredService<Installer>();
            _licenseActivity.Text = await Task.Run(() => installer.InstallSkillsAsync(_log!, _cancel.Token));
            SaveLog();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _activate.Enabled = true;
            RefreshButtons();
        }
    }

    // ------------------------------------------------------------------ step 5: finish

    private async Task EnterFinishPageAsync()
    {
        if (!_licensed)
        {
            _log?.Info("Bỏ qua bước license, khách sẽ nhập sau.", "license");
        }

        SaveLog();
        var sent = 0;
        if (_licensed)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            wait.CancelAfter(TimeSpan.FromSeconds(20));
            sent = await _services!.GetRequiredService<InstallLogUploader>().UploadPendingAsync(wait.Token);
        }

        var connected = _log?.Steps.Where(s => s.Id is "claude" or "codex" && s.State == StepState.Done).Select(s => s.Id == "claude" ? "Claude" : "Codex").ToList() ?? [];
        _doneTitle.Text = IsUpdate ? "Cập nhật hoàn tất!" : "Cài đặt hoàn tất!";
        _doneText.Text = string.Join(Environment.NewLine, new[]
        {
            $"TodoX EditVideo {StatusService.Version} đã sẵn sàng tại {Root}.",
            connected.Count > 0 ? $"Tắt hẳn rồi mở lại {string.Join(" và ", connected)} để bắt đầu dựng video với TodoX." : null,
            _licensed ? null : "Bạn chưa nhập license: mở TodoX EditVideo để nhập khi có key."
        }.OfType<string>());
        _doneLog.Text = $"Nhật ký cài đặt: {_logPath}" + (sent > 0 ? " · đã gửi về TodoX" : _licensed ? " · sẽ gửi về TodoX ở lần mở sau" : " · sẽ gửi về TodoX khi có license");
        _doneLog.LinkArea = new LinkArea("Nhật ký cài đặt: ".Length, _logPath?.Length ?? 0);
        Show(4);
    }

    private void Finish()
    {
        if (PreviewMode is not null)
        {
            Close();
            return;
        }

        var exe = _services?.GetRequiredService<Installer>().InstalledExe;
        if (_openSite.Checked)
        {
            TryStart(new ProcessStartInfo(MainWindow.SupportUrl) { UseShellExecute = true });
        }

        if (_launch.Checked && exe is not null && File.Exists(exe))
        {
            TryStart(new ProcessStartInfo(exe, "app") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }

        Close();
    }

    // ------------------------------------------------------------------ preview

    private async Task PreviewJumpAsync()
    {
        if (PreviewMode is null || !int.TryParse(Environment.GetEnvironmentVariable("TODOX_SETUP_PREVIEW_PAGE"), out var target) || target < 1)
        {
            return;
        }

        Show(1);
        if (target >= 2)
        {
            await NextAsync();
        }

        if (target >= 3)
        {
            await EnterLicensePageAsync();
        }

        if (target >= 4)
        {
            await EnterFinishPageAsync();
        }
    }

    private static async Task<bool> SimulateInstallAsync(InstallLog log, InstallOptions options)
    {
        var delay = int.TryParse(Environment.GetEnvironmentVariable("TODOX_SETUP_PREVIEW_DELAY"), out var ms) ? ms : 0;
        async Task Step(string id, string title, string detail)
        {
            log.Start(id, title);
            await Task.Delay(delay);
            log.Done(id, detail);
        }

        await Step("copy", "Chép chương trình TodoX EditVideo", @"C:\...\TodoX EditVideo\app\todox-editvideo.exe");
        await Step("server", "Kết nối máy chủ TodoX", "bản mới nhất " + StatusService.Version);
        await Step("component-ffmpeg", "Cài Bộ ghép & xử lý video", "đã cài bản 7.1");
        await Step("component-node", "Cài Bộ chạy nền TodoX", "đã cài bản 22.11");
        log.Info("Đang tải 96 MB...", "component-remotion");
        await Step("component-remotion", "Cài Bộ hiệu ứng & dựng hình", "đã cài bản 4.0");
        await Step("component-whisper", "Cài Bộ nghe lại lời thoại", "đã có bản mới nhất");
        await Step("browser", "Tải trình duyệt dựng hình", "đã tải xong");
        await Step("resources", "Tạo thư mục tư liệu dự án", @"C:\Users\...\Documents\TodoX EditVideo");
        await Step("claude", "Kết nối vào Claude", "Claude Desktop, Claude Code — tắt hẳn rồi mở lại để dùng TodoX");
        if (options.ConnectCodex)
        {
            await Step("codex", "Kết nối vào Codex", "Codex — tắt hẳn rồi mở lại để dùng TodoX");
        }

        log.Skip("skills", "Tải bộ skill TodoX", "sẽ tải sau khi nhập license");
        await Step("shortcuts", "Tạo biểu tượng Desktop và Start menu", "Desktop và Start menu");
        await Step("startup", "Tự khởi động cùng Windows", "tự kiểm tra cập nhật mỗi khi mở máy");
        await Step("register", "Đăng ký với Windows", "phiên bản " + StatusService.Version);
        return true;
    }

    // ------------------------------------------------------------------ helpers

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_page == 4 || _page == 0 || (_page == 1 && _log is null))
        {
            return;
        }

        var question = _installing
            ? "Đang cài đặt. Dừng cài đặt và thoát?"
            : _page == 3 ? "Thoát bộ cài? Bạn có thể nhập license sau trong cửa sổ TodoX EditVideo." : "Thoát bộ cài?";
        if (MessageBox.Show(this, question, "TodoX EditVideo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _cancel.Cancel();
        if (_log is not null && !_installing)
        {
            SaveLog();
        }
    }

    private void SaveLog()
    {
        if (_log is null || _services is null)
        {
            return;
        }

        try
        {
            _logPath = _log.Save(_services.GetRequiredService<AgentPaths>());
            _logLink.Text = $"Nhật ký đã lưu: {_logPath}";
            _logLink.LinkArea = new LinkArea("Nhật ký đã lưu: ".Length, _logPath.Length);
            _logLink.Visible = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog(new InstallLogEntry(DateTimeOffset.Now, "warn", null, "Không lưu được nhật ký: " + ex.Message));
        }
    }

    private void OpenLog()
    {
        if (_logPath is not null && File.Exists(_logPath))
        {
            TryStart(new ProcessStartInfo("notepad.exe", $"\"{_logPath}\"") { UseShellExecute = false });
        }
    }

    private void BrowseFolder()
    {
        var current = Root;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục cài TodoX EditVideo",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(current) ? current : Path.GetDirectoryName(current) is { } parent && Directory.Exists(parent) ? parent : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folder.Text = WithProductFolder(dialog.SelectedPath);
        }
    }

    /// <summary>Like other setups: picking "D:\Apps" installs into "D:\Apps\TodoX EditVideo".</summary>
    internal static string WithProductFolder(string selected)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(selected);
        return string.Equals(Path.GetFileName(trimmed), Installer.ProductName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, Installer.ProductName);
    }

    private void RefreshFolderNote()
    {
        var free = "";
        try
        {
            if (Path.IsPathFullyQualified(Root) && Path.GetPathRoot(Root) is { } drive)
            {
                free = $" Ổ {drive.TrimEnd('\\')} còn trống {new DriveInfo(drive).AvailableFreeSpace / 1073741824.0:0.#} GB.";
            }
        }
        catch (Exception)
        {
            // An unknown or disconnected drive is reported when the install starts.
        }

        _folderNote.Text = IsUpdate
            ? "Đang cập nhật bản đã cài nên giữ nguyên thư mục này." + free
            : "Mặc định giống các phần mềm khác: thư mục Programs của tài khoản Windows này, không cần quyền quản trị. " +
              "Chương trình và các thành phần cần khoảng 1 GB." + free;
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static void TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception)
        {
            // Opening the site or the app is a convenience.
        }
    }

    // ------------------------------------------------------------------ layout

    private Control Header()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = ColorTranslator.FromHtml("#101318") };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("todox-logo.png"))
        {
            if (stream is not null)
            {
                header.Controls.Add(new PictureBox { Image = Image.FromStream(stream), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(30, 14, 130, 50) });
            }
        }

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(172, 10) };
        titles.Controls.Add(new Label { Text = "TodoX EditVideo", Font = new Font("Segoe UI Semibold", 16), ForeColor = Ink, AutoSize = true, Margin = new Padding(0) });
        titles.Controls.Add(new Label { Text = $"TODO EXCELLENCE · bộ cài phiên bản {StatusService.Version}", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Yellow, AutoSize = true, Margin = new Padding(2, 0, 0, 0) });
        header.Controls.Add(titles);
        return header;
    }

    private Control Stepper()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(34, 11, 20, 0), BackColor = ColorTranslator.FromHtml("#0E1014"), WrapContents = false };
        for (var i = 0; i < StepNames.Length; i++)
        {
            if (i > 0)
            {
                bar.Controls.Add(new Label { Text = "›", ForeColor = Muted, AutoSize = true, Margin = new Padding(6, 0, 6, 0) });
            }

            var label = new Label { AutoSize = true, Margin = new Padding(0) };
            _stepLabels.Add(label);
            bar.Controls.Add(label);
        }

        return bar;
    }

    private Control Footer()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = ColorTranslator.FromHtml("#101318") };
        footer.Controls.Add(new Label { Text = "todox.vn · " + LicenseMessages.SupportContact, ForeColor = Muted, AutoSize = true, Location = new Point(34, 24) });
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_next);
        buttons.Controls.Add(_back);
        footer.Controls.Add(buttons);
        footer.Layout += (_, _) => buttons.Location = new Point(footer.ClientSize.Width - buttons.Width - 26, 13);
        return footer;
    }

    private static FlowLayoutPanel Stack() => new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, 12), BackColor = Ground };

    private static FlowLayoutPanel Row() => new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };

    private static Label Caption(string text, float size, Color color, bool bold = false, int top = 0, int width = ContentWidth) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        MaximumSize = new Size(width, 0),
        Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size),
        ForeColor = color,
        Margin = new Padding(0, top, 0, 0)
    };

    private static Label Section(string title, int top = 20) => new()
    {
        Text = title.ToUpperInvariant(),
        ForeColor = Yellow,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, top, 0, 8)
    };

    private static Control Box(string text, string background, string foreground, int top)
    {
        var box = new Panel { Width = ContentWidth, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = ColorTranslator.FromHtml(background), Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, top, 0, 0) };
        box.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth - 28, 0), ForeColor = ColorTranslator.FromHtml(foreground), Font = new Font("Segoe UI", 10), Location = new Point(14, 12) });
        return box;
    }

    private static CheckBox Check(string text, bool on) => new()
    {
        Text = text,
        Checked = on,
        AutoSize = true,
        ForeColor = Ink,
        Font = new Font("Segoe UI", 10),
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 2, 0, 6)
    };

    private static Button FooterButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(104, 38),
            Padding = new Padding(10, 2, 10, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Yellow : PanelColor,
            ForeColor = primary ? Ground : Ink,
            Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0)
        };
        button.FlatAppearance.BorderColor = primary ? Yellow : ColorTranslator.FromHtml("#2C2F38");
        // Flat buttons look the same when disabled; dim them so "Tiếp tục" clearly waits for the license choice.
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = !button.Enabled ? ColorTranslator.FromHtml(primary ? "#5C4B1C" : "#1C1E24") : primary ? Yellow : PanelColor;
            button.FlatAppearance.BorderColor = !button.Enabled ? button.BackColor : primary ? Yellow : ColorTranslator.FromHtml("#2C2F38");
        };
        return button;
    }
}
