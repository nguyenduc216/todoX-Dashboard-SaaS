using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.App;

/// <summary>TodoX's dark look shared by the main window and the settings window.</summary>
internal static class Ui
{
    public static readonly Color Ground = MainWindow.Ground;
    public static readonly Color Panel = MainWindow.Panel;
    public static readonly Color Card = ColorTranslator.FromHtml("#13161C");
    public static readonly Color CardHover = ColorTranslator.FromHtml("#1B1F27");
    public static readonly Color Line = ColorTranslator.FromHtml("#2C2F38");
    public static readonly Color Bar = ColorTranslator.FromHtml("#101318");
    public static readonly Color Yellow = MainWindow.Yellow;
    public static readonly Color Ink = MainWindow.Ink;
    public static readonly Color Muted = MainWindow.Muted;
    public static readonly Color Green = ColorTranslator.FromHtml("#4ADE80");
    public static readonly Color Red = ColorTranslator.FromHtml("#F87171");

    public static Label Section(string title, int top = 18) => new()
    {
        Text = title.ToUpperInvariant(),
        ForeColor = Yellow,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, top, 0, 8)
    };

    public static Label Text(string text, float size = 10, Color? color = null, int width = 640, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        UseMnemonic = false,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Ink,
        Margin = new Padding(0, 0, 0, 6)
    };

    public static FlowLayoutPanel Stack(int width = 0) => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Ground,
        Margin = new Padding(0),
        MinimumSize = width > 0 ? new Size(width, 0) : Size.Empty
    };

    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        WrapContents = true,
        MaximumSize = new Size(660, 0),
        Margin = new Padding(0, 2, 0, 4)
    };

    public static Button Button(string text, bool primary = false) => Style(new Button { Text = text }, primary);

    public static Button Style(Button button, bool primary)
    {
        button.AutoSize = true;
        button.Height = 36;
        button.Padding = new Padding(12, 2, 12, 2);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Yellow : Panel;
        button.ForeColor = primary ? Ground : Ink;
        button.Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 4);
        button.FlatAppearance.BorderColor = primary ? Yellow : Line;
        button.FlatAppearance.MouseOverBackColor = primary ? ColorTranslator.FromHtml("#FFD166") : CardHover;
        return button;
    }

    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening a page or folder is a convenience.
        }
    }

    public static Image? Logo()
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("todox-logo.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public enum TileArt
{
    EditVideo,
    AiVideo,
    Key,
    Gear,
    Folder,
    Back
}

/// <summary>Line icons drawn with anti-aliasing (film strip, AI sparkle, key, gear, folder, back arrow).</summary>
public sealed class LineIcon : Control
{
    public LineIcon(TileArt art, int size)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Art = art;
        Size = new Size(size, size);
        Margin = new Padding(0);
        ForeColor = Ui.Yellow;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TileArt Art { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(Width, Height);
        var w = Math.Max(1.6f, s / 16f);
        using var pen = new Pen(ForeColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(ForeColor);
        float P(float f) => f * s;

        switch (Art)
        {
            case TileArt.EditVideo:
            {
                // Film frame with sprocket holes and a cut line through it.
                using (var frame = Ui.Rounded(new RectangleF(P(0.12f), P(0.2f), P(0.76f), P(0.6f)), P(0.06f)))
                {
                    g.DrawPath(pen, frame);
                }

                g.DrawLine(pen, P(0.3f), P(0.2f), P(0.3f), P(0.8f));
                g.DrawLine(pen, P(0.7f), P(0.2f), P(0.7f), P(0.8f));
                foreach (var y in new[] { 0.3f, 0.45f, 0.6f })
                {
                    g.FillRectangle(brush, P(0.17f), P(y), P(0.07f), P(0.06f));
                    g.FillRectangle(brush, P(0.76f), P(y), P(0.07f), P(0.06f));
                }

                // Play triangle in the middle frame.
                g.FillPolygon(brush, [new PointF(P(0.44f), P(0.38f)), new PointF(P(0.44f), P(0.62f)), new PointF(P(0.6f), P(0.5f))]);
                break;
            }
            case TileArt.AiVideo:
            {
                Sparkle(g, brush, P(0.42f), P(0.52f), P(0.3f));
                Sparkle(g, brush, P(0.76f), P(0.24f), P(0.13f));
                Sparkle(g, brush, P(0.78f), P(0.78f), P(0.09f));
                break;
            }
            case TileArt.Key:
            {
                g.DrawEllipse(pen, P(0.12f), P(0.3f), P(0.36f), P(0.36f));
                g.DrawLine(pen, P(0.48f), P(0.48f), P(0.88f), P(0.48f));
                g.DrawLine(pen, P(0.74f), P(0.48f), P(0.74f), P(0.64f));
                g.DrawLine(pen, P(0.86f), P(0.48f), P(0.86f), P(0.6f));
                break;
            }
            case TileArt.Gear:
            {
                var c = new PointF(P(0.5f), P(0.5f));
                for (var i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    g.DrawLine(pen, c.X + (float)Math.Cos(a) * P(0.26f), c.Y + (float)Math.Sin(a) * P(0.26f),
                        c.X + (float)Math.Cos(a) * P(0.4f), c.Y + (float)Math.Sin(a) * P(0.4f));
                }

                g.DrawEllipse(pen, P(0.24f), P(0.24f), P(0.52f), P(0.52f));
                g.DrawEllipse(pen, P(0.4f), P(0.4f), P(0.2f), P(0.2f));
                break;
            }
            case TileArt.Folder:
            {
                using var path = new GraphicsPath();
                path.AddLines([
                    new PointF(P(0.1f), P(0.28f)), new PointF(P(0.38f), P(0.28f)), new PointF(P(0.46f), P(0.36f)),
                    new PointF(P(0.9f), P(0.36f)), new PointF(P(0.9f), P(0.78f)), new PointF(P(0.1f), P(0.78f))
                ]);
                path.CloseFigure();
                g.DrawPath(pen, path);
                break;
            }
            case TileArt.Back:
                g.DrawLines(pen, [new PointF(P(0.55f), P(0.25f)), new PointF(P(0.3f), P(0.5f)), new PointF(P(0.55f), P(0.75f))]);
                g.DrawLine(pen, P(0.3f), P(0.5f), P(0.8f), P(0.5f));
                break;
        }
    }

    private static void Sparkle(Graphics g, Brush brush, float x, float y, float r)
    {
        var k = r * 0.22f;
        g.FillPolygon(brush, [
            new PointF(x, y - r), new PointF(x + k, y - k), new PointF(x + r, y), new PointF(x + k, y + k),
            new PointF(x, y + r), new PointF(x - k, y + k), new PointF(x - r, y), new PointF(x - k, y - k)
        ]);
    }
}
