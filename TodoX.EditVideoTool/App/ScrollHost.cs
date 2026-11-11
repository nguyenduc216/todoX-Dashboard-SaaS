using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// Holds the page and scrolls it with a thin, rounded TodoX-styled bar instead of the Windows scroll bar.
/// The bar only appears when the page is taller than the window; the mouse wheel scrolls the page anywhere
/// over it (except inside a control that scrolls itself, such as the log).
/// </summary>
public sealed class ScrollHost : Panel, IMessageFilter
{
    private const int WmMouseWheel = 0x020A;

    private readonly Control _content;
    private readonly ThinBar _bar;
    private int _offset;

    public ScrollHost(Control content, Color background)
    {
        _content = content;
        BackColor = background;
        DoubleBuffered = true;
        _content.Location = Point.Empty;
        Controls.Add(_content);
        _bar = new ThinBar(this) { Dock = DockStyle.Right, Width = 10, BackColor = background };
        Controls.Add(_bar);
        _bar.BringToFront();
        _content.SizeChanged += (_, _) => Clamp();
        _content.Layout += (_, _) => BeginInvokeClamp();
        Resize += (_, _) => Clamp();
        Application.AddMessageFilter(this);
        Disposed += (_, _) => Application.RemoveMessageFilter(this);
    }

    /// <summary>Controls under the cursor that keep the wheel for themselves.</summary>
    public List<Control> WheelOwners { get; } = [];

    internal int Overflow => Math.Max(0, _content.Height - ClientSize.Height);

    internal int Offset => _offset;

    internal int ViewHeight => ClientSize.Height;

    internal int ContentHeight => _content.Height;

    internal void ScrollTo(int offset)
    {
        var clamped = Math.Clamp(offset, 0, Overflow);
        if (clamped == _offset)
        {
            return;
        }

        _offset = clamped;
        _content.Top = -_offset;
        _bar.Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Clamp();
    }

    private void Clamp()
    {
        // Layout already runs while the constructor adds the page, before the bar exists.
        if (_bar is null)
        {
            return;
        }

        _bar.Visible = Overflow > 0;
        _offset = Math.Clamp(_offset, 0, Overflow);
        _content.Top = -_offset;
        _bar.Invalidate();
    }

    private void BeginInvokeClamp()
    {
        if (IsHandleCreated)
        {
            BeginInvoke(Clamp);
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseWheel || !Visible || Overflow == 0 || FindForm() is not { } form || Form.ActiveForm != form)
        {
            return false;
        }

        var cursor = Cursor.Position;
        if (!RectangleToScreen(ClientRectangle).Contains(cursor)
            || WheelOwners.Any(c => c.Visible && c.RectangleToScreen(c.ClientRectangle).Contains(cursor))
            || Control.FromHandle(m.HWnd) is ComboBox { DroppedDown: true })
        {
            return false;
        }

        var delta = (short)((long)m.WParam >> 16);
        ScrollTo(_offset - delta / 120 * 60);
        return true;
    }

    /// <summary>The thin bar: a rounded thumb, grey at rest and TodoX yellow while hovered or dragged.</summary>
    private sealed class ThinBar : Control
    {
        private static readonly Color Rest = ColorTranslator.FromHtml("#4A505E");
        private static readonly Color Active = ColorTranslator.FromHtml("#FFBE2E");

        private readonly ScrollHost _host;
        private bool _hover;
        private int? _dragFrom;
        private int _dragOffset;

        public ThinBar(ScrollHost host)
        {
            _host = host;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        private Rectangle Thumb()
        {
            var track = Height - 8;
            var length = Math.Max(40, (int)(track * (double)_host.ViewHeight / Math.Max(1, _host.ContentHeight)));
            var top = 4 + (int)((track - length) * (_host.Overflow == 0 ? 0 : (double)_host.Offset / _host.Overflow));
            return new Rectangle(2, top, Width - 4, length);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var thumb = Thumb();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_hover || _dragFrom is not null ? Active : Rest);
            using var path = new GraphicsPath();
            var r = thumb.Width;
            path.AddArc(thumb.X, thumb.Y, r, r, 180, 180);
            path.AddArc(thumb.X, thumb.Bottom - r, r, r, 0, 180);
            path.CloseFigure();
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var thumb = Thumb();
            if (!thumb.Contains(e.Location))
            {
                // Click on the track: jump a page towards the click.
                _host.ScrollTo(_host.Offset + (e.Y < thumb.Top ? -1 : 1) * _host.ViewHeight * 9 / 10);
                return;
            }

            _dragFrom = e.Y;
            _dragOffset = _host.Offset;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragFrom is not { } from)
            {
                return;
            }

            var track = Height - 8 - Thumb().Height;
            if (track > 0)
            {
                _host.ScrollTo(_dragOffset + (int)((e.Y - from) * (double)_host.Overflow / track));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragFrom = null;
            Capture = false;
            Invalidate();
        }
    }
}
