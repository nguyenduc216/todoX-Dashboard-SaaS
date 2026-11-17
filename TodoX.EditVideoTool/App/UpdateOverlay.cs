using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.App;

/// <summary>
/// Covers the whole window while TodoX EditVideo updates itself: a turning ring, what is happening, the percent
/// and a progress bar, so it is obvious the app is busy and nothing else can be clicked meanwhile.
/// </summary>
public sealed partial class UpdateOverlay : Panel
{
    private readonly Spinner _spinner = new() { Size = new Size(72, 72) };
    private readonly Label _title = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 18), UseMnemonic = false, BackColor = Color.Transparent };
    private readonly Label _detail = new() { AutoSize = false, Height = 26, ForeColor = MainWindow.Muted, Font = new Font("Segoe UI", 11), TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false, BackColor = Color.Transparent };
    private readonly Bar _bar = new() { Height = 10 };
    private readonly Label _hint = new() { AutoSize = false, Height = 24, ForeColor = MainWindow.Muted, Font = new Font("Segoe UI", 9.5f), TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false, BackColor = Color.Transparent,
        Text = "Đừng tắt máy hay đóng cửa sổ trong lúc cập nhật." };

    public UpdateOverlay()
    {
        // Not docked: it lies over the whole window, top bar and banner included.
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        BackColor = MainWindow.Ground;
        Visible = false;
        Controls.AddRange([_spinner, _title, _detail, _bar, _hint]);
        Resize += (_, _) => Arrange();
    }

    public void Show(string title)
    {
        _title.Text = title;
        _detail.Text = "Đang chuẩn bị…";
        _bar.Percent = null;
        if (Parent is { } host)
        {
            Bounds = host.ClientRectangle;
        }

        Visible = true;
        BringToFront();
        _spinner.Start();
        Arrange();
    }

    /// <summary>A progress message from the updater, e.g. "Tải TodoX EditVideo: 42%".</summary>
    public void Report(string message)
    {
        _detail.Text = message;
        var m = PercentRegex().Match(message);
        _bar.Percent = m.Success && double.TryParse(m.Groups[1].Value, out var p) ? Math.Clamp(p, 0, 100) : _bar.Percent;
    }

    public new void Hide()
    {
        _spinner.Stop();
        Visible = false;
    }

    private void Arrange()
    {
        var w = Math.Min(560, ClientSize.Width - 80);
        var top = Math.Max(20, ClientSize.Height / 2 - 120);
        _spinner.Location = new Point((ClientSize.Width - _spinner.Width) / 2, top);
        _title.Location = new Point((ClientSize.Width - _title.PreferredWidth) / 2, _spinner.Bottom + 22);
        _detail.SetBounds((ClientSize.Width - w) / 2, _title.Bottom + 10, w, 26);
        _bar.SetBounds((ClientSize.Width - w) / 2, _detail.Bottom + 12, w, 10);
        _hint.SetBounds((ClientSize.Width - w) / 2, _bar.Bottom + 14, w, 24);
    }

    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d+)?)\s*%")]
    private static partial Regex PercentRegex();

    /// <summary>A turning yellow arc (TodoX colour) drawn with anti-aliasing.</summary>
    private sealed class Spinner : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
        private float _angle;

        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _timer.Tick += (_, _) =>
            {
                _angle = (_angle + 9) % 360;
                Invalidate();
            };
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(5, 5, Width - 10, Height - 10);
            using var track = new Pen(MainWindow.Panel, 7);
            g.DrawEllipse(track, r);
            using var arc = new Pen(MainWindow.Yellow, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, r, _angle, 100);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Progress bar; with no percent yet a short block slides back and forth.</summary>
    private sealed class Bar : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
        private float _slide;
        private double? _percent;

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = MainWindow.Panel;
            _timer.Tick += (_, _) =>
            {
                _slide = (_slide + 0.012f) % 1.3f;
                Invalidate();
            };
            _timer.Start();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double? Percent
        {
            get => _percent;
            set
            {
                _percent = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(MainWindow.Panel);
            using var fill = new SolidBrush(MainWindow.Yellow);
            if (_percent is { } p)
            {
                e.Graphics.FillRectangle(fill, 0, 0, (float)(Width * p / 100), Height);
            }
            else
            {
                var w = Width * 0.25f;
                e.Graphics.FillRectangle(fill, (_slide - 0.3f) * Width, 0, w, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
