namespace TodoX.EditVideo.Planning;

/// <summary>
/// Look presets served to the tool. Adding or tuning a style here changes every customer's next render
/// without shipping a new exe.
/// </summary>
public static class StyleCatalog
{
    private const string Font = "\"Segoe UI\", \"Helvetica Neue\", Arial, sans-serif";

    public static readonly IReadOnlyList<PlanStyle> All =
    [
        new("todox", "TodoX", "#FFBE2E", "#0B0D10", "#FFFFFF", Font, "karaoke", "mix", 0.08),
        new("clean", "Sạch – tối giản", "#FFFFFF", "#111317", "#FFFFFF", Font, "plain", "fade", 0.04),
        new("energetic", "Năng động", "#FF4D6D", "#120A14", "#FFFFFF", Font, "pop", "slide", 0.12),
        new("real-estate", "Bất động sản", "#D9B26A", "#14120E", "#FFFFFF", Font, "box", "fade", 0.06),
        new("sales", "Bán hàng", "#FFBE2E", "#0B0D10", "#FFFFFF", Font, "pop", "mix", 0.1)
    ];

    public static PlanStyle Resolve(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
