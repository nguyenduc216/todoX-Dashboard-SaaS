using System.Text.RegularExpressions;

namespace TodoX.Web.Services.Media;

/// <summary>
/// Uploaded videos are shrunk to a 720p web copy for playing on the sites ("videos" folder); the untouched file the
/// admin uploaded is kept beside it in "originals" under the same 32-hex name, and every download hands that one out.
/// No database column: the original is found from the web copy's URL. Videos uploaded before this have no original
/// (it was deleted after transcoding), so their download falls back to the web copy.
/// </summary>
public static partial class MediaOriginals
{
    public const string Folder = "originals";
    private static readonly string[] Extensions = [".mp4", ".webm"];

    [GeneratedRegex(@"^(?<dir>.*)/videos/(?<name>[a-f0-9]{32})\.(mp4|webm)$", RegexOptions.IgnoreCase)]
    private static partial Regex WebCopy();

    /// <summary>".../videos/{id}.mp4" → ".../originals/{id}.mp4" and ".webm" (where the original may be).</summary>
    public static IReadOnlyList<string> Candidates(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return [];
        var match = WebCopy().Match(url.Split('?', '#')[0]);
        if (!match.Success) return [];
        var dir = match.Groups["dir"].Value;
        var name = match.Groups["name"].Value;
        return Extensions.Select(ext => $"{dir}/{Folder}/{name}{ext}").ToList();
    }

    /// <summary>Download file name with the extension of the file actually sent (an original may be .webm).</summary>
    public static string WithExtensionOf(string fileName, string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? fileName : Path.ChangeExtension(fileName, ext);
    }
}
