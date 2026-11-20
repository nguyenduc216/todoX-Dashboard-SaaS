using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Storage;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>
/// Cài đặt → Fanpage Facebook: the user's own Facebook app (App ID + Secret), token expiry, the daily token check
/// that renews page tokens before they expire, and the token renewal history.
/// </summary>
public sealed partial class MainForm
{
    private TokenHistory _tokenHistory = null!;
    private TokenKeeper _tokenKeeper = null!;
    private readonly TextBox _appIdInput = Input(200, "App ID");
    private readonly TextBox _appSecretInput = Input(300, "App Secret", secret: true);
    private readonly Label _appStatus = Theme.Text("", Muted);
    private readonly ListView _tokenEvents = Theme.List();
    private bool _tokenCheckRunning;

    private void SetupTokens()
    {
        _tokenHistory = TokenHistory.Load(_paths.TokenHistoryFile);
        _tokenKeeper = new TokenKeeper(_facebook, _tokenHistory);
        _tokenHistory.Changed += () => OnUi(RefreshTokenHistory);
    }

    private void AddFacebookAppSection(FlowLayoutPanel page)
    {
        page.Controls.Add(Section("Tự gia hạn token — ứng dụng Facebook của bạn (tuỳ chọn)"));
        page.Controls.Add(Theme.Text(
            "Có App ID + App Secret, app tự đổi token ngắn hạn sang dài hạn và tự lấy page token mới trước khi hết hạn — không cần mở Access Token Debugger.\n" +
            "Cách lấy: developers.facebook.com → My Apps → Create App (loại Business) → App settings → Basic → copy App ID và App Secret. " +
            "Khi tạo token ở Graph API Explorer, chọn đúng ứng dụng này ở ô \"Meta App\" rồi dán token vào Cách 1.\n" +
            "App Secret được mã hoá bằng Windows (DPAPI) và chỉ gửi tới Facebook.", Muted));
        var row = Row();
        _appIdInput.Text = _settings.FacebookApp.AppId;
        var save = Theme.Button("Lưu", primary: true);
        save.Click += (_, _) => SaveFacebookApp();
        var remove = Theme.Button("Xoá App ID / Secret");
        remove.Click += (_, _) =>
        {
            if (!_settings.FacebookApp.HasCredentials || MessageBox.Show("Xoá App ID + App Secret? Page token đang có vẫn dùng được; app chỉ không tự đổi token sang dài hạn nữa.",
                    "todoX ReupVideo", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }

            _settings.FacebookApp.AppId = "";
            _settings.FacebookApp.ProtectedSecret = "";
            _appIdInput.Clear();
            SaveSettings();
            _tokenHistory.Add(TokenEventKind.Updated, null, "Ứng dụng Facebook", "Đã xoá App ID + App Secret.");
            UpdateAppStatus();
        };
        var create = Theme.Button("Mở trang tạo ứng dụng");
        create.Click += (_, _) => OpenUrl("https://developers.facebook.com/apps/");
        row.Controls.AddRange([_appIdInput, _appSecretInput, save, remove, create]);
        page.Controls.Add(row);
        page.Controls.Add(_appStatus);
        UpdateAppStatus();
    }

    private void AddTokenHistorySection(FlowLayoutPanel page)
    {
        page.Controls.Add(Section("Lịch sử token / gia hạn"));
        Columns(_tokenEvents, ("Thời gian", 112), ("Fanpage / tài khoản", 190), ("Sự kiện", 120), ("Chi tiết", -1), ("Hạn mới", 150));
        _tokenEvents.ShowItemToolTips = true;
        _tokenEvents.Height = S(230);
        page.Controls.Add(_tokenEvents);
        page.Controls.Add(Theme.Text(
            "Mỗi ngày app tự hỏi Facebook hạn của từng token (lúc đang mở hoặc chạy ở khay hệ thống). Token hết hạn hoặc còn dưới 7 ngày sẽ được tự lấy lại bằng token tài khoản đã lưu; " +
            "không tự gia hạn được thì Trang chính báo vàng và khay hệ thống hiện thông báo. Lịch sử không bao giờ lưu nội dung token.", Muted));
        RefreshTokenHistory();
    }

    private void SaveFacebookApp()
    {
        var id = _appIdInput.Text.Trim();
        var secret = _appSecretInput.Text.Trim();
        if (id.Length == 0 || !id.All(char.IsDigit))
        {
            MessageBox.Show("App ID là dãy số, VD 1234567890123456.", "todoX ReupVideo");
            return;
        }

        if (secret.Length == 0 && !(_settings.FacebookApp.HasCredentials && _settings.FacebookApp.AppId == id))
        {
            MessageBox.Show("Nhập App Secret (App settings → Basic → App Secret → Show).", "todoX ReupVideo");
            return;
        }

        _settings.FacebookApp.AppId = id;
        if (secret.Length > 0)
        {
            _settings.FacebookApp.ProtectedSecret = SecretProtector.Protect(secret);
        }

        _appSecretInput.Clear();
        SaveSettings();
        _tokenHistory.Add(TokenEventKind.Updated, null, "Ứng dụng Facebook", $"Lưu App ID {id} + App Secret.");
        UpdateAppStatus();
        _log.Report($"Đã lưu ứng dụng Facebook (App ID {id}). Dán token tạo từ ứng dụng này vào Cách 1 để có token dài hạn.");
    }

    private void UpdateAppStatus()
    {
        var app = _settings.FacebookApp;
        var user = app.ProtectedUserToken.Length == 0
            ? "Chưa lưu token tài khoản dài hạn — dán token ở Cách 1 để app tự gia hạn page token."
            : $"Token tài khoản đã lưu: {TokenKeeper.Describe(app.UserTokenExpiresAt)}.";
        var check = app.LastAutoCheckAt is { } at ? $" Kiểm tra hằng ngày gần nhất: {at:HH:mm dd/MM}." : "";
        _appStatus.Text = (app.HasCredentials ? $"Đã lưu App ID {app.AppId} (App Secret: đã mã hoá). " : "Chưa có App ID / App Secret. ") + user + check;
        _appStatus.ForeColor = app.HasCredentials && app.ProtectedUserToken.Length > 0 ? Green : Muted;
    }

    private void RefreshTokenHistory()
    {
        if (_tokenEvents.Columns.Count == 0)
        {
            return;
        }

        _tokenEvents.BeginUpdate();
        _tokenEvents.Items.Clear();
        foreach (var e in _tokenHistory.Snapshot().Take(300))
        {
            var item = new ListViewItem([
                e.At.ToString("HH:mm dd/MM/yy"),
                e.Name,
                KindText(e.Kind),
                e.Detail,
                e.NeverExpires ? "Không hết hạn" : e.ExpiresAt is { } x ? x.ToString("HH:mm dd/MM/yyyy") : ""
            ]) { UseItemStyleForSubItems = false, ToolTipText = e.Detail };
            item.SubItems[2].ForeColor = e.Kind switch
            {
                TokenEventKind.Renewed or TokenEventKind.Exchanged => Green,
                TokenEventKind.Expired or TokenEventKind.Failed => Red,
                TokenEventKind.Warning => Yellow,
                _ => Muted
            };
            _tokenEvents.Items.Add(item);
        }

        _tokenEvents.EndUpdate();
        UpdateAppStatus();
    }

    private static string KindText(TokenEventKind kind) => kind switch
    {
        TokenEventKind.Updated => "Cập nhật",
        TokenEventKind.Exchanged => "Đổi dài hạn",
        TokenEventKind.Renewed => "Tự gia hạn",
        TokenEventKind.Checked => "Kiểm tra",
        TokenEventKind.Warning => "Sắp hết hạn",
        TokenEventKind.Expired => "Hết hạn",
        TokenEventKind.Failed => "Lỗi gia hạn",
        _ => kind.ToString()
    };

    /// <summary>"Hạn token" column of the fanpage list.</summary>
    private static (string Text, Color Color) ExpiryText(FacebookPage fb)
    {
        var now = DateTime.Now;
        if (fb.ProtectedToken.Length == 0)
        {
            return ("—", Muted);
        }

        if (fb.TokenExpired)
        {
            return ("HẾT HẠN", Red);
        }

        if (fb.TokenCheckedAt is null)
        {
            return ("chưa kiểm tra", Muted);
        }

        if (TokenKeeper.NeedsRenew(fb, now))
        {
            return (TokenKeeper.ProblemText(fb).Replace("Token hết hạn", "hết hạn"), Yellow);
        }

        return fb.TokenExpiresAt is { } at ? ($"đến {at:dd/MM/yyyy}", Ink) : ("Không hết hạn", Green);
    }

    /// <summary>Daily check (from the scheduler tick), also run after a reup found an expired token.</summary>
    private async Task MaybeCheckTokensAsync()
    {
        var expiredDuringRun = _settings.FacebookPages.Any(p => p.TokenExpired) && _settings.FacebookApp.ProtectedUserToken.Length > 0 &&
                               _settings.FacebookApp.LastAutoCheckAt is { } last && DateTime.Now - last > TimeSpan.FromMinutes(30);
        if (_runner.IsRunning || _tokenCheckRunning || !(TokenKeeper.IsDue(_settings, DateTime.Now) || expiredDuringRun))
        {
            return;
        }

        await RunTokenCheckAsync(null, null);
    }

    private async Task RunTokenCheckAsync(IReadOnlyCollection<FacebookPage>? only, Button? button)
    {
        if (_tokenCheckRunning)
        {
            return;
        }

        _tokenCheckRunning = true;
        if (button is not null)
        {
            button.Enabled = false;
        }

        try
        {
            var report = await _tokenKeeper.CheckAsync(_settings, only, _log, CancellationToken.None);
            SaveSettings();
            _log.Report($"Kiểm tra token: {report.Checked} trang, {report.Renewed} tự gia hạn, {report.Problems.Count} cần xử lý.");
            foreach (var p in report.Problems)
            {
                _log.Report($"  ⚠ {p.Page.Name}: {p.Text}");
            }

            if (report.Problems.Count > 0 && !Visible)
            {
                _tray.ShowBalloonTip(8000, "todoX ReupVideo — token fanpage",
                    $"{report.Problems.Count} fanpage cần cập nhật token: {string.Join(", ", report.Problems.Take(3).Select(p => p.Page.Name))}. Mở Cài đặt → Fanpage Facebook.",
                    ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _log.Report("Không kiểm tra được token: " + ex.Message);
        }
        finally
        {
            _tokenCheckRunning = false;
            if (button is not null)
            {
                button.Enabled = true;
            }

            RefreshPages();
            RefreshHome();
            UpdateAppStatus();
        }
    }
}
