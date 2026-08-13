namespace TodoX.Web.Models.Catalog;

public sealed record TodoXServiceEngineType(string Value, string Label);

public static class TodoXServiceEngineTypes
{
    public const string Timelapse = "timelapse";
    public const string RVideo = "rvideo";
    public const string RDance = "rdance";

    public static IReadOnlyList<TodoXServiceEngineType> All { get; } =
    [
        new(Timelapse, "Timelapse"),
        new(RVideo, "RVideo"),
        new(RDance, "RDance")
    ];

    public static bool IsValid(string? value)
        => All.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? value)
    {
        var match = All.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new InvalidOperationException($"Unsupported TodoX service engine_type: {value}");
        }

        return match.Value;
    }

    public static string LabelFor(string? value)
        => All.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase))?.Label
           ?? value
           ?? string.Empty;
}
