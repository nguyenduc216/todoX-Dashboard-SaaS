using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using static TodoX.ReupVideo.App.Theme;

namespace TodoX.ReupVideo.App;

/// <summary>
/// Trang chính: a search box above each column of the link list (date, channel, text, state, fanpages).
/// Matching ignores case and Vietnamese accents ("tat vo" finds "Tất Vớ").
/// </summary>
public sealed partial class MainForm
{
    private readonly Panel _filterBar = new() { Dock = DockStyle.Top, BackColor = Ground };
    private readonly Dictionary<int, TextBox> _filters = [];
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 300 };
    private readonly LinkLabel _clearFilters = new() { Text = "✕ Xoá tìm kiếm", AutoSize = true, LinkColor = Yellow, ActiveLinkColor = Ink, Font = F(9.5f), Margin = P(8, 13, 0, 0), Visible = false };

    /// <summary>The highlighted link's full TikTok text (above the per-fanpage list).</summary>
    private readonly Label _linkText = new() { AutoSize = false, ForeColor = Ink, Font = F(9.5f), UseMnemonic = false, AutoEllipsis = true };

    private void BuildFilterBar()
    {
        (int Column, string Hint)[] boxes =
        [
            (1, "Tìm ngày…"),
            (2, "Tìm kênh…"),
            (3, "🔍 Tìm trong nội dung, hashtag…"),
            (4, "Tìm trạng thái…"),
            (5, "x/y"),
            (6, "Tìm fanpage…")
        ];
        foreach (var (column, hint) in boxes)
        {
            var box = Theme.Input(100, hint);
            box.Name = "filter" + column;
            box.Font = F(9.5f);
            box.Margin = Padding.Empty;
            box.TextChanged += (_, _) =>
            {
                _filterTimer.Stop();
                _filterTimer.Start();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    box.Clear();
                    e.SuppressKeyPress = true;
                }
            };
            _filters[column] = box;
            _filterBar.Controls.Add(box);
        }

        _filterBar.Height = _filters[3].PreferredHeight + S(6);
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            _clearFilters.Visible = _filters.Values.Any(b => b.Text.Trim().Length > 0);
            RefreshHome();
        };
        _clearFilters.LinkClicked += (_, _) =>
        {
            foreach (var b in _filters.Values)
            {
                b.Clear();
            }
        };
        _links.ColumnWidthChanged += (_, _) => LayoutFilters();
        _links.Resize += (_, _) => LayoutFilters();
        _filterBar.Resize += (_, _) => LayoutFilters();
    }

    /// <summary>Puts each search box exactly over its column.</summary>
    private void LayoutFilters()
    {
        if (_links.Columns.Count == 0)
        {
            return;
        }

        var x = SystemInformation.BorderSize.Width; // the bar and the list share the same left edge
        for (var i = 0; i < _links.Columns.Count; i++)
        {
            var width = _links.Columns[i].Width;
            if (_filters.TryGetValue(i, out var box))
            {
                box.SetBounds(x + S(2), S(2), Math.Max(S(30), width - S(4)), box.Height);
            }

            x += width;
        }
    }

    private bool MatchesFilters(ListViewItem row)
    {
        foreach (var (column, box) in _filters)
        {
            var query = box.Text.Trim();
            if (query.Length == 0)
            {
                continue;
            }

            // Text column: search the whole TikTok text, not only what fits in the cell.
            var cell = column == 3 ? row.ToolTipText : row.SubItems[column].Text;
            var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var folded = Fold(cell);
            if (!words.All(folded.Contains))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lower case without Vietnamese accents: "Tất Vớ Đẹp" → "tat vo dep".</summary>
    internal static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
