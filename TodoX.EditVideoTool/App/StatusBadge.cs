using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.App;

public enum BadgeKind
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped,
    Number,
    Bullet,
    Chevron
}

/// <summary>
/// A small vector icon drawn with anti-aliasing (status circles, step numbers, spinner), so the setup does not depend
/// on emoji or symbol fonts that look different on each machine.
/// </summary>
public sealed class StatusBadge : Control
{
    private BadgeKind _kind;
    private int _number;
    private Color _accent = Color.FromArgb(0xF5, 0xB7, 0x31);
    private float _angle;

    public StatusBadge(BadgeKind kind, int size)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        _kind = kind;
        Size = new Size(size, size);
        Margin = new Padding(0);
    }

    public static readonly Color Green = ColorTranslator.FromHtml("#22C55E");
    public static readonly Color Red = ColorTranslator.FromHtml("#EF4444");
    public static readonly Color Grey = ColorTranslator.FromHtml("#6B7280");

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BadgeKind Kind
    {
        get => _kind;
        set
        {
            if (_kind != value)
            {
                _kind = value;
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Number
    {
        get => _number;
        set
        {
            _number = value;
            Invalidate();
        }
    }

    /// <summary>Colour of the running arc, the current step number and bullets.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Invalidate();
        }
    }

    /// <summary>Filled number circle (the step on screen) instead of an outline.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Filled { get; set; }

    /// <summary>Turns the running arc; called by the owner's timer.</summary>
    public void Advance()
    {
        if (_kind == BadgeKind.Running)
        {
            _angle = (_angle + 30) % 360;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = Math.Min(Width, Height);
        var stroke = Math.Max(1.5f, s / 11f);
        var box = new RectangleF((Width - s) / 2 + stroke / 2, (Height - s) / 2 + stroke / 2, s - stroke - 1, s - stroke - 1);

        switch (_kind)
        {
            case BadgeKind.Done:
                Disc(g, box, Green);
                Check(g, box, Color.White, stroke * 1.05f);
                break;
            case BadgeKind.Failed:
                Disc(g, box, Red);
                Cross(g, box, Color.White, stroke * 1.05f);
                break;
            case BadgeKind.Skipped:
                Ring(g, box, Grey, stroke);
                using (var pen = RoundPen(Grey, stroke))
                {
                    g.DrawLine(pen, box.Left + box.Width * 0.3f, box.Top + box.Height / 2, box.Right - box.Width * 0.3f, box.Top + box.Height / 2);
                }

                break;
            case BadgeKind.Pending:
                Ring(g, box, Grey, stroke);
                break;
            case BadgeKind.Running:
                Ring(g, box, Color.FromArgb(60, _accent), stroke);
                using (var pen = RoundPen(_accent, stroke))
                {
                    g.DrawArc(pen, box, _angle - 90, 100);
                }

                break;
            case BadgeKind.Number:
                if (Filled)
                {
                    Disc(g, box, _accent);
                }
                else
                {
                    Ring(g, box, ForeColor, stroke * 0.8f);
                }

                using (var font = new Font("Segoe UI", s * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Filled ? Color.FromArgb(0x11, 0x13, 0x18) : ForeColor))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(_number.ToString(), font, brush, new RectangleF(box.X, box.Y + s * 0.02f, box.Width, box.Height), format);
                }

                break;
            case BadgeKind.Bullet:
                Disc(g, new RectangleF(box.X + box.Width * 0.3f, box.Y + box.Height * 0.3f, box.Width * 0.4f, box.Height * 0.4f), _accent);
                break;
            case BadgeKind.Chevron:
                using (var pen = RoundPen(ForeColor, stroke * 0.9f))
                {
                    g.DrawLines(pen, [
                        new PointF(box.Left + box.Width * 0.38f, box.Top + box.Height * 0.25f),
                        new PointF(box.Left + box.Width * 0.62f, box.Top + box.Height * 0.5f),
                        new PointF(box.Left + box.Width * 0.38f, box.Top + box.Height * 0.75f)
                    ]);
                }

                break;
        }
    }

    private static Pen RoundPen(Color color, float width) => new(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    private static void Disc(Graphics g, RectangleF box, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, box);
    }

    private static void Ring(Graphics g, RectangleF box, Color color, float width)
    {
        using var pen = new Pen(color, width);
        g.DrawEllipse(pen, box);
    }

    private static void Check(Graphics g, RectangleF box, Color color, float width)
    {
        using var pen = RoundPen(color, width);
        g.DrawLines(pen, [
            new PointF(box.Left + box.Width * 0.28f, box.Top + box.Height * 0.52f),
            new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.67f),
            new PointF(box.Left + box.Width * 0.73f, box.Top + box.Height * 0.36f)
        ]);
    }

    private static void Cross(Graphics g, RectangleF box, Color color, float width)
    {
        using var pen = RoundPen(color, width);
        float a = 0.33f, b = 0.67f;
        g.DrawLine(pen, box.Left + box.Width * a, box.Top + box.Height * a, box.Left + box.Width * b, box.Top + box.Height * b);
        g.DrawLine(pen, box.Left + box.Width * b, box.Top + box.Height * a, box.Left + box.Width * a, box.Top + box.Height * b);
    }
}
