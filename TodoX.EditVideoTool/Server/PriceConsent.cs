using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.Server;

/// <summary>A price TodoX asks the person to accept before a paid command (from the server, never stored here).</summary>
public sealed record PriceQuote(string QuoteId, string Kind, string Summary, decimal Points, decimal Vnd, decimal Balance)
{
    /// <summary>The project this command works in, when known: the window then offers "allow the whole project".</summary>
    public string? Project { get; init; }

    public static PriceQuote? From(JsonElement? detail)
    {
        if (detail is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("quoteId", out var id) || id.GetString() is not { Length: > 0 } quoteId)
        {
            return null;
        }

        static decimal Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        return new PriceQuote(quoteId, Str(d, "kind"), Str(d, "summary"), Num(d, "points"), Num(d, "vnd"), Num(d, "balance"));
    }

    public string Text =>
        $"{Summary}\n\nGiá TodoX: {Points:0.##} điểm (≈ {Vnd:#,0} đ)\nSố dư hiện có: {Balance:0.##} điểm\n\n" +
        "Bấm \"Đồng ý trừ điểm\" để bắt đầu tạo. Bấm \"Hủy\" thì không tạo và không trừ điểm.";
}

/// <summary>What happened in the confirmation window.</summary>
public enum ConsentAnswer
{
    Accepted,
    /// <summary>Accepted, and every later paid command of the same project runs without asking.</summary>
    AcceptedForProject,
    Declined,
    /// <summary>The window was shown but nobody answered in time.</summary>
    NoAnswer,
    /// <summary>The window could not be shown on this computer.</summary>
    Unavailable
}

/// <summary>Asks the person (not the AI assistant) to accept a price.</summary>
public interface IPriceConsent
{
    /// <summary>How the agreement was given, written to TodoX's log (e.g. "desktop_dialog").</summary>
    string Method { get; }

    Task<ConsentAnswer> AskAsync(PriceQuote quote, CancellationToken ct);
}

/// <summary>
/// TodoX's own confirmation window: always on top, flashing in the taskbar, "Hủy" by default, closed as "no answer"
/// after 5 minutes. It runs on its own UI thread because the MCP server has none; the AI assistant cannot click it.
/// </summary>
public sealed class DesktopPriceConsent : IPriceConsent
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    public string Method => "desktop_dialog";

    public Task<ConsentAnswer> AskAsync(PriceQuote quote, CancellationToken ct)
    {
        var done = new TaskCompletionSource<ConsentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new ConsentForm(quote);
                using var timer = new System.Windows.Forms.Timer { Interval = (int)Wait.TotalMilliseconds };
                timer.Tick += (_, _) =>
                {
                    form.Answer = ConsentAnswer.NoAnswer;
                    form.Close();
                };
                using var cancel = ct.Register(() => form.BeginInvoke(() => form.Close()));
                timer.Start();
                form.ShowDialog();
                done.TrySetResult(form.Answer);
            }
            catch (Exception ex)
            {
                Log("price_consent_unavailable " + ex.GetType().Name + ": " + ex.Message);
                done.TrySetResult(ConsentAnswer.Unavailable); // never agree on the person's behalf
            }
        })
        {
            IsBackground = true,
            Name = "todox-price-consent"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>The window itself, for the app's screenshot check.</summary>
    internal static Form PreviewForm(PriceQuote quote) => new ConsentForm(quote);

    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoX", "EditVideoTool", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private static readonly System.Globalization.CultureInfo Vi = new("vi-VN");

    private sealed class ConsentForm : Form
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ConsentAnswer Answer { get; set; } = ConsentAnswer.Declined;

        public ConsentForm(PriceQuote quote)
        {
            Text = "TodoX EditVideo — Xác nhận trừ điểm";
            Icon = WindowsNotifier.AppIcon();
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ColorTranslator.FromHtml("#0B0D10");
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10.5f);
            ClientSize = new Size(520, quote.Project is null ? 320 : 396);

            var title = new Label { Text = "Xác nhận trừ điểm TodoX", Font = new Font("Segoe UI Semibold", 15), AutoSize = true, Location = new Point(24, 18), ForeColor = Color.White };
            var summary = new Label { Text = quote.Summary, AutoSize = false, Location = new Point(24, 58), Size = new Size(472, 48), ForeColor = ColorTranslator.FromHtml("#A9AFBD"), UseMnemonic = false };
            var price = new Label
            {
                Text = $"{quote.Points.ToString("0.##", Vi)} điểm  (≈ {quote.Vnd.ToString("#,0", Vi)} đ)",
                Font = new Font("Segoe UI Semibold", 20), AutoSize = true, Location = new Point(22, 110), ForeColor = ColorTranslator.FromHtml("#FFBE2E")
            };
            var balance = new Label
            {
                Text = $"Số dư hiện có: {quote.Balance.ToString("#,0.##", Vi)} điểm. Bấm Hủy thì không tạo và không trừ điểm.",
                AutoSize = false, Location = new Point(24, 160), Size = new Size(472, 60), ForeColor = ColorTranslator.FromHtml("#A9AFBD"), UseMnemonic = false
            };
            var whole = new CheckBox { AutoSize = true, Location = new Point(24, 224), Visible = quote.Project is not null };
            var wholeText = new Label
            {
                Text = $"Cho phép mọi lệnh tốn điểm của dự án \"{quote.Project}\" không hỏi lại (chỉ báo số điểm trừ)",
                AutoSize = true, MaximumSize = new Size(440, 0), Location = new Point(48, 222), ForeColor = Color.White, UseMnemonic = false,
                Cursor = Cursors.Hand, Visible = quote.Project is not null
            };
            wholeText.Click += (_, _) => whole.Checked = !whole.Checked;
            var buttonsTop = quote.Project is null ? 246 : 322;
            var yes = new Button
            {
                Text = "Đồng ý trừ điểm", Size = new Size(200, 44), Location = new Point(24, buttonsTop), FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#FFBE2E"), ForeColor = Color.Black, Font = new Font("Segoe UI Semibold", 11)
            };
            yes.FlatAppearance.BorderSize = 0;
            yes.Click += (_, _) =>
            {
                Answer = whole.Checked ? ConsentAnswer.AcceptedForProject : ConsentAnswer.Accepted;
                Close();
            };
            var no = new Button
            {
                Text = "Hủy", Size = new Size(140, 44), Location = new Point(236, buttonsTop), FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#16181E"), ForeColor = Color.White
            };
            no.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#2C2F38");
            no.Click += (_, _) =>
            {
                Answer = ConsentAnswer.Declined;
                Close();
            };
            Controls.AddRange([title, summary, price, balance, whole, wholeText, yes, no]);
            if (quote.Project is not null)
            {
                // The tick's text wraps by itself (DPI, long project names); the buttons go right under it.
                Load += (_, _) =>
                {
                    yes.Top = no.Top = Math.Max(whole.Bottom, wholeText.Bottom) + 16;
                    ClientSize = new Size(ClientSize.Width, yes.Bottom + 20);
                };
            }
            CancelButton = no;
            Shown += (_, _) =>
            {
                no.Focus(); // Enter does not agree by accident
                Activate();
                Flash(Handle);
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Hwnd;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        private static void Flash(IntPtr hwnd)
        {
            var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Hwnd = hwnd, Flags = 3 | 12, Count = 0, Timeout = 0 }; // all + until foreground
            FlashWindowEx(ref info);
        }
    }
}
