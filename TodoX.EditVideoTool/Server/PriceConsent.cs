using System.Text.Json;
using System.Windows.Forms;

namespace TodoX.EditVideoTool.Server;

/// <summary>A price TodoX asks the person to accept before a paid command (from the server, never stored here).</summary>
public sealed record PriceQuote(string QuoteId, string Kind, string Summary, decimal Points, decimal Vnd, decimal Balance)
{
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
        "Bấm Yes để đồng ý trừ điểm và bắt đầu tạo. Bấm No để hủy — không trừ điểm.";
}

/// <summary>Asks the person (not the AI assistant) to accept a price.</summary>
public interface IPriceConsent
{
    /// <summary>How the agreement was given, written to TodoX's log (e.g. "desktop_dialog").</summary>
    string Method { get; }

    Task<bool> AskAsync(PriceQuote quote, CancellationToken ct);
}

/// <summary>
/// A Windows confirmation window on top of everything, "No" by default. It runs on its own UI thread because
/// the MCP server has none; the AI assistant cannot click it.
/// </summary>
public sealed class DesktopPriceConsent : IPriceConsent
{
    public string Method => "desktop_dialog";

    public Task<bool> AskAsync(PriceQuote quote, CancellationToken ct)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var answer = MessageBox.Show(quote.Text, "TodoX EditVideo — Xác nhận trừ điểm", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
                done.TrySetResult(answer == DialogResult.Yes);
            }
            catch (Exception)
            {
                done.TrySetResult(false); // no desktop to ask on: never agree on the person's behalf
            }
        })
        {
            IsBackground = true,
            Name = "todox-price-consent"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ct.Register(() => done.TrySetResult(false));
        return done.Task;
    }
}
