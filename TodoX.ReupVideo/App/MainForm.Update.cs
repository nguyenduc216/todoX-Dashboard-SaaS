using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>
/// Self-update: checks todox.vn shortly after start and every 6 hours. With "Tự động cập nhật" on, a newer build is
/// downloaded in the background; it is installed at once when the app sits in the tray and is idle, otherwise a
/// yellow bar on Trang chính offers "Cập nhật ngay" (and installs it the next time the window is hidden).
/// </summary>
public sealed partial class MainForm
{
    private AppUpdater _updater = null!;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60_000 };
    private readonly Panel _updateBar = new() { Dock = DockStyle.Top, Visible = false, BackColor = ColorTranslator.FromHtml("#2A2210") };
    private readonly Label _updateText = new() { AutoSize = true, ForeColor = Note, Font = F(10), Margin = P(0, 12, 14, 0) };
    private readonly Label _updateState = Theme.Text("", Muted);
    private Button _updateNowButton = null!;
    private UpdateInfo? _pendingUpdate;
    private string? _downloadedUpdate;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _checkingUpdate;
    /// <summary>Auto-update on and the window open: install at this time unless the user pressed "Để sau".</summary>
    private DateTime? _autoInstallAt;

    private void SetupUpdater()
    {
        _updater = new AppUpdater(_paths, _http, typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0));

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = _updateBar.BackColor, Padding = P(16, 0, 16, 0), WrapContents = false };
        _updateNowButton = Theme.Button("Cập nhật ngay", primary: true);
        _updateNowButton.Margin = P(0, 5, 8, 0);
        _updateNowButton.Click += (_, _) => InstallUpdate(askFirst: false);
        var later = new LinkLabel { Text = "Để sau", AutoSize = true, LinkColor = Muted, ActiveLinkColor = Yellow, Font = F(9.5f), Margin = P(4, 13, 0, 0) };
        later.LinkClicked += (_, _) =>
        {
            _updateBar.Visible = false;
            _autoInstallAt = null; // installs when the window is hidden, or at the next check
        };
        flow.Controls.AddRange([_updateText, _updateNowButton, later]);
        _updateBar.Height = S(46);
        _updateBar.Controls.Add(flow);
        Controls.Add(_updateBar);
        _updateBar.BringToFront();

        // First check 20 s after start, then every 6 hours (the timer ticks each minute).
        _lastUpdateCheck = DateTime.Now - AppUpdater.CheckEvery + TimeSpan.FromSeconds(20);
        _updateTimer.Tick += async (_, _) => await UpdateTickAsync();
        _updateTimer.Start();
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                TryInstallQuietly();
            }
        };
        UpdateStateLabel();
    }

    private async Task UpdateTickAsync()
    {
        if (DateTime.Now - _lastUpdateCheck >= AppUpdater.CheckEvery)
        {
            await CheckForUpdateAsync(manual: false);
        }

        TryInstallQuietly();
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_checkingUpdate)
        {
            return;
        }

        _checkingUpdate = true;
        _lastUpdateCheck = DateTime.Now;
        try
        {
            var info = await _updater.CheckAsync(CancellationToken.None);
            if (info is null)
            {
                _updateState.Text = $"Đang dùng bản mới nhất ({Version}) · kiểm tra lúc {DateTime.Now:HH:mm dd/MM}.";
                if (manual)
                {
                    MessageBox.Show($"Bạn đang dùng phiên bản mới nhất ({Version}).", "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            _pendingUpdate = info;
            if (!_settings.AutoUpdate && !manual)
            {
                ShowUpdateBar($"Có phiên bản mới {info.Version.ToString(3)} (bạn đang dùng {Version}).", "Tải và cập nhật");
                return;
            }

            _log.Report($"Có phiên bản mới {info.Version.ToString(3)}, đang tải về...");
            _updateState.Text = $"Đang tải phiên bản {info.Version.ToString(3)}...";
            _downloadedUpdate = await _updater.DownloadAsync(info, CancellationToken.None);
            _log.Report($"Đã tải xong phiên bản {info.Version.ToString(3)}.");
            _autoInstallAt = manual ? null : DateTime.Now.AddMinutes(2);
            ShowUpdateBar(manual
                ? $"Phiên bản mới {info.Version.ToString(3)} đã sẵn sàng (bạn đang dùng {Version})."
                : $"Phiên bản mới {info.Version.ToString(3)} đã sẵn sàng (bạn đang dùng {Version}). Ứng dụng tự khởi động lại để cập nhật lúc {_autoInstallAt:HH:mm} (khi không có lượt reup đang chạy).", "Cập nhật ngay");
            if (manual)
            {
                InstallUpdate(askFirst: true);
            }
        }
        catch (Exception ex)
        {
            _updateState.Text = "Không kiểm tra được cập nhật: " + ex.Message;
            if (manual)
            {
                MessageBox.Show("Không kiểm tra được cập nhật: " + ex.Message + "\n\nCó thể tải bản mới tại todox.vn/reupvideo.", "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkingUpdate = false;
            UpdateStateLabel();
        }
    }

    private void ShowUpdateBar(string text, string button)
    {
        _updateText.Text = text;
        _updateNowButton.Text = button;
        _updateBar.Visible = true;
        if (!Visible)
        {
            _tray.ShowBalloonTip(5000, "todoX ReupVideo", text, ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// Auto-update on and idle: install without asking when the window is hidden in the tray, or when it is open and the
    /// announced restart time has come (the window used to stay open for days and never got the update).
    /// </summary>
    private void TryInstallQuietly()
    {
        var timeHasCome = _autoInstallAt is { } at && DateTime.Now >= at;
        if (_downloadedUpdate is not null && _settings.AutoUpdate && (!Visible || timeHasCome) && !_runner.IsRunning && !_tokenCheckRunning && !_installingUpdate)
        {
            InstallUpdate(askFirst: false);
        }
    }

    private bool _installingUpdate;

    private async void InstallUpdate(bool askFirst)
    {
        if (_installingUpdate)
        {
            return;
        }

        if (_downloadedUpdate is null || _pendingUpdate is null)
        {
            // Manual update with auto-update off, or the bar was shown before download.
            await CheckForUpdateAsync(manual: true);
            return;
        }

        if (_runner.IsRunning)
        {
            MessageBox.Show("Đang có lượt reup chạy dở. Ứng dụng sẽ cập nhật khi lượt này xong (hoặc bấm Dừng rồi cập nhật).", "todoX ReupVideo");
            return;
        }

        if (askFirst && MessageBox.Show($"Cập nhật lên phiên bản {_pendingUpdate.Version.ToString(3)} và khởi động lại ứng dụng?", "todoX ReupVideo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _installingUpdate = true;
        try
        {
            _log.Report($"Cập nhật lên phiên bản {_pendingUpdate.Version.ToString(3)} và khởi động lại.");
            SaveSettings();
            AppUpdater.ApplyAndRestart(_downloadedUpdate, _pendingUpdate.FileName, startMinimized: !Visible);
            _exiting = true;
            Close();
        }
        catch (Exception ex)
        {
            _installingUpdate = false;
            _log.Report("Không cài được bản cập nhật: " + ex.Message);
            if (Visible)
            {
                MessageBox.Show("Không cài được bản cập nhật: " + ex.Message + "\n\nHãy tải bản mới tại todox.vn/reupvideo và chạy file đó.",
                    "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void UpdateStateLabel()
    {
        if (_updateState.Text.Length == 0)
        {
            _updateState.Text = $"Phiên bản đang dùng: {Version}.";
        }
    }

    /// <summary>Section in Cài đặt → Hệ thống.</summary>
    private void AddUpdateSection(FlowLayoutPanel page)
    {
        page.Controls.Add(Section("Cập nhật phiên bản"));
        var auto = Check("Tự động cập nhật khi có phiên bản mới (khuyên dùng)", _settings.AutoUpdate);
        auto.CheckedChanged += (_, _) =>
        {
            _settings.AutoUpdate = auto.Checked;
            SaveSettings();
        };
        page.Controls.Add(auto);
        page.Controls.Add(_updateState);
        var row = Row();
        var check = Theme.Button("Kiểm tra cập nhật");
        check.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        var page2 = Theme.Button("Mở trang tải app");
        page2.Click += (_, _) => OpenUrl(Website + "/reupvideo");
        row.Controls.AddRange([check, page2]);
        page.Controls.Add(row);
        page.Controls.Add(Theme.Text("Ứng dụng chỉ hỏi todox.vn xem có phiên bản mới không (không gửi dữ liệu của bạn). Bản mới được kiểm tra mã SHA-256 trước khi thay thế.", Muted));
    }
}
