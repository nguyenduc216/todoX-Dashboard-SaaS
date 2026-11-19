using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace TodoX.ReupVideo.App;

/// <summary>
/// TodoX look (same palette as TodoX EditVideo): near-black ground, dark panels, TodoX yellow accents.
/// Sizes are written in 96-DPI pixels and go through <see cref="S"/> so 125%/150% screens keep the layout.
/// </summary>
internal static class Theme
{
    public static readonly Color Ground = ColorTranslator.FromHtml("#0B0D10");
    public static readonly Color Surface = ColorTranslator.FromHtml("#16181E");
    public static readonly Color PanelHover = ColorTranslator.FromHtml("#1E2129");
    public static readonly Color Border = ColorTranslator.FromHtml("#2C2F38");
    public static readonly Color Yellow = ColorTranslator.FromHtml("#FFBE2E");
    public static readonly Color Ink = Color.White;
    public static readonly Color Muted = ColorTranslator.FromHtml("#A9AFBD");
    public static readonly Color Green = ColorTranslator.FromHtml("#3DDC97");
    public static readonly Color Red = ColorTranslator.FromHtml("#FF6B6B");
    public static readonly Color Note = ColorTranslator.FromHtml("#F5DFA6");
    public static readonly Color Selected = ColorTranslator.FromHtml("#3A3220");

    public static readonly float Scale = ReadScale();

    public static int S(int px) => (int)Math.Round(px * Scale);

    public static Padding P(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    public static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    private static float ReadScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    public static Image? Logo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("todox-logo.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    public static Icon AppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }

    public static Label Section(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        ForeColor = Yellow,
        Font = F(9, FontStyle.Bold),
        AutoSize = true,
        Margin = P(0, 14, 0, 6)
    };

    public static Label Text(string text, Color? color = null, float size = 9.5f, int maxWidth = 760) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(S(maxWidth), 0),
        Font = F(size),
        ForeColor = color ?? Ink,
        Margin = P(0, 0, 0, 6)
    };

    public static Label Inline(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Margin = P(0, 9, 6, 0)
    };

    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        WrapContents = true,
        MaximumSize = new Size(S(800), 0),
        Margin = P(0, 0, 0, 4)
    };

    /// <summary>
    /// Page of stacked sections scrolled with the thin yellow TodoX bar (same as TodoX EditVideo); returns the stack
    /// to fill, <paramref name="host"/> is what goes into the window.
    /// </summary>
    public static FlowLayoutPanel Page(out Control host)
    {
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = P(24, 8, 24, 16),
            BackColor = Ground
        };
        var scroll = new ScrollHost(stack, Ground) { Dock = DockStyle.Fill };
        scroll.Resize += (_, _) => stack.MinimumSize = new Size(Math.Max(0, scroll.ClientSize.Width - S(10)), 0);
        host = scroll;
        return stack;
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

    /// <summary>Windows' dark scroll bar for lists and the log (their own bar cannot be painted yellow) — as in EditVideo.</summary>
    public static void DarkScrollBars(Control control)
    {
        void Apply() => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        if (control.IsHandleCreated)
        {
            Apply();
        }

        control.HandleCreated += (_, _) => Apply();
    }

    public static TextBox Input(int width, string placeholder = "", bool secret = false) => new()
    {
        Width = S(width),
        Font = F(10),
        BackColor = Surface,
        ForeColor = Ink,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = placeholder,
        UseSystemPasswordChar = secret,
        Margin = P(0, 4, 8, 4)
    };

    public static NumericUpDown Number(int min, int max, int value, int width = 70) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = S(width),
        BackColor = Surface,
        ForeColor = Ink,
        Font = F(10),
        Margin = P(0, 4, 8, 4)
    };

    public static CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = Ink,
        Margin = P(0, 6, 12, 4)
    };

    public static RadioButton Radio(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Ink,
        Margin = P(0, 4, 0, 2)
    };

    public static Button Button(string text, bool primary = false, bool big = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(0, S(big ? 42 : 34)),
            Padding = P(10, 2, 10, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Yellow : Surface,
            ForeColor = primary ? Ground : Ink,
            Font = F(big ? 11 : 9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            UseMnemonic = false,
            Margin = P(0, 2, 8, 4)
        };
        button.FlatAppearance.BorderColor = primary ? Yellow : Border;
        button.EnabledChanged += (_, _) => button.ForeColor = button.Enabled ? (primary ? Ground : Ink) : Muted;
        return button;
    }

    /// <summary>Dark list with a dark header (the Windows header is otherwise always light).</summary>
    public static ListView List(bool checkboxes = false)
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            CheckBoxes = checkboxes,
            BackColor = Surface,
            ForeColor = Ink,
            BorderStyle = BorderStyle.None,
            Font = F(9.5f),
            OwnerDraw = true,
            HideSelection = false,
            Width = S(780),
            Height = S(300),
            Margin = P(0, 4, 0, 6)
        };
        var headerFont = F(9, FontStyle.Bold);
        list.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Ground);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var line = new Pen(Border);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, headerFont, Rectangle.Inflate(e.Bounds, -S(4), 0), Yellow,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) => e.DrawDefault = checkboxes;
        list.DrawSubItem += (_, e) =>
        {
            if (checkboxes && e.ColumnIndex == 0)
            {
                e.DrawDefault = true; // keep the native tick box
                return;
            }

            var selected = e.Item!.Selected;
            using (var back = new SolidBrush(selected ? Selected : list.BackColor))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", list.Font, Rectangle.Inflate(e.Bounds, -S(6), 0),
                selected ? Color.White : e.SubItem?.ForeColor ?? e.Item.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        };
        list.SelectedIndexChanged += (_, _) => list.Invalidate();
        DarkScrollBars(list);
        return list;
    }

    /// <summary>Adds columns given in 96-DPI widths; the column marked fill (-1) takes the remaining width.</summary>
    public static Action Columns(ListView list, params (string Title, int Width)[] columns)
    {
        foreach (var (title, width) in columns)
        {
            list.Columns.Add(title, width < 0 ? S(200) : S(width));
        }

        var fill = Array.FindIndex(columns, c => c.Width < 0);
        if (fill < 0)
        {
            return () => { };
        }

        void Fit()
        {
            var others = list.Columns.Cast<ColumnHeader>().Where(c => c.Index != fill).Sum(c => c.Width);
            list.Columns[fill].Width = Math.Max(S(120), list.ClientSize.Width - others - 1);
            if (list.IsHandleCreated)
            {
                ShowScrollBar(list.Handle, 0, false); // SB_HORZ: columns fit, so no sideways bar
            }
        }

        list.Resize += (_, _) => Fit();
        list.HandleCreated += (_, _) => Fit();
        return Fit;
    }

    /// <summary>Row of toggle buttons switching between pages (used for the settings tabs).</summary>
    public static Control Tabs(params (string Title, Control Page)[] tabs)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Ground };
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Ground, Padding = P(24, 12, 24, 0), WrapContents = true };
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Ground };
        var buttons = new List<Button>();
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var b = Button(tabs[i].Title);
            b.Click += (_, _) => Select(index);
            buttons.Add(b);
            strip.Controls.Add(b);
            tabs[i].Page.Visible = false;
            body.Controls.Add(tabs[i].Page);
        }

        void Select(int index)
        {
            for (var i = 0; i < tabs.Length; i++)
            {
                tabs[i].Page.Visible = i == index;
                buttons[i].BackColor = i == index ? Yellow : Surface;
                buttons[i].ForeColor = i == index ? Ground : Ink;
                buttons[i].FlatAppearance.BorderColor = i == index ? Yellow : Border;
            }
        }

        host.Controls.Add(body);
        host.Controls.Add(strip);
        Select(0);
        host.Tag = (Action<int>)Select;
        return host;
    }
}
