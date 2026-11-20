using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Facebook;
using TodoX.ReupVideo.Storage;
using TodoX.ReupVideo.TikTok;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>Cài đặt: fanpages, TikTok channels, schedule, posting rules, system.</summary>
public sealed partial class MainForm
{
    private const int SettingsTabPages = 0, SettingsTabChannels = 1, SettingsTabMapping = 2, SettingsTabSchedule = 3;

    private Control _settingsTabs = null!;

    // fanpages
    private readonly ListView _pages = Theme.List(checkboxes: true);
    private readonly TextBox _pageIdInput = Input(180, "Page ID (để trống = tự nhận)");
    private readonly TextBox _pageTokenInput = Input(400, "Page access token", secret: true);
    private readonly TextBox _userTokenInput = Input(400, "User access token (để lấy tất cả fanpage)", secret: true);
    private bool _fillingPages;

    // channels
    private readonly ListView _channels = Theme.List(checkboxes: true);
    private readonly TextBox _channelInput = Input(400, "@tenkenh hoặc link kênh TikTok");
    private readonly TextBox _manualLink = Input(460, "https://www.tiktok.com/@kenh/video/...");
    private bool _fillingChannels;

    private readonly Label _ytdlpLabel = Theme.Text("", Muted);

    private Control BuildSettingsPage()
    {
        _settingsTabs = Tabs(
            ("Fanpage Facebook", BuildPagesTab()),
            ("Kênh TikTok", BuildChannelsTab()),
            ("Kênh → Fanpage", BuildMappingTab()),
            ("Lịch chạy", BuildScheduleTab()),
            ("Đăng bài", BuildPostTab()),
            ("Hệ thống", BuildSystemTab()));
        return _settingsTabs;
    }

    private void OpenSettingsTab(int tab)
    {
        ShowPage(PageSettings);
        ((Action<int>)_settingsTabs.Tag!)(tab);
    }

    // ================================================================== Fanpage Facebook

    private Control BuildPagesTab()
    {
        var page = Page(out var host);
        page.Controls.Add(Section("Fanpage — tick các trang sẽ đăng video"));
        Columns(_pages, ("Fanpage", -1), ("Page ID", 140), ("Reup từ kênh TikTok", 190), ("Kiểu đăng", 76), ("Token", 120), ("Hạn token", 160), ("Kiểm tra", 150));
        _pages.Height = S(220);
        _pages.ItemChecked += (_, e) =>
        {
            if (_fillingPages || e.Item.Tag is not FacebookPage fb)
            {
                return;
            }

            fb.Enabled = e.Item.Checked;
            SaveSettings();
            RefreshHome();
        };
        page.Controls.Add(_pages);
        var row = Row();
        var check = Theme.Button("Kiểm tra & gia hạn token");
        check.Click += async (_, _) =>
        {
            var selected = SelectedPages();
            await RunTokenCheckAsync(selected.Count > 0 ? selected : _settings.FacebookPages.ToList(), check);
        };
        var kind = Theme.Button("Đổi kiểu đăng (Reels ↔ Video)");
        kind.Click += (_, _) =>
        {
            foreach (var fb in SelectedPages())
            {
                fb.PostKind = fb.PostKind == FacebookPostKind.Reels ? FacebookPostKind.Video : FacebookPostKind.Reels;
            }

            SaveSettings();
            RefreshPages();
        };
        var open = Theme.Button("Mở fanpage");
        open.Click += (_, _) => { foreach (var fb in SelectedPages()) OpenUrl(FacebookUrl(fb.PageId)); };
        var remove = Theme.Button("Xoá");
        remove.Click += (_, _) =>
        {
            var selected = SelectedPages();
            if (selected.Count == 0 || MessageBox.Show($"Xoá {selected.Count} fanpage khỏi ứng dụng?", "todoX ReupVideo", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }

            _settings.FacebookPages.RemoveAll(selected.Contains);
            SaveSettings();
            RefreshPages();
            RefreshHome();
        };
        row.Controls.AddRange([check, kind, open, remove]);
        page.Controls.Add(row);
        page.Controls.Add(Theme.Text("Reels: video dọc tối đa 90 giây (dài hơn tự đăng dạng Video). Token được mã hoá bằng Windows (DPAPI), chỉ dùng được trên máy và tài khoản Windows này.", Muted));

        page.Controls.Add(Section("Thêm fanpage — cách 1: lấy tất cả bằng User access token"));
        page.Controls.Add(Theme.Text("Tạo token trên Graph API Explorer với quyền pages_show_list, pages_manage_posts, pages_read_engagement, publish_video. Ứng dụng tự lấy Page token cho từng fanpage.\n\nQUAN TRỌNG — token Graph API Explorer chỉ sống khoảng 1–2 giờ, page token lấy từ nó cũng hết hạn theo. Để page token không hết hạn: nhập App ID + App Secret ở mục \"Tự gia hạn token\" bên dưới (app tự đổi sang dài hạn), hoặc: bấm \"Gia hạn token\" → dán token, bấm Debug → \"Extend Access Token\" (cuối trang) → copy token dài hạn rồi dán vào ô này. App lưu token dài hạn (mã hoá) để tự lấy page token mới trước khi hết hạn.", Muted));
        var userRow = Row();
        var load = Theme.Button("Lấy danh sách fanpage", primary: true);
        load.Click += async (_, _) => await ImportPagesAsync(load);
        var explorer = Theme.Button("Mở Graph API Explorer");
        explorer.Click += (_, _) => OpenUrl("https://developers.facebook.com/tools/explorer/");
        var extend = Theme.Button("Gia hạn token (Access Token Debugger)");
        extend.Click += (_, _) => OpenUrl("https://developers.facebook.com/tools/debug/accesstoken/");
        userRow.Controls.AddRange([_userTokenInput, load, explorer, extend]);
        page.Controls.Add(userRow);

        page.Controls.Add(Section("Thêm fanpage — cách 2: 1 trang bằng Page access token"));
        var pageRow = Row();
        var addPage = Theme.Button("Thêm / cập nhật");
        addPage.Click += async (_, _) => await AddPageAsync(addPage);
        pageRow.Controls.AddRange([_pageIdInput, _pageTokenInput, addPage]);
        page.Controls.Add(pageRow);

        AddFacebookAppSection(page);
        AddTokenHistorySection(page);
        return host;
    }

    private List<FacebookPage> SelectedPages() => _pages.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<FacebookPage>().ToList();

    private async Task ImportPagesAsync(Button button)
    {
        var token = _userTokenInput.Text.Trim();
        if (token.Length == 0)
        {
            return;
        }

        button.Enabled = false;
        try
        {
            var (userToken, note) = await _tokenKeeper.PrepareUserTokenAsync(_settings, token, CancellationToken.None);
            var found = await _facebook.ListPagesAsync(userToken, CancellationToken.None);
            var withToken = found.Where(p => !string.IsNullOrEmpty(p.AccessToken)).ToList();
            var added = withToken.Count(UpsertPage);
            SaveSettings();
            _tokenHistory.Add(TokenEventKind.Updated, null, "Tài khoản Facebook", $"Cập nhật token cho {withToken.Count} fanpage bằng Cách 1 ({added} trang mới).");
            var imported = _settings.FacebookPages.Where(p => withToken.Any(f => f.Id == p.PageId)).ToList();
            await RunTokenCheckAsync(imported, null);
            if (note is not null)
            {
                MessageBox.Show(note, "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshPages();
            RefreshHome();
            _userTokenInput.Clear();
            _log.Report($"Lấy được {found.Count} fanpage ({added} trang mới, các trang đã có được cập nhật token).");
            if (found.Count == 0)
            {
                MessageBox.Show("Token không quản lý fanpage nào, hoặc thiếu quyền pages_show_list.", "todoX ReupVideo");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không lấy được fanpage: " + ex.Message, "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task AddPageAsync(Button button)
    {
        var token = _pageTokenInput.Text.Trim();
        if (token.Length == 0)
        {
            return;
        }

        button.Enabled = false;
        try
        {
            var info = await _facebook.VerifyPageAsync(_pageIdInput.Text, token, CancellationToken.None);
            UpsertPage(info with { AccessToken = token });
            SaveSettings();
            _tokenHistory.Add(TokenEventKind.Updated, info.Id, info.Name, "Cập nhật page token bằng Cách 2.");
            await RunTokenCheckAsync(_settings.FacebookPages.Where(p => p.PageId == info.Id).ToList(), null);
            RefreshPages();
            RefreshHome();
            _pageIdInput.Clear();
            _pageTokenInput.Clear();
            _log.Report($"Đã lưu fanpage {info.Name} ({info.Id}).");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Token không dùng được: " + ex.Message, "todoX ReupVideo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    /// <summary>Adds the page or refreshes its name/token; returns true when it is new.</summary>
    private bool UpsertPage(FacebookPageInfo info)
    {
        var existing = _settings.FacebookPages.FirstOrDefault(p => p.PageId == info.Id);
        var isNew = existing is null;
        if (existing is null)
        {
            existing = new FacebookPage { PageId = info.Id, Enabled = true };
            _settings.FacebookPages.Add(existing);
        }

        existing.Name = info.Name;
        existing.ProtectedToken = SecretProtector.Protect(info.AccessToken ?? "");
        existing.LastCheckAt = DateTime.Now;
        existing.LastCheckResult = "OK";
        existing.TokenExpired = false;
        existing.TokenCheckedAt = null;
        existing.TokenExpiresAt = null;
        existing.DataAccessExpiresAt = null;
        return isNew;
    }

    private void RefreshPages()
    {
        _fillingPages = true;
        _pages.BeginUpdate();
        _pages.Items.Clear();
        foreach (var fb in _settings.FacebookPages)
        {
            var token = SecretProtector.Unprotect(fb.ProtectedToken);
            var item = new ListViewItem([
                fb.Name,
                fb.PageId,
                ChannelsOf(fb),
                fb.PostKind == FacebookPostKind.Reels ? "Reels" : "Video",
                token.Length == 0 ? "cần nhập lại" : fb.TokenExpired ? "HẾT HẠN — nhập token mới" : SecretProtector.Mask(token),
                ExpiryText(fb).Text,
                fb.LastCheckAt is null ? "" : $"{fb.LastCheckAt:HH:mm dd/MM} {fb.LastCheckResult}"
            ]) { Tag = fb, Checked = fb.Enabled, UseItemStyleForSubItems = false };
            item.SubItems[5].ForeColor = ExpiryText(fb).Color;
            item.SubItems[2].ForeColor = item.SubItems[2].Text == "—" ? Muted : Ink;
            item.SubItems[4].ForeColor = token.Length == 0 || fb.TokenExpired ? Red : Ink;
            item.SubItems[6].ForeColor = (fb.LastCheckResult ?? "").StartsWith("OK", StringComparison.Ordinal) ? Green : Red;
            _pages.Items.Add(item);
        }

        _pages.EndUpdate();
        _fillingPages = false;
    }

    // ================================================================== Kênh TikTok

    private Control BuildChannelsTab()
    {
        var page = Page(out var host);
        page.Controls.Add(Section("Kênh TikTok — tick các kênh sẽ clone video"));
        Columns(_channels, ("Kênh", -1), ("Đăng lên fanpage", 240), ("Lần quét cuối", 110), ("Kết quả", 170));
        _channels.Height = S(240);
        _channels.ItemChecked += (_, e) =>
        {
            if (_fillingChannels || e.Item.Tag is not TikTokSource source)
            {
                return;
            }

            source.Enabled = e.Item.Checked;
            SaveSettings();
            RefreshHome();
        };
        _channels.DoubleClick += (_, _) => { if (SelectedChannels().FirstOrDefault() is { } s) OpenUrl(s.ProfileUrl); };
        page.Controls.Add(_channels);

        var row = Row();
        var all = Theme.Button("Chọn tất cả");
        all.Click += (_, _) => SetAllChannels(true);
        var none = Theme.Button("Bỏ chọn tất cả");
        none.Click += (_, _) => SetAllChannels(false);
        var open = Theme.Button("Mở kênh");
        open.Click += (_, _) => { foreach (var s in SelectedChannels()) OpenUrl(s.ProfileUrl); };
        var remove = Theme.Button("Xoá kênh");
        remove.Click += (_, _) => RemoveChannels();
        row.Controls.AddRange([all, none, open, remove]);
        page.Controls.Add(row);

        page.Controls.Add(Section("Thêm kênh TikTok"));
        var addRow = Row();
        var add = Theme.Button("Thêm kênh", primary: true);
        add.Click += (_, _) => AddChannel();
        _channelInput.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddChannel(); } };
        addRow.Controls.AddRange([_channelInput, add]);
        page.Controls.Add(addRow);
        page.Controls.Add(Theme.Text("Có thể dán nhiều kênh một lần, cách nhau dấu phẩy hoặc khoảng trắng. Kênh mới được tick sẵn; sau khi thêm, chọn fanpage nhận video của kênh ở tab \"Kênh → Fanpage\".", Muted));

        page.Controls.Add(Section("Thêm link video lẻ vào danh sách chờ reup"));
        var linkRow = Row();
        var addLink = Theme.Button("Thêm link");
        addLink.Click += (_, _) => AddManualLink();
        linkRow.Controls.AddRange([_manualLink, addLink]);
        page.Controls.Add(linkRow);
        return host;
    }

    private void AddChannel()
    {
        var parts = _channelInput.Text.Split(['\n', '\r', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var added = 0;
        var bad = new List<string>();
        foreach (var part in parts)
        {
            var user = TikTokLinks.ParseUsername(part);
            if (user is null)
            {
                bad.Add(part);
                continue;
            }

            if (_settings.TikTokSources.Any(s => s.Username.Equals(user, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _settings.TikTokSources.Add(new TikTokSource { Username = user, Enabled = true });
            added++;
        }

        if (bad.Count > 0)
        {
            MessageBox.Show("Không nhận ra kênh: " + string.Join(", ", bad) + "\nNhập dạng @tenkenh hoặc https://www.tiktok.com/@tenkenh", "todoX ReupVideo");
        }

        if (added > 0)
        {
            SaveSettings();
            _log.Report($"Đã thêm {added} kênh TikTok.");
            _channelInput.Clear();
            RefreshChannels();
            RefreshHome();
            // Next step for a new channel: choose the fanpages it reups to.
            var newest = _settings.TikTokSources[^1];
            if (_settings.FacebookPages.Count > 0)
            {
                OpenSettingsTab(SettingsTabMapping);
                RefreshMapping(newest);
            }
        }
    }

    private void AddManualLink()
    {
        var parsed = TikTokLinks.ParseVideoUrl(_manualLink.Text);
        if (parsed is null)
        {
            MessageBox.Show("Cần link video TikTok đầy đủ dạng https://www.tiktok.com/@kenh/video/123... (link rút gọn vt.tiktok.com: mở trên trình duyệt rồi copy link đầy đủ).", "todoX ReupVideo");
            return;
        }

        var (user, id) = parsed.Value;
        var added = _library.AddDiscovered([new VideoItem { VideoId = id, Url = TikTokLinks.VideoUrlOf(user, id), Author = user }]);
        _log.Report(added > 0 ? $"Đã thêm link {id} vào danh sách chờ reup." : $"Link {id} đã có trong danh sách.");
        _manualLink.Clear();
    }

    private List<TikTokSource> SelectedChannels() =>
        _channels.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<TikTokSource>().ToList();

    private void SetAllChannels(bool enabled)
    {
        _settings.TikTokSources.ForEach(s => s.Enabled = enabled);
        SaveSettings();
        RefreshChannels();
        RefreshHome();
    }

    private void RemoveChannels()
    {
        var selected = SelectedChannels();
        if (selected.Count == 0 || MessageBox.Show($"Xoá {selected.Count} kênh? (Lịch sử link đã reup vẫn được giữ.)", "todoX ReupVideo", MessageBoxButtons.YesNo) != DialogResult.Yes)
        {
            return;
        }

        _settings.TikTokSources.RemoveAll(selected.Contains);
        SaveSettings();
        RefreshChannels();
        RefreshHome();
    }

    private void RefreshChannels()
    {
        _fillingChannels = true;
        _channels.BeginUpdate();
        _channels.Items.Clear();
        foreach (var s in _settings.TikTokSources)
        {
            var item = new ListViewItem([s.ToString(), PagesOf(s), s.LastScanAt?.ToString("HH:mm dd/MM") ?? "chưa quét", s.LastScanResult ?? ""])
                { Tag = s, Checked = s.Enabled, UseItemStyleForSubItems = false };
            item.SubItems[1].ForeColor = s.PageIds.Count == 0 ? Red : Ink;
            item.SubItems[3].ForeColor = (s.LastScanResult ?? "").StartsWith("Lỗi", StringComparison.Ordinal) ? Red : Muted;
            _channels.Items.Add(item);
        }

        _channels.EndUpdate();
        _fillingChannels = false;
    }

    // ================================================================== Lịch chạy

    private Control BuildScheduleTab()
    {
        var page = Page(out var host);
        var s = _settings.Schedule;
        page.Controls.Add(Section("Chế độ chạy"));
        var manual = Radio("Chỉ khi bấm nút \"Reup ngay\"");
        var daily = Radio("Theo giờ cố định mỗi ngày");
        var every = Radio("Lặp lại sau mỗi khoảng thời gian");
        manual.Checked = s.Mode == ScheduleMode.Manual;
        daily.Checked = s.Mode == ScheduleMode.DailyTimes;
        every.Checked = s.Mode == ScheduleMode.Interval;
        var times = Input(300, "VD: 08:00, 12:00, 20:00");
        times.Text = string.Join(", ", s.DailyTimes);
        var interval = Number(SchedulePlanner.MinIntervalMinutes, 24 * 60, s.IntervalMinutes, 80);

        page.Controls.Add(manual);
        page.Controls.Add(daily);
        var dailyRow = Row();
        dailyRow.Padding = P(22, 0, 0, 0);
        dailyRow.Controls.AddRange([Inline("Các mốc giờ:"), times]);
        page.Controls.Add(dailyRow);
        page.Controls.Add(every);
        var intervalRow = Row();
        intervalRow.Padding = P(22, 0, 0, 0);
        intervalRow.Controls.AddRange([Inline("Mỗi"), interval, Inline("phút")]);
        page.Controls.Add(intervalRow);

        var save = Theme.Button("Lưu lịch chạy", primary: true);
        save.Click += (_, _) =>
        {
            var parsed = SchedulePlanner.ParseTimes([times.Text], out var invalid);
            if (daily.Checked && (parsed.Count == 0 || invalid.Count > 0))
            {
                MessageBox.Show("Giờ không hợp lệ: " + string.Join(", ", invalid) + "\nNhập dạng 08:00, 12:30, 20:00", "todoX ReupVideo");
                return;
            }

            s.Mode = daily.Checked ? ScheduleMode.DailyTimes : every.Checked ? ScheduleMode.Interval : ScheduleMode.Manual;
            if (parsed.Count > 0)
            {
                s.DailyTimes = parsed.Select(t => t.ToString(@"hh\:mm")).ToList();
            }

            s.IntervalMinutes = (int)interval.Value;
            times.Text = string.Join(", ", s.DailyTimes);
            SaveSettings();
            UpdateScheduleLabel();
            _log.Report("Đã lưu lịch chạy: " + _scheduleLabel.Text);
        };
        page.Controls.Add(save);
        page.Controls.Add(Theme.Text("Mỗi lượt tự động: cập nhật link mới từ các kênh TikTok đã tick → kiểm tra link còn active → tải video → đăng kèm nội dung + hashtag TikTok lên các fanpage đã tick. Link reup lỗi được tự thử lại ở các lượt sau (số lần trong tab Đăng bài).", Muted));
        page.Controls.Add(Theme.Text("Lịch tự động chạy khi ứng dụng đang mở (có thể thu nhỏ xuống khay hệ thống). Bật \"Khởi động cùng Windows\" ở tab Hệ thống để luôn chạy.", Muted));
        return host;
    }

    // ================================================================== Đăng bài

    private Control BuildPostTab()
    {
        var page = Page(out var host);
        var p = _settings.Post;
        page.Controls.Add(Section("Số lượng"));
        var scan = Number(1, 200, p.ScanLimitPerChannel);
        var max = Number(1, 50, p.MaxVideosPerRun);
        var delay = Number(0, 3600, p.DelaySecondsBetweenPosts, 80);
        var older = Number(0, 3650, p.SkipOlderThanDays, 80);
        var attempts = Number(1, 10, p.MaxAttempts);
        page.Controls.Add(LabeledRow("Mỗi lần quét lấy", scan, "video mới nhất của mỗi kênh TikTok"));
        page.Controls.Add(LabeledRow("Mỗi lượt reup tối đa", max, "video (mỗi video lên tất cả fanpage đã tick)"));
        page.Controls.Add(LabeledRow("Nghỉ giữa 2 lần đăng", delay, "giây"));
        page.Controls.Add(LabeledRow("Bỏ qua video TikTok cũ hơn", older, "ngày (0 = không giới hạn)"));
        page.Controls.Add(LabeledRow("Reup lỗi thì tự thử lại tối đa", attempts, "lần (sau đó bấm \"Reup lại\" ở Trang chính)"));
        var newest = Check("Ưu tiên video mới nhất trước", p.NewestFirst);
        var keep = Check("Giữ lại file video đã tải", p.KeepDownloadedFiles);
        var flags = Row();
        flags.Controls.AddRange([newest, keep]);
        page.Controls.Add(flags);

        page.Controls.Add(Section("Nội dung bài viết"));
        page.Controls.Add(Theme.Text("Mẫu: {caption} = nội dung + hashtag TikTok, {text} = nội dung không hashtag, {hashtags}, {author}, {link}", Muted));
        var template = new TextBox
        {
            Multiline = true, Width = S(600), Height = S(70), Text = p.CaptionTemplate.Replace("\n", Environment.NewLine),
            BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Font = F(10), Margin = P(0, 0, 0, 6)
        };
        DarkScrollBars(template);
        page.Controls.Add(template);
        var extra = Input(600, "Hashtag thêm vào cuối mọi bài, VD: #todox #review");
        extra.Text = p.ExtraHashtags;
        page.Controls.Add(extra);

        page.Controls.Add(Section("TikTok"));
        page.Controls.Add(Theme.Text("Cookies TikTok (tuỳ chọn, file cookies.txt dạng Netscape — chỉ cần khi TikTok chặn lấy danh sách video):", Muted));
        var cookiesRow = Row();
        var cookies = Input(480, "Đường dẫn cookies.txt");
        cookies.Text = p.TikTokCookiesFile ?? "";
        var browse = Theme.Button("Chọn file...");
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "cookies.txt|*.txt|Tất cả|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                cookies.Text = dialog.FileName;
            }
        };
        cookiesRow.Controls.AddRange([cookies, browse]);
        page.Controls.Add(cookiesRow);

        var save = Theme.Button("Lưu cài đặt đăng bài", primary: true);
        save.Click += (_, _) =>
        {
            p.ScanLimitPerChannel = (int)scan.Value;
            p.MaxVideosPerRun = (int)max.Value;
            p.DelaySecondsBetweenPosts = (int)delay.Value;
            p.SkipOlderThanDays = (int)older.Value;
            p.MaxAttempts = (int)attempts.Value;
            p.NewestFirst = newest.Checked;
            p.KeepDownloadedFiles = keep.Checked;
            p.CaptionTemplate = string.IsNullOrWhiteSpace(template.Text) ? "{caption}" : template.Text.Replace("\r\n", "\n");
            p.ExtraHashtags = extra.Text.Trim();
            p.TikTokCookiesFile = string.IsNullOrWhiteSpace(cookies.Text) ? null : cookies.Text.Trim();
            SaveSettings();
            RefreshHome();
            _log.Report("Đã lưu cài đặt đăng bài.");
        };
        page.Controls.Add(save);
        return host;
    }

    // ================================================================== Hệ thống

    private Control BuildSystemTab()
    {
        var page = Page(out var host);
        page.Controls.Add(Section("Khởi động"));
        var startup = Check("Khởi động cùng Windows (chạy ẩn ở khay hệ thống)", SafeStartupState());
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupRegistration.Set(startup.Checked);
                _settings.StartWithWindows = startup.Checked;
                SaveSettings();
            }
            catch (Exception ex)
            {
                _log.Report("Không đổi được khởi động cùng Windows: " + ex.Message);
            }
        };
        page.Controls.Add(startup);

        page.Controls.Add(Section("Công cụ tải TikTok"));
        page.Controls.Add(_ytdlpLabel);
        var row = Row();
        var updateTool = Theme.Button("Cài / cập nhật yt-dlp");
        updateTool.Click += async (_, _) => await UpdateYtDlpAsync(updateTool);
        row.Controls.Add(updateTool);
        page.Controls.Add(row);
        page.Controls.Add(Theme.Text("Khi TikTok đổi cách hiển thị, quét link có thể lỗi: bấm \"Cài / cập nhật yt-dlp\" để lấy bản sửa mới nhất.", Muted));

        AddUpdateSection(page);

        page.Controls.Add(Section("Dữ liệu trên máy (chạy offline)"));
        page.Controls.Add(Theme.Text(
            "todoX ReupVideo chạy trên máy này và không gửi thông tin của bạn lên server TodoX. Cài đặt, danh sách link, " +
            "lịch sử reup (history.json) và nhật ký từng ngày (thư mục logs) đều lưu tại:\n" + _paths.Root +
            "\nỨng dụng chỉ kết nối TikTok (lấy video), Facebook (đăng bài), GitHub của yt-dlp (tải công cụ) và todox.vn (kiểm tra phiên bản mới).", Muted));
        var dataRow = Row();
        var logs = Theme.Button("Mở thư mục nhật ký");
        logs.Click += (_, _) => { Directory.CreateDirectory(_paths.LogsDir); OpenUrl(_paths.LogsDir); };
        var data = Theme.Button("Mở thư mục dữ liệu");
        data.Click += (_, _) => OpenUrl(_paths.Root);
        dataRow.Controls.AddRange([logs, data]);
        page.Controls.Add(dataRow);

        page.Controls.Add(Section("Lưu ý"));
        page.Controls.Add(Theme.Text("Chỉ reup nội dung bạn sở hữu hoặc được chủ kênh cho phép. Đăng lại video của người khác có thể bị Facebook gỡ bài hoặc hạn chế fanpage vì vi phạm bản quyền.", Note));
        page.Controls.Add(Theme.Text($"todoX ReupVideo {Version} · Hỗ trợ: 0366 699 961 · hello@todox.vn · todox.vn", Muted));
        _ = RefreshYtDlpLabelAsync();
        return host;
    }

    private static FlowLayoutPanel LabeledRow(string before, Control input, string after)
    {
        var row = Row();
        row.Controls.AddRange([Inline(before), input, Inline(after)]);
        return row;
    }

    private static bool SafeStartupState()
    {
        try
        {
            return StartupRegistration.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task RefreshYtDlpLabelAsync()
    {
        try
        {
            var version = await _ytdlp.VersionAsync(CancellationToken.None);
            OnUi(() => _ytdlpLabel.Text = version is null
                ? "yt-dlp: chưa cài — sẽ tự tải ở lần chạy đầu (từ GitHub chính thức của yt-dlp)."
                : $"yt-dlp phiên bản {version}");
        }
        catch (Exception ex)
        {
            OnUi(() => _ytdlpLabel.Text = "yt-dlp: " + ex.Message);
        }
    }

    private async Task UpdateYtDlpAsync(Button button)
    {
        button.Enabled = false;
        try
        {
            _log.Report(await _ytdlp.UpdateAsync(_log, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _log.Report("Không cập nhật được yt-dlp: " + ex.Message);
        }
        finally
        {
            button.Enabled = true;
            await RefreshYtDlpLabelAsync();
        }
    }
}
