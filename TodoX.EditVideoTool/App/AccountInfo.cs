using System.Text.Json;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.App;

/// <summary>The customer's package, its end date, points and storage, as shown in the window and Settings.</summary>
public sealed record AccountInfo(string? Package, DateTimeOffset? Ends, decimal Points, long UsedBytes, long LimitBytes, string? CustomerName = null)
{
    public static async Task<AccountInfo?> LoadAsync(TodoXServerClient server, CancellationToken ct)
    {
        var result = await server.AccountAsync(ct);
        if (!result.Ok)
        {
            return null;
        }

        var d = result.Value;
        DateTimeOffset? ends = d.TryGetProperty("packageEnds", out var e) && e.ValueKind == JsonValueKind.String && e.TryGetDateTimeOffset(out var at) ? at : null;
        var storage = d.TryGetProperty("storage", out var s) ? s : default;
        return new AccountInfo(
            d.TryGetProperty("package", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            ends,
            d.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Number ? pts.GetDecimal() : 0,
            storage.ValueKind == JsonValueKind.Object && storage.TryGetProperty("usedBytes", out var u) ? u.GetInt64() : 0,
            storage.ValueKind == JsonValueKind.Object && storage.TryGetProperty("limitBytes", out var l) ? l.GetInt64() : 0,
            d.TryGetProperty("customerName", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()!.Trim() : null);
    }

    public string PointsText => $"{Points:N0} điểm".Replace(',', '.');

    public string? DaysLeftText => Ends is { } end
        ? $"còn {Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.Now).TotalDays))} ngày"
        : null;

    public string StorageText => $"{StorageUploader.Size(UsedBytes)} / {StorageUploader.Size(LimitBytes)}";
}
