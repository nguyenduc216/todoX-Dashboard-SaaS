using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.App;

public enum SettingsPage
{
    License,
    Folders,
    Assistants,
    Skills,
    System
}

/// <summary>
/// Settings, laid out like Windows Settings: a list of pages on the left (license, resource folder, AI assistants,
/// skills, system and updates) and the chosen page on the right. Messages show in one line at the bottom.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly (SettingsPage Page, string Title, TileArt Art)[] Pages =
    [
        (SettingsPage.License, "Kích hoạt", TileArt.Key),
        (SettingsPage.Folders, "Thư mục dự án", TileArt.Folder),
        (SettingsPage.Assistants, "Trợ lý AI", TileArt.AiVideo),
        (SettingsPage.Skills, "Skill TodoX", TileArt.EditVideo),
        (SettingsPage.System, "Hệ thống & cập nhật", TileArt.Gear)
    ];

    private readonly IServiceProvider _services;
    private readonly LicenseGuard _license;
    private readonly HealthCheck _health;
    private readonly Installer _installer;
    private readonly Updater _updater;
    private readonly SkillSync _skills;
    private readonly AgentPaths _paths;
    private readonly CancellationTokenSource _closing = new();

    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Ui.Ground, Padding = new Padding(32, 24, 20, 8) };
    private readonly Dictionary<SettingsPage, Control> _pages = [];
    private readonly Dictionary<SettingsPage, Button> _nav = [];
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Padding = new Padding(28, 0, 0, 0) };

    private readonly Label _licenseState = Ui.Text("", 12, bold: true);
    private readonly Label _licenseDetail = Ui.Text("", 10, Ui.Muted);
    private readonly TextBox _keyBox = new() { Width = 470, Font = new Font("Segoe UI", 10.5f), BackColor = Ui.Panel, ForeColor = Ui.Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Dán license key TodoX (tdxv_...)", Margin = new Padding(0, 4, 8, 0) };
    private readonly FlowLayoutPanel _keyRow = Ui.Row();
    private readonly FlowLayoutPanel _signIn = Ui.Stack();
    private readonly TextBox _user = new() { Width = 330, Font = new Font("Segoe UI", 10.5f), BackColor = Ui.Panel, ForeColor = Ui.Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Tên đăng nhập hoặc email TodoX", Margin = new Padding(0, 4, 8, 6) };
    private readonly TextBox _password = new() { Width = 330, Font = new Font("Segoe UI", 10.5f), BackColor = Ui.Panel, ForeColor = Ui.Ink, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Mật khẩu", UseSystemPasswordChar = true, Margin = new Padding(0, 0, 8, 6) };
    private readonly FlowLayoutPanel _accountCard = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Card, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0, 6, 0, 8), MinimumSize = new Size(640, 0), Visible = false };
    private readonly Label _accountText = Ui.Text("", 11);
    private readonly Label _resourceLabel = Ui.Text("", 10.5f);
    private readonly Label _clientsLabel = Ui.Text("", 10.5f);
    private readonly Label _skillsLabel = Ui.Text("", 10);
    private readonly NumericUpDown _skillHours = new() { Minimum = 1, Maximum = 48, Width = 60, BackColor = Ui.Panel, ForeColor = Ui.Ink, Font = new Font("Segoe UI", 10), Margin = new Padding(0, 6, 8, 0) };
    private readonly ComboBox _skillPick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, BackColor = Ui.Panel, ForeColor = Ui.Ink, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 8, 0) };
    private readonly ComboBox _versionPick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, BackColor = Ui.Panel, ForeColor = Ui.Ink, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 8, 0) };
    private readonly FlowLayoutPanel _checks = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Card, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 8), MinimumSize = new Size(640, 0) };
    private readonly Label _versionLabel = Ui.Text("", 10.5f);
    private readonly ToolTip _tips = new();

    /// <summary>Raised after something the main window shows changed (license, folder, update).</summary>
    public event Action? Changed;

    public SettingsForm(IServiceProvider services, SettingsPage page)
    {
        _services = services;
        _license = services.GetRequiredService<LicenseGuard>();
        _health = services.GetRequiredService<HealthCheck>();
        _installer = services.GetRequiredService<Installer>();
        _updater = services.GetRequiredService<Updater>();
        _skills = services.GetRequiredService<SkillSync>();
        _paths = services.GetRequiredService<AgentPaths>();

        Text = "Cài đặt · TodoX EditVideo";
        Icon = WindowsNotifier.AppIcon();
        BackColor = Ui.Ground;
        ForeColor = Ui.Ink;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 600);
        ClientSize = new Size(980, Math.Min(700, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80));

        _pages[SettingsPage.License] = LicensePage();
        _pages[SettingsPage.Folders] = FoldersPage();
        _pages[SettingsPage.Assistants] = AssistantsPage();
        _pages[SettingsPage.Skills] = SkillsPage();
        _pages[SettingsPage.System] = SystemPage();
        foreach (var content in _pages.Values)
        {
            _body.Controls.Add(new ScrollHost(content, Ui.Ground) { Dock = DockStyle.Fill, Visible = false });
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Ui.Bar };
        footer.Controls.Add(_status);
        Controls.Add(_body);
        Controls.Add(Navigation());
        Controls.Add(footer);

        Shown += async (_, _) =>
        {
            Show(page);
            RefreshResources();
            RefreshClients();
            RefreshSkills();
            await RefreshLicenseAsync();
            await RunHealthAsync();
        };
        FormClosing += (_, _) => _closing.Cancel();
    }

    // ------------------------------------------------------------------ pages

    private Control LicensePage()
    {
        var page = Ui.Stack(660);
        page.Controls.Add(Title("Kích hoạt"));
        page.Controls.Add(_licenseState);
        page.Controls.Add(_licenseDetail);
        _accountCard.Controls.Add(_accountText);
        page.Controls.Add(_accountCard);

        // Two ways to register this app, one shown at a time: the TodoX account (its package gives the license) or the
        // license key that TodoX syncs to the same account.
        _signIn.Controls.Add(Ui.Section("Đăng ký ứng dụng bằng"));
        var byKey = Choice("License key", true);
        var byAccount = Choice("Tài khoản TodoX", false);
        var choices = Ui.Row();
        choices.Controls.AddRange([byKey, byAccount]);
        _signIn.Controls.Add(choices);

        var account = Ui.Stack();
        account.Controls.Add(Ui.Text("Tài khoản (tên đăng nhập hoặc email)", 9.5f, Ui.Muted));
        account.Controls.Add(_user);
        account.Controls.Add(Ui.Text("Mật khẩu", 9.5f, Ui.Muted));
        account.Controls.Add(_password);
        var signIn = Action("Đăng nhập", SignInAsync, primary: true);
        signIn.Margin = new Padding(0, 4, 0, 4);
        account.Controls.Add(signIn);
        account.Visible = false;
        _password.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SignInAsync();
            }
        };

        var key = Ui.Stack();
        key.Controls.Add(Ui.Text("License key TodoX (dạng tdxv_...), lấy trên Dashboard TodoX › EditVideo của tôi.", 9.5f, Ui.Muted));
        _keyRow.Controls.Add(_keyBox);
        _keyRow.Controls.Add(Action("Kích hoạt", ActivateAsync, primary: true));
        key.Controls.Add(_keyRow);

        byAccount.CheckedChanged += (_, _) =>
        {
            account.Visible = byAccount.Checked;
            key.Visible = !byAccount.Checked;
        };
        _signIn.Controls.Add(account);
        _signIn.Controls.Add(key);
        page.Controls.Add(_signIn);
        page.Controls.Add(Ui.Text(LicenseMessages.OneMachineNote, 9.5f, Ui.Muted));
        page.Controls.Add(Ui.Text("Chưa có license? Liên hệ TodoX: " + LicenseMessages.SupportContact, 9.5f, Ui.Muted));
        return page;
    }

    private Control FoldersPage()
    {
        var page = Ui.Stack(660);
        page.Controls.Add(Title("Thư mục dự án"));
        page.Controls.Add(Ui.Section("Thư mục gốc chứa tư liệu", top: 4));
        page.Controls.Add(_resourceLabel);
        var row = Ui.Row();
        row.Controls.Add(Action("Mở thư mục", OpenResourcesAsync));
        row.Controls.Add(Action("Đổi thư mục...", ChangeResourcesAsync));
        page.Controls.Add(row);

        page.Controls.Add(Ui.Section("Mỗi dự án gồm"));
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Card, Padding = new Padding(14, 10, 14, 10), MinimumSize = new Size(640, 0) };
        foreach (var (name, holds) in ProjectFolders.Layout)
        {
            var line = Ui.Row();
            line.Controls.Add(new Label { Text = name, AutoSize = false, Width = 175, Height = 24, ForeColor = Ui.Yellow, Font = new Font("Consolas", 10), TextAlign = ContentAlignment.MiddleLeft });
            line.Controls.Add(new Label { Text = holds, AutoSize = false, Width = 440, Height = 24, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
            layout.Controls.Add(line);
        }

        var claude = Ui.Row();
        claude.Controls.Add(new Label { Text = ProjectFolders.ClaudeFile, AutoSize = false, Width = 175, Height = 24, ForeColor = Ui.Yellow, Font = new Font("Consolas", 10), TextAlign = ContentAlignment.MiddleLeft });
        claude.Controls.Add(new Label { Text = "hồ sơ dự án và quy tắc cho Claude", AutoSize = false, Width = 440, Height = 24, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft });
        layout.Controls.Add(claude);
        page.Controls.Add(layout);
        page.Controls.Add(Ui.Text("Khi bạn nhắc tên dự án, Claude mở đúng thư mục đó, đọc CLAUDE.md và dùng công cụ TodoX để dựng video.", 9.5f, Ui.Muted));
        return page;
    }

    private Control AssistantsPage()
    {
        var page = Ui.Stack(660);
        page.Controls.Add(Title("Trợ lý AI"));
        page.Controls.Add(Ui.Text("TodoX được thêm vào làm công cụ cho Claude / Codex; các cài đặt khác của trợ lý được giữ nguyên.", 10, Ui.Muted));
        page.Controls.Add(Ui.Section("Trạng thái kết nối", top: 8));
        page.Controls.Add(_clientsLabel);
        var row = Ui.Row();
        row.Controls.Add(Action("Kết nối Claude / Codex", ConnectAsync, primary: true));
        page.Controls.Add(row);
        page.Controls.Add(Ui.Text("Sau khi kết nối, tắt hẳn rồi mở lại Claude / Codex để dùng TodoX.", 9.5f, Ui.Muted));
        return page;
    }

    private Control SkillsPage()
    {
        var page = Ui.Stack(660);
        page.Controls.Add(Title("Skill TodoX"));
        page.Controls.Add(_skillsLabel);
        var syncRow = Ui.Row();
        syncRow.Controls.Add(Action("Cập nhật skill", () => SyncSkillsAsync(notify: false), primary: true));
        syncRow.Controls.Add(new Label { Text = "Tự kiểm tra mỗi", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(8, 10, 6, 0) });
        syncRow.Controls.Add(_skillHours);
        syncRow.Controls.Add(new Label { Text = "giờ", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 10, 0, 0) });
        page.Controls.Add(syncRow);

        page.Controls.Add(Ui.Section("Phiên bản mặc định của từng skill"));
        var pinRow = Ui.Row();
        pinRow.Controls.Add(_skillPick);
        pinRow.Controls.Add(_versionPick);
        pinRow.Controls.Add(Action("Đặt làm mặc định", PinSkillAsync));
        page.Controls.Add(pinRow);
        _skillPick.SelectedIndexChanged += (_, _) => FillVersions();

        _skillHours.Value = Math.Clamp(UserSettings.Load(_paths).SkillSyncHours, 1, 48);
        _skillHours.ValueChanged += (_, _) =>
        {
            var current = UserSettings.Load(_paths);
            current.SkillSyncHours = (int)_skillHours.Value;
            current.Save(_paths);
        };
        return page;
    }

    private Control SystemPage()
    {
        var page = Ui.Stack(660);
        page.Controls.Add(Title("Hệ thống & cập nhật"));
        page.Controls.Add(Ui.Section("Phiên bản", top: 4));
        page.Controls.Add(_versionLabel);
        var updateRow = Ui.Row();
        updateRow.Controls.Add(Action("Kiểm tra cập nhật", UpdateAsync, primary: true));
        page.Controls.Add(updateRow);

        page.Controls.Add(Ui.Section("Kiểm tra hệ thống"));
        page.Controls.Add(_checks);
        var row = Ui.Row();
        row.Controls.Add(Action("Kiểm tra lại", RunHealthAsync));
        row.Controls.Add(Action("Cài lại / sửa lỗi", ReinstallAsync));
        page.Controls.Add(row);

        page.Controls.Add(Ui.Section("Hỗ trợ"));
        var support = new LinkLabel
        {
            Text = "todox.vn · " + LicenseMessages.SupportContact,
            LinkArea = new LinkArea(0, "todox.vn".Length),
            AutoSize = true,
            LinkColor = Ui.Yellow,
            ActiveLinkColor = Ui.Ink,
            ForeColor = Ui.Muted,
            Margin = new Padding(0, 0, 0, 6)
        };
        support.LinkClicked += (_, _) => Ui.Open(MainWindow.SupportUrl);
        page.Controls.Add(support);
        page.Controls.Add(Ui.Text("Windows có thể cảnh báo khi mở vì ứng dụng chưa đăng ký chữ ký số. Đây là phần mềm chính hãng TodoX.", 9.5f, Ui.Muted));
        return page;
    }

    // ------------------------------------------------------------------ navigation

    private Control Navigation()
    {
        var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Bar, Padding = new Padding(14, 22, 14, 0) };
        nav.Controls.Add(new Label { Text = "Cài đặt", Font = new Font("Segoe UI Semibold", 16), ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(8, 0, 0, 18) });
        foreach (var (page, title, art) in Pages)
        {
            var button = new Button
            {
                Text = "      " + title,
                Width = 272,
                Height = 42,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Ui.Ink,
                BackColor = Ui.Bar,
                Font = new Font("Segoe UI", 10.5f),
                Cursor = Cursors.Hand,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 4)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Ui.CardHover;
            var icon = new LineIcon(art, 20) { Location = new Point(12, 11), BackColor = Color.Transparent };
            icon.Click += (_, _) => Show(page);
            button.Controls.Add(icon);
            button.Click += (_, _) => Show(page);
            _nav[page] = button;
            nav.Controls.Add(button);
        }

        return nav;
    }

    public void Show(SettingsPage page)
    {
        foreach (ScrollHost host in _body.Controls)
        {
            host.Visible = false;
        }

        _body.Controls.OfType<ScrollHost>().ElementAt((int)page).Visible = true;
        foreach (var (p, button) in _nav)
        {
            var on = p == page;
            button.BackColor = on ? Ui.CardHover : Ui.Bar;
            button.ForeColor = on ? Ui.Yellow : Ui.Ink;
            button.Font = new Font("Segoe UI", 10.5f, on ? FontStyle.Bold : FontStyle.Regular);
            ((LineIcon)button.Controls[0]).ForeColor = on ? Ui.Yellow : Ui.Muted;
            button.Controls[0].Invalidate();
        }
    }

    // ------------------------------------------------------------------ actions

    private async Task ActivateAsync()
    {
        var key = _keyBox.Text.Trim();
        if (key.Length == 0)
        {
            Say("Dán license key TodoX vào ô rồi bấm Kích hoạt.");
            return;
        }

        Say("Đang kích hoạt...");
        var result = await _license.ActivateAsync(key, ct: _closing.Token);
        Say(result.Message);
        if (result.IsUsable)
        {
            _keyBox.Clear();
            await SyncSkillsAsync(notify: false);
            _ = _services.GetRequiredService<InstallLogUploader>().UploadPendingAsync(_closing.Token);
        }

        await RefreshLicenseAsync();
        Changed?.Invoke();
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(_user.Text) || _password.Text.Length == 0)
        {
            Say("Nhập tên đăng nhập (hoặc email) và mật khẩu TodoX.");
            return;
        }

        Say("Đang đăng nhập...");
        var result = await _license.ActivateWithAccountAsync(_user.Text, _password.Text, _closing.Token);
        _password.Clear();
        Say(result.IsUsable ? "Đã kích hoạt máy này bằng tài khoản TodoX." : result.Message);
        if (result.IsUsable)
        {
            await SyncSkillsAsync(notify: false);
            _ = _services.GetRequiredService<InstallLogUploader>().UploadPendingAsync(_closing.Token);
        }

        await RefreshLicenseAsync();
        Changed?.Invoke();
    }

    private async Task RefreshLicenseAsync()
    {
        var s = await _license.EnsureAsync(_closing.Token);
        var (title, detail) = LicenseText(s);
        _licenseState.Text = (s.IsUsable ? "✔  " : "✖  ") + title;
        _licenseState.ForeColor = s.IsUsable ? Ui.Green : Ui.Red;
        _licenseDetail.Text = detail;
        _signIn.Visible = !s.IsUsable;
        _accountCard.Visible = false;
        if (s.IsUsable && await AccountInfo.LoadAsync(_services.GetRequiredService<TodoXServerClient>(), _closing.Token) is { } account)
        {
            _accountText.Text = string.Join(Environment.NewLine, new[]
            {
                $"Gói dịch vụ: {account.Package ?? "—"}{(account.Ends is { } end ? $" · đến {end.ToLocalTime():dd/MM/yyyy} ({account.DaysLeftText})" : "")}",
                $"Điểm: {account.PointsText}",
                $"Lưu trữ trên máy chủ TodoX: {account.StorageText} (file giữ 7 ngày)"
            });
            _accountCard.Visible = true;
        }
    }

    /// <summary>Short status and its detail, shared with the main window's license chip.</summary>
    internal static (string Title, string Detail) LicenseText(LicenseStatus s)
    {
        if (!s.IsUsable)
        {
            return ("Chưa kích hoạt", s.State == LicenseState.NotActivated
                ? "Đăng nhập tài khoản TodoX (hoặc nhập license key) để dựng video và tạo video AI."
                : s.Message);
        }

        var plan = s.Payload?.Plan is { Length: > 0 } p ? $"Gói {p}" : "Đã kích hoạt";
        if (s.Payload?.Exp is not { } exp)
        {
            return ("Đã kích hoạt", $"{plan} · không thời hạn");
        }

        var end = DateTimeOffset.FromUnixTimeSeconds(exp).ToLocalTime();
        var days = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.Now).TotalDays));
        return ($"Đã kích hoạt · còn {days} ngày", $"{plan} · hết hạn {end:dd/MM/yyyy}");
    }

    private Task OpenResourcesAsync()
    {
        var root = UserSettings.Load(_paths).EffectiveResourceRoot;
        if (!ProjectFolders.Open(root))
        {
            Say($"Không mở được thư mục {root}.");
        }

        return Task.CompletedTask;
    }

    private Task ChangeResourcesAsync()
    {
        var settings = UserSettings.Load(_paths);
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục gốc chứa tư liệu các dự án video (mỗi dự án một thư mục con)",
            UseDescriptionForTitle = true,
            InitialDirectory = settings.EffectiveResourceRoot,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            settings.ResourceRoot = dialog.SelectedPath;
            settings.Save(_paths);
            Say($"Thư mục dự án: {dialog.SelectedPath}");
            RefreshResources();
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }

    private void RefreshResources()
    {
        var root = UserSettings.Load(_paths).EffectiveResourceRoot;
        Directory.CreateDirectory(root);
        var projects = Directory.EnumerateDirectories(root).Count(d => !Path.GetFileName(d).StartsWith('.'));
        _resourceLabel.Text = $"{root}{Environment.NewLine}{projects} dự án";
    }

    private Task ConnectAsync()
    {
        var done = _installer.Reconnect();
        Say(done.Count > 0 ? $"Đã kết nối: {string.Join(", ", done)}. Tắt hẳn rồi mở lại các ứng dụng này." : "Chưa thấy Claude Desktop / Claude Code / Codex trên máy này.");
        RefreshClients();
        return Task.CompletedTask;
    }

    private void RefreshClients()
    {
        _clientsLabel.Text = string.Join(Environment.NewLine, _installer.Clients.Select(c =>
            $"{(c.Registered ? "✔" : c.Detected ? "•" : "–")}  {c.Name}: {(c.Registered ? "đã kết nối" : c.Detected ? "có trên máy, chưa kết nối" : "chưa cài")}"));
    }

    private async Task SyncSkillsAsync(bool notify)
    {
        var result = await Task.Run(() => _skills.SyncAsync(notify, _closing.Token));
        Say(result.Describe());
        RefreshSkills();
    }

    private sealed record SkillChoice(string Slug, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record VersionChoice(int? Version, string Text)
    {
        public override string ToString() => Text;
    }

    private void RefreshSkills()
    {
        var state = _skills.LoadState();
        var lines = state.Skills.Values.Select(s =>
            $"•  {s.Title}: dùng v{s.Effective}{(s.Pinned is null ? " (luôn bản mới nhất)" : s.Effective != s.Latest ? $" — đã có v{s.Latest}" : "")}").ToList();
        _skillsLabel.Text = state.LastSync is { } last
            ? $"{state.Skills.Count} skill · kiểm tra lần cuối {last.ToLocalTime():dd/MM HH:mm}{(lines.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, lines) : "")}"
            : "Chưa tải bộ skill TodoX. Bộ skill tải về sau khi máy được kích hoạt license và có Claude / Codex.";

        var selected = (_skillPick.SelectedItem as SkillChoice)?.Slug;
        _skillPick.Items.Clear();
        foreach (var (slug, skill) in state.Skills.OrderBy(s => s.Value.Category).ThenBy(s => s.Value.Title))
        {
            _skillPick.Items.Add(new SkillChoice(slug, skill.Category is null ? skill.Title : $"{skill.Category} · {skill.Title}"));
        }

        var index = _skillPick.Items.Cast<SkillChoice>().ToList().FindIndex(c => c.Slug == selected);
        _skillPick.SelectedIndex = _skillPick.Items.Count == 0 ? -1 : Math.Max(index, 0);
        if (_skillPick.Items.Count == 0)
        {
            _versionPick.Items.Clear();
        }
    }

    private void FillVersions()
    {
        _versionPick.Items.Clear();
        if (_skillPick.SelectedItem is not SkillChoice choice || !_skills.LoadState().Skills.TryGetValue(choice.Slug, out var skill))
        {
            return;
        }

        _versionPick.Items.Add(new VersionChoice(null, $"Luôn dùng bản mới nhất (v{skill.Latest})"));
        foreach (var v in skill.Versions.OrderByDescending(v => v.Version))
        {
            _versionPick.Items.Add(new VersionChoice(v.Version, $"v{v.Version} – {v.Name}{(v.CreatedAt is { } at ? $" · {at.ToLocalTime():dd/MM/yyyy}" : "")}"));
        }

        _versionPick.SelectedIndex = skill.Pinned is { } p ? Math.Max(_versionPick.Items.Cast<VersionChoice>().ToList().FindIndex(c => c.Version == p), 0) : 0;
    }

    private Task PinSkillAsync()
    {
        if (_skillPick.SelectedItem is not SkillChoice skill || _versionPick.SelectedItem is not VersionChoice version)
        {
            Say("Chọn skill và phiên bản trước.");
            return Task.CompletedTask;
        }

        var (_, message) = _skills.Pin(skill.Slug, version.Version);
        Say(message + " Mở cuộc trò chuyện mới trong Claude / Codex để dùng.");
        RefreshSkills();
        return Task.CompletedTask;
    }

    private async Task UpdateAsync()
    {
        Say(await Task.Run(() => _updater.CheckAndApplyAsync(new Progress<string>(Say), _closing.Token)));
        await RunHealthAsync();
        Changed?.Invoke();
    }

    private Task ReinstallAsync()
    {
        // The setup wizard repairs the install in its folder; this window and the main one close.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "setup") { UseShellExecute = false });
        Application.Exit();
        return Task.CompletedTask;
    }

    private async Task RunHealthAsync()
    {
        try
        {
            var report = await _health.RunAsync(1, Say, ct: _closing.Token);
            _checks.Controls.Clear();
            foreach (var item in report.Items)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new StatusBadge(item.Ok ? BadgeKind.Done : BadgeKind.Failed, 18) { Margin = new Padding(0, 3, 10, 0) });
                row.Controls.Add(new Label { Text = item.Name, AutoSize = false, Width = 250, Height = 24, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var detail = new Label { Text = (item.Ok ? item.Detail : item.Id == "license" ? "chưa kích hoạt (xem mục Kích hoạt)" : $"{item.Detail} — {item.Fix}").ReplaceLineEndings(" ").Trim(), AutoSize = false, Width = 340, Height = 24, AutoEllipsis = true, ForeColor = item.Ok ? Ui.Muted : Ui.Red, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                _tips.SetToolTip(detail, detail.Text);
                row.Controls.Add(detail);
                _checks.Controls.Add(row);
            }

            _versionLabel.Text = $"TodoX EditVideo {StatusService.Version}";
            Say(report.Ok ? "Hệ thống sẵn sàng." : "Có mục cần xử lý (xem Kiểm tra hệ thống).");
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ------------------------------------------------------------------ helpers

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

    private static RadioButton Choice(string text, bool on) => new()
    {
        Text = text,
        Checked = on,
        AutoSize = true,
        ForeColor = Ui.Ink,
        Font = new Font("Segoe UI", 10.5f),
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 0, 28, 8)
    };

    private static Label Title(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 18),
        ForeColor = Ui.Ink,
        AutoSize = true,
        UseMnemonic = false,
        Margin = new Padding(0, 0, 0, 14)
    };

    private Button Action(string text, Func<Task> handler, bool primary = false)
    {
        var button = Ui.Button(text, primary);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                Say("Lỗi: " + ex.Message);
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Enabled = true;
                }
            }
        };
        return button;
    }
}
