namespace TodoX.Web.Services.AiCharacters;

public sealed record SelectOption(string Label, string Value);

public static class CharacterPresetOptions
{
    public const string NotSpecified = "not_specified";

    public static readonly IReadOnlyList<SelectOption> StylePresets =
    [
        new("Không chọn", NotSpecified),
        new("Chibi", "3d_chibi"),
        new("Realistic", "realistic"),
        new("Anime", "anime"),
        new("Cartoon", "cartoon"),
        new("Corporate Mascot", "corporate_mascot"),
        new("KOC AI", "koc_ai")
    ];

    public static readonly IReadOnlyList<SelectOption> GenderOptions =
    [
        new("Không chọn", NotSpecified),
        new("Nam", "male"),
        new("Nữ", "female"),
        new("Trung tính", "neutral")
    ];

    public static readonly IReadOnlyList<SelectOption> AspectRatios =
    [
        new("Vuông 1:1", "1:1"),
        new("Dọc 9:16", "9:16"),
        new("Ngang 16:9", "16:9"),
        new("Dọc 4:5", "4:5"),
        new("Dọc 3:4", "3:4")
    ];

    public static string? NormalizeOptionalPreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().Equals(NotSpecified, StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
    }

    public static bool IsAllowedOptional(string? value, IReadOnlyList<SelectOption> options)
    {
        var normalized = NormalizeOptionalPreset(value);
        return normalized is null || options.Any(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string Label(IReadOnlyList<SelectOption> options, string? value)
        => options.FirstOrDefault(x => x.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Label
           ?? options.First().Label;
}
