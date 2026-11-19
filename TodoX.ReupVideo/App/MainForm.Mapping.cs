using System.Drawing;
using System.Windows.Forms;
using TodoX.ReupVideo.Core;
using TodoX.ReupVideo.Storage;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>
/// Cài đặt → Kênh → Fanpage: for each TikTok channel, the fanpages its videos are reuploaded to.
/// Left: channels (with how many fanpages each has); right: tick the fanpages for the selected channel.
/// </summary>
public sealed partial class MainForm
{
    private readonly ListView _mapChannels = Theme.List();
    private readonly ListView _mapPages = Theme.List(checkboxes: true);
    private readonly Label _mapTitle = new() { AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 6, 0, 6) };
    private bool _fillingMap;

    private Control BuildMappingTab()
    {
        var page = Page(out var host);
        page.Controls.Add(Section("Kênh TikTok nào reup lên fanpage nào"));
        page.Controls.Add(Theme.Text("Chọn một kênh TikTok bên trái, rồi tick các fanpage sẽ nhận video của kênh đó. Kênh chưa chọn fanpage sẽ không được đăng. Fanpage phải đang được tick ở tab Fanpage Facebook thì mới đăng.", Muted, 9.5f, 900));

        Columns(_mapChannels, ("Kênh TikTok", -1), ("Fanpage", 80));
        _mapChannels.MultiSelect = false;
        _mapChannels.Width = S(380);
        _mapChannels.Height = S(340);
        _mapChannels.SelectedIndexChanged += (_, _) => FillMapPages();

        Columns(_mapPages, ("Fanpage nhận video", -1), ("Page ID", 150));
        _mapPages.Width = S(470);
        _mapPages.Height = S(340);
        _mapPages.ItemChecked += (_, e) =>
        {
            if (_fillingMap || SelectedMapChannel() is not { } source || e.Item.Tag is not FacebookPage fb)
            {
                return;
            }

            if (e.Item.Checked && !source.PageIds.Contains(fb.PageId))
            {
                source.PageIds.Add(fb.PageId);
            }
            else if (!e.Item.Checked)
            {
                source.PageIds.Remove(fb.PageId);
            }

            SaveSettings();
            RefreshMappingCounts();
            RefreshChannels();
            RefreshPages();
            RefreshHome();
        };

        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 18, 0) };
        left.Controls.Add(new Label { Text = "KÊNH TIKTOK", AutoSize = true, ForeColor = Yellow, Font = F(9, FontStyle.Bold), Margin = P(0, 6, 0, 6) });
        left.Controls.Add(_mapChannels);

        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        right.Controls.Add(_mapTitle);
        right.Controls.Add(_mapPages);
        var buttons = Row();
        var all = Theme.Button("Chọn tất cả fanpage");
        all.Click += (_, _) => SetMapAll(true);
        var none = Theme.Button("Bỏ chọn tất cả");
        none.Click += (_, _) => SetMapAll(false);
        var copy = Theme.Button("Áp dụng cho mọi kênh");
        copy.Click += (_, _) => CopyMappingToAll();
        buttons.Controls.AddRange([all, none, copy]);
        right.Controls.Add(buttons);

        var columns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = P(0, 6, 0, 0) };
        columns.Controls.AddRange([left, right]);
        page.Controls.Add(columns);
        host.VisibleChanged += (_, _) =>
        {
            if (host.Visible)
            {
                RefreshMapping();
            }
        };
        return host;
    }

    private TikTokSource? SelectedMapChannel() =>
        _mapChannels.SelectedItems.Count > 0 ? _mapChannels.SelectedItems[0].Tag as TikTokSource : null;

    /// <summary>Rebuilds the channel list, keeping the selection (or selecting <paramref name="select"/>).</summary>
    private void RefreshMapping(TikTokSource? select = null)
    {
        var keep = select ?? SelectedMapChannel();
        _mapChannels.BeginUpdate();
        _mapChannels.Items.Clear();
        foreach (var s in _settings.TikTokSources)
        {
            _mapChannels.Items.Add(new ListViewItem([s.ToString() + (s.Enabled ? "" : "  (không tick)"), ""]) { Tag = s, UseItemStyleForSubItems = false });
        }

        _mapChannels.EndUpdate();
        RefreshMappingCounts();
        var index = keep is null ? 0 : _settings.TikTokSources.IndexOf(keep);
        if (_mapChannels.Items.Count > 0)
        {
            _mapChannels.Items[Math.Max(0, index)].Selected = true;
        }

        FillMapPages();
    }

    private void RefreshMappingCounts()
    {
        foreach (ListViewItem item in _mapChannels.Items)
        {
            if (item.Tag is not TikTokSource s)
            {
                continue;
            }

            var count = _settings.FacebookPages.Count(p => s.PageIds.Contains(p.PageId));
            item.SubItems[1].Text = count == 0 ? "chưa chọn" : count.ToString();
            item.SubItems[1].ForeColor = count == 0 ? Red : Green;
        }

        _mapChannels.Invalidate();
    }

    private void FillMapPages()
    {
        var source = SelectedMapChannel();
        _mapTitle.Text = source is null
            ? (_settings.TikTokSources.Count == 0 ? "CHƯA CÓ KÊNH TIKTOK — THÊM Ở TAB KÊNH TIKTOK" : "CHỌN MỘT KÊNH BÊN TRÁI")
            : $"FANPAGE NHẬN VIDEO CỦA {source.ToString().ToUpperInvariant()}";
        _fillingMap = true;
        _mapPages.BeginUpdate();
        _mapPages.Items.Clear();
        if (source is not null)
        {
            foreach (var fb in _settings.FacebookPages)
            {
                var item = new ListViewItem([fb.Name + (fb.Enabled ? "" : "  (fanpage đang không tick)"), fb.PageId])
                    { Tag = fb, Checked = source.PageIds.Contains(fb.PageId), UseItemStyleForSubItems = false };
                item.SubItems[0].ForeColor = fb.Enabled ? Ink : Muted;
                _mapPages.Items.Add(item);
            }
        }

        _mapPages.EndUpdate();
        _fillingMap = false;
        _mapPages.Enabled = source is not null;
    }

    private void SetMapAll(bool on)
    {
        if (SelectedMapChannel() is not { } source)
        {
            return;
        }

        source.PageIds = on ? _settings.FacebookPages.Select(p => p.PageId).ToList() : [];
        SaveSettings();
        FillMapPages();
        RefreshMappingCounts();
        RefreshChannels();
        RefreshPages();
        RefreshHome();
    }

    private void CopyMappingToAll()
    {
        if (SelectedMapChannel() is not { } source)
        {
            return;
        }

        if (MessageBox.Show($"Dùng đúng các fanpage của {source} cho tất cả {_settings.TikTokSources.Count} kênh TikTok?", "todoX ReupVideo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (var s in _settings.TikTokSources)
        {
            s.PageIds = source.PageIds.ToList();
        }

        SaveSettings();
        RefreshMappingCounts();
        RefreshChannels();
        RefreshPages();
        RefreshHome();
    }

    /// <summary>Channel names mapped to a fanpage (for the fanpage list).</summary>
    private string ChannelsOf(FacebookPage fb)
    {
        var names = _settings.TikTokSources.Where(s => s.PageIds.Contains(fb.PageId)).Select(s => "@" + s.Username).ToList();
        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    /// <summary>Fanpage names a channel posts to (for the channel list).</summary>
    private string PagesOf(TikTokSource s)
    {
        var names = _settings.FacebookPages.Where(p => s.PageIds.Contains(p.PageId)).Select(p => p.Name).ToList();
        return names.Count == 0 ? "chưa chọn fanpage" : string.Join(", ", names);
    }
}
