using System.Drawing;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.App;

/// <summary>Thư viện: the account's AI characters, voices, music and VFX (downloaded from the dashboard).</summary>
public sealed partial class MainWindow
{
    private enum LibraryTab
    {
        Characters,
        Voices,
        Music,
        Vfx
    }

    private readonly Panel _libraryBody = new() { Dock = DockStyle.Fill, BackColor = Ground };
    private readonly Label _libraryInfo = new() { AutoSize = true, ForeColor = Muted, Margin = new Padding(16, 14, 0, 0), UseMnemonic = false };
    private readonly Dictionary<LibraryTab, Button> _libraryTabs = [];
    private LibraryTab _libraryTab = LibraryTab.Characters;
    private JsonObject _libraryIndex = [];
    private Task? _libraryLoad;

    private Control LibraryScreen()
    {
        var screen = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, BackColor = Ground };
        screen.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        screen.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        screen.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        var back = Ui.Button("   Trang chính");
        back.Controls.Add(new LineIcon(TileArt.Back, 18) { Location = new Point(8, 9), BackColor = Color.Transparent, ForeColor = Ink, Enabled = false });
        back.Click += (_, _) => Go(Screen.Home);
        back.Margin = new Padding(0, 6, 18, 0);
        head.Controls.Add(back);
        head.Controls.Add(new LineIcon(TileArt.Skills, 40) { Margin = new Padding(0, 2, 12, 0) });
        head.Controls.Add(new Label { Text = "Thư viện", Font = new Font("Segoe UI Semibold", 20), ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        screen.Controls.Add(head, 0, 0);

        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        foreach (var (tab, text) in new[] { (LibraryTab.Characters, "Nhân vật"), (LibraryTab.Voices, "Giọng đọc"), (LibraryTab.Music, "Nhạc"), (LibraryTab.Vfx, "VFX") })
        {
            var b = Ui.Button(text);
            b.Click += (_, _) => ShowLibrary(tab);
            _libraryTabs[tab] = b;
            bar.Controls.Add(b);
        }

        var refresh = Ui.Button("Tải lại từ Dashboard");
        refresh.Margin = new Padding(24, 0, 0, 0);
        refresh.Click += async (_, _) => await LoadLibraryAsync(refresh: true);
        var folder = Ui.Button("Mở thư mục");
        folder.Click += (_, _) =>
        {
            var dir = _services.GetRequiredService<LibrarySync>().Folder;
            Directory.CreateDirectory(dir);
            Ui.Open(dir);
        };
        bar.Controls.AddRange([refresh, folder, _libraryInfo]);
        screen.Controls.Add(bar, 0, 1);
        screen.Controls.Add(_libraryBody, 0, 2);
        return screen;
    }

    /// <summary>One load at a time; a second request waits for the one already running.</summary>
    private Task LoadLibraryAsync(bool refresh)
    {
        if (_libraryLoad is { IsCompleted: false } running)
        {
            return running;
        }

        return _libraryLoad = LoadLibraryCoreAsync(refresh);
    }

    private async Task LoadLibraryCoreAsync(bool refresh)
    {
        _libraryInfo.Text = refresh ? "Đang tải từ Dashboard…" : "Đang mở thư viện…";
        try
        {
            var (index, error) = await _services.GetRequiredService<LibrarySync>().GetAsync(refresh, _closing.Token);
            _libraryIndex = index;
            var updated = index["updatedAt"]?.ToString();
            _libraryInfo.Text = error is not null
                ? (updated is null ? "Chưa tải được thư viện: " + error : $"Bản tải lúc {updated} (chưa cập nhật được: {error})")
                : updated is null ? "" : "Cập nhật lúc " + updated;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            _libraryInfo.Text = "Chưa tải được thư viện: " + ex.Message;
        }

        ShowLibrary(_libraryTab);
    }

    private JsonObject[] Items(string key) => (_libraryIndex[key] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];

    private static string S(JsonObject o, string key) => o[key]?.ToString() ?? "";

    private void ShowLibrary(LibraryTab tab)
    {
        _libraryTab = tab;
        foreach (var (t, b) in _libraryTabs)
        {
            Ui.Style(b, t == tab);
        }

        _libraryBody.SuspendLayout();
        foreach (Control c in _libraryBody.Controls)
        {
            c.Dispose();
        }

        _libraryBody.Controls.Clear();
        _libraryBody.Controls.Add(tab switch
        {
            LibraryTab.Characters => CharacterGrid(),
            LibraryTab.Voices => LibraryList(Items("voices"), "Chưa có giọng đọc.", "Bấm đúp để nghe giọng mẫu (giọng có chữ \"Có\" ở cột Nghe thử).",
                [("Giọng", 260), ("Mã", 200), ("Giới tính", 100), ("Ngôn ngữ", 100), ("Nghe thử", 130)],
                v => [S(v, "name") + (v["isDefault"]?.GetValue<bool>() == true ? "  (mặc định)" : ""), S(v, "code"), Gender(S(v, "gender")), S(v, "language"),
                    HasFile(v) ? "Có" : "Chưa có mẫu"],
                "Giọng này chưa có file nghe thử trên Dashboard TodoX. Quản trị viên tải file mẫu ở Dashboard › AI Studio › Giọng đọc, rồi bấm Tải lại từ Dashboard."),
            LibraryTab.Music => LibraryList(Items("music"), "Chưa có nhạc. Tạo nhạc AI bằng skill todox-nhac-suno hoặc nhờ TodoX thêm nhạc nền.",
                "Bấm đúp để nghe. Nhờ Claude dùng bài này làm nhạc nền (todox_library).",
                [("Bài nhạc", 330), ("Nguồn", 130), ("Thể loại", 130), ("Thời lượng", 110)],
                m => [S(m, "name"), S(m, "source") == "ai" ? "Nhạc AI của bạn" : "Nhạc TodoX", S(m, "category"),
                    m["durationSeconds"] is JsonValue d && d.TryGetValue<int>(out var sec) ? $"{sec / 60}:{sec % 60:00}" : ""],
                "Chưa tải được file bài nhạc này về máy. Bấm Tải lại từ Dashboard."),
            _ => LibraryList(Items("vfx"), "Chưa có model VFX nào bật cho EditVideo.", "Nhờ Claude dùng VFX với một clip (skill todox-vfx); giá hiện khi tạo.",
                [("Hiệu ứng (model)", 330), ("Mã model", 260), ("Hạng", 120)],
                x => [S(x, "name"), S(x, "model"), S(x, "tier")])
        });
        _libraryBody.ResumeLayout();
    }

    private static string Gender(string g) => g.ToLowerInvariant() switch
    {
        "female" or "f" or "nu" or "nữ" => "Nữ",
        "male" or "m" or "nam" => "Nam",
        _ => g
    };

    private Control CharacterGrid()
    {
        var items = Items("characters");
        if (items.Length == 0)
        {
            return Empty("Tài khoản chưa có nhân vật AI. Tạo nhân vật trên Dashboard (Tài nguyên AI › Nhân vật) rồi bấm Tải lại từ Dashboard.");
        }

        var grid = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MaximumSize = new Size(1, 0), BackColor = Ground };
        foreach (var c in items)
        {
            var card = new Panel { Width = 200, Height = 300, BackColor = Ui.Card, Margin = new Padding(0, 0, 14, 14), Cursor = Cursors.Hand };
            var pic = new PictureBox { Location = new Point(10, 10), Size = new Size(180, 200), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Panel };
            var path = S(c, "localPath");
            if (path.Length > 0 && File.Exists(path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    pic.Image = Image.FromStream(stream);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
                {
                    // Not an image this app can show; the name still is.
                }
            }

            var name = new Label { Text = S(c, "name"), Location = new Point(10, 214), Size = new Size(180, 28), ForeColor = Ink, Font = new Font("Segoe UI Semibold", 10.5f), AutoEllipsis = true, UseMnemonic = false };
            var desc = new Label
            {
                Text = string.Join(" · ", new[] { Gender(S(c, "gender")), S(c, "style") }.Where(x => x.Length > 0)) + (S(c, "description") is { Length: > 0 } d ? "\n" + d : ""),
                Location = new Point(10, 244), Size = new Size(180, 50), ForeColor = Muted, Font = new Font("Segoe UI", 9f), AutoEllipsis = true, UseMnemonic = false
            };
            card.Controls.AddRange([pic, name, desc]);
            _tips.SetToolTip(pic, S(c, "name") + (S(c, "description") is { Length: > 0 } t ? "\n" + t : "") + "\nBấm để mở ảnh.");
            foreach (Control ctl in new Control[] { card, pic, name, desc })
            {
                ctl.Click += (_, _) =>
                {
                    if (path.Length > 0 && File.Exists(path))
                    {
                        Ui.Open(path);
                    }
                };
            }

            grid.Controls.Add(card);
        }

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ground };
        host.Controls.Add(grid);
        host.Resize += (_, _) => grid.MaximumSize = new Size(Math.Max(220, host.ClientSize.Width - 4), 0);
        grid.MaximumSize = new Size(Math.Max(220, _libraryBody.ClientSize.Width - 4), 0);
        return host;
    }

    private static bool HasFile(JsonObject o) => S(o, "localPath") is { Length: > 0 } f && File.Exists(f);

    private Control LibraryList(JsonObject[] items, string empty, string hint, (string Title, int Width)[] columns, Func<JsonObject, string[]> cells,
        string? noFile = null)
    {
        if (items.Length == 0)
        {
            return Empty(empty);
        }

        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(16) };
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
        list.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Ui.Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, new Font("Segoe UI", 9.5f), Rectangle.Inflate(e.Bounds, -4, 0), Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        foreach (var (title, width) in columns)
        {
            list.Columns.Add(title, width);
        }

        Ui.FitList(list, 160);
        foreach (var o in items)
        {
            var values = cells(o);
            var item = new ListViewItem(values[0]) { Tag = S(o, "localPath") };
            foreach (var v in values.Skip(1))
            {
                item.SubItems.Add(v);
            }

            list.Items.Add(item);
        }

        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItems.Count == 0)
            {
                return;
            }

            if (list.SelectedItems[0].Tag is string { Length: > 0 } file && File.Exists(file))
            {
                Ui.Open(file);
            }
            else if (noFile is not null)
            {
                MessageBox.Show(this, noFile, "TodoX EditVideo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        var tip = new Label { Text = hint, Dock = DockStyle.Bottom, Height = 30, ForeColor = Muted, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
        panel.Controls.Add(list);
        panel.Controls.Add(tip);
        return panel;
    }

    private static Control Empty(string text) => new Label
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 11),
        TextAlign = ContentAlignment.TopLeft,
        Padding = new Padding(4, 12, 4, 0),
        UseMnemonic = false
    };
}
