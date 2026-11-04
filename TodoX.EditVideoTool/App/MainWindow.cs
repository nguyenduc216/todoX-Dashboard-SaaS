using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// The window customers see when they double-click the exe: install, activate, system check, connect assistants.
/// Claude/Codex never see it — they start the exe with "mcp".
/// </summary>
public sealed partial class MainWindow : Form
{
    public static readonly Color Ground = ColorTranslator.FromHtml("#0B0D10");
    public static readonly Color Panel = ColorTranslator.FromHtml("#16181E");
    public static readonly Color Yellow = ColorTranslator.FromHtml("#FFBE2E");
    public static readonly Color Ink = Color.White;
    public static readonly Color Muted = ColorTranslator.FromHtml("#A9AFBD");
    public const string SupportUrl = "https://todox.vn";

    private readonly IServiceProvider _services;
    private readonly LicenseGuard _license;
    private readonly HealthCheck _health;
    private readonly Installer _installer;
    private readonly Updater _updater;
    private readonly CancellationTokenSource _closing = new();

    private readonly Label _licenseState = Label("", 11);
    private readonly TextBox _keyBox = new() { Width = 520, Font = new Font("Segoe UI", 10), BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Dán license key TodoX (tdxv_...)" };
    private readonly ListView _checks = new() { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, Height = 190, Width = 640, BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f) };
    private readonly Label _clientsLabel = Label("", 9.5f);
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 110, Width = 640, BackColor = Panel, ForeColor = Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9) };
    private readonly List<Button> _actions = [];

    private MainWindow(IServiceProvider services)
    {
        _services = services;
        _license = services.GetRequiredService<LicenseGuard>();
        _health = services.GetRequiredService<HealthCheck>();
        _installer = services.GetRequiredService<Installer>();
        _updater = services.GetRequiredService<Updater>();

        Text = "TodoX EditVideo";
        Icon = WindowsNotifier.AppIcon();
        BackColor = Ground;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 760);
        AutoScroll = true;

        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 20, 28, 20), AutoScroll = true };
        Controls.Add(root);

        root.Controls.Add(Header());
        root.Controls.Add(UnsignedNotice());

        root.Controls.Add(Section("License"));
        root.Controls.Add(_licenseState);
        var keyRow = Row();
        keyRow.Controls.Add(_keyBox);
        keyRow.Controls.Add(Action("Kích hoạt", ActivateAsync, primary: true));
        root.Controls.Add(keyRow);
        var licenseRow = Row();
        licenseRow.Controls.Add(Action("Gỡ máy này khỏi license", DeactivateAsync));
        root.Controls.Add(licenseRow);

        root.Controls.Add(Section("Kiểm tra hệ thống"));
        _checks.Columns.Add("", 26);
        _checks.Columns.Add("", 190);
        _checks.Columns.Add("", 410);
        root.Controls.Add(_checks);
        var checkRow = Row();
        checkRow.Controls.Add(Action("Kiểm tra lại", () => RunHealthAsync(untilConnected: true)));
        checkRow.Controls.Add(Action(_installer.IsRunningInstalled ? "Cài lại / sửa lỗi" : "Cài đặt TodoX EditVideo", InstallAsync, primary: !_installer.IsRunningInstalled));
        checkRow.Controls.Add(Action("Kiểm tra cập nhật", UpdateAsync));
        root.Controls.Add(checkRow);

        root.Controls.Add(Section("Kết nối trợ lý AI"));
        root.Controls.Add(_clientsLabel);
        var clientRow = Row();
        clientRow.Controls.Add(Action("Kết nối Claude / Codex", ConnectAsync));
        root.Controls.Add(clientRow);

        root.Controls.Add(Section("Nhật ký"));
        root.Controls.Add(_log);

        Shown += async (_, _) =>
        {
            RefreshClients();
            await RefreshLicenseAsync();
            await RunHealthAsync(untilConnected: true);
        };
        FormClosing += (_, _) => _closing.Cancel();
    }

    public static int Run(IServiceProvider services)
    {
        FreeConsole(); // hide the console window a double-click opened
        var exit = 0;
        var ui = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new MainWindow(services));
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        return exit;
    }

    // ------------------------------------------------------------------ actions

    private async Task ActivateAsync()
    {
        var key = _keyBox.Text.Trim();
        if (key.Length == 0)
        {
            Log("Nhập license key TodoX trước khi kích hoạt.");
            return;
        }

        Log("Đang kích hoạt...");
        var result = await _license.ActivateAsync(key, ct: _closing.Token);
        Log(result.Message);
        if (result.IsUsable)
        {
            _keyBox.Clear();
        }

        await RefreshLicenseAsync();
        await RunHealthAsync(untilConnected: false);
    }

    private async Task DeactivateAsync()
    {
        if (MessageBox.Show(this, "Gỡ máy này khỏi license? Bạn có thể kích hoạt lại trên máy khác.", "TodoX EditVideo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var (_, message) = await _license.DeactivateAsync(_closing.Token);
        Log(message);
        await RefreshLicenseAsync();
    }

    private async Task InstallAsync()
    {
        var ok = await Task.Run(() => _installer.InstallAsync(new Progress<string>(Log), _closing.Token));
        RefreshClients();
        await RunHealthAsync(untilConnected: false);
        if (ok && !_installer.IsRunningInstalled)
        {
            Log("Từ giờ mở TodoX EditVideo bằng biểu tượng trên Desktop.");
        }
    }

    private async Task UpdateAsync() => Log(await Task.Run(() => _updater.CheckAndApplyAsync(new Progress<string>(Log), _closing.Token)));

    private Task ConnectAsync()
    {
        var done = _installer.Reconnect();
        Log(done.Count > 0 ? $"Đã kết nối: {string.Join(", ", done)}. Khởi động lại các ứng dụng này." : "Chưa thấy Claude Desktop / Claude Code / Codex trên máy này.");
        RefreshClients();
        return Task.CompletedTask;
    }

    private async Task RunHealthAsync(bool untilConnected)
    {
        try
        {
            // While the window is open an unreachable TodoX is retried every 15 s (with a Windows notification).
            var report = await _health.RunAsync(untilConnected ? 0 : 1, Log, ct: _closing.Token);
            _checks.Items.Clear();
            foreach (var item in report.Items)
            {
                var row = new ListViewItem(item.Ok ? "✔" : "✖") { ForeColor = item.Ok ? ColorTranslator.FromHtml("#4ADE80") : ColorTranslator.FromHtml("#F87171") };
                row.SubItems.Add(item.Name);
                row.SubItems.Add(item.Ok ? item.Detail : $"{item.Detail} — {item.Fix}");
                _checks.Items.Add(row);
            }

            Log(report.Ok ? "Hệ thống sẵn sàng." : "Có mục cần xử lý (xem danh sách).");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshLicenseAsync()
    {
        var s = await _license.EnsureAsync(_closing.Token);
        _licenseState.Text = s.IsUsable
            ? $"✔ Đã kích hoạt · gói {s.Payload?.Plan} · {(s.Payload?.Exp is { } exp ? "hết hạn " + DateTimeOffset.FromUnixTimeSeconds(exp).ToLocalTime().ToString("dd/MM/yyyy") : "không thời hạn")}"
            : $"✖ {s.Message}";
        _licenseState.ForeColor = s.IsUsable ? ColorTranslator.FromHtml("#4ADE80") : Yellow;
    }

    private void RefreshClients()
    {
        var clients = _installer.Clients;
        _clientsLabel.Text = string.Join(Environment.NewLine, clients.Select(c =>
            $"{(c.Registered ? "✔" : c.Detected ? "•" : "–")} {c.Name}: {(c.Registered ? "đã kết nối" : c.Detected ? "có trên máy, chưa kết nối" : "chưa cài")}"));
    }

    private void Log(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(line));
            return;
        }

        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }

    // ------------------------------------------------------------------ layout helpers

    private Control Header()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("todox-logo.png");
        if (stream is not null)
        {
            var logo = Image.FromStream(stream);
            panel.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Width = 150, Height = 54, Margin = new Padding(0, 0, 16, 0) });
        }

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        titles.Controls.Add(new Label { Text = "EditVideo", Font = new Font("Segoe UI Semibold", 20), ForeColor = Ink, AutoSize = true });
        titles.Controls.Add(new Label { Text = $"TODO EXCELLENCE · phiên bản {StatusService.Version}", ForeColor = Yellow, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        panel.Controls.Add(titles);
        return panel;
    }

    private static Control UnsignedNotice()
    {
        var box = new Panel { Width = 640, Height = 66, BackColor = ColorTranslator.FromHtml("#2A2210"), Margin = new Padding(0, 0, 0, 8) };
        var text = new LinkLabel
        {
            Text = "Ứng dụng chưa đăng ký chữ ký số với Windows nên Windows có thể cảnh báo khi mở. Đây là phần mềm chính hãng TodoX. Hỗ trợ: 0366 699 961 · hello@todox.vn · todox.vn",
            LinkColor = Yellow,
            ActiveLinkColor = Yellow,
            ForeColor = ColorTranslator.FromHtml("#F5DFA6"),
            Location = new Point(12, 10),
            Size = new Size(616, 44)
        };
        text.LinkArea = new LinkArea(text.Text.IndexOf("todox.vn", StringComparison.Ordinal), "todox.vn".Length);
        text.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
        box.Controls.Add(text);
        return box;
    }

    private static Label Section(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        ForeColor = Yellow,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 14, 0, 6)
    };

    private static Label Label(string text, float size) => new() { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), Font = new Font("Segoe UI", size), ForeColor = Ink, Margin = new Padding(0, 0, 0, 6) };

    private static FlowLayoutPanel Row() => new() { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 0, 0, 4) };

    private Button Action(string text, Func<Task> handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(10, 2, 10, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Yellow : Panel,
            ForeColor = primary ? Ground : Ink,
            Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 4)
        };
        button.FlatAppearance.BorderColor = primary ? Yellow : ColorTranslator.FromHtml("#2C2F38");
        button.Click += async (_, _) =>
        {
            foreach (var b in _actions)
            {
                b.Enabled = false;
            }

            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                Log("Lỗi: " + ex.Message);
            }
            finally
            {
                foreach (var b in _actions)
                {
                    b.Enabled = true;
                }
            }
        };
        _actions.Add(button);
        return button;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
