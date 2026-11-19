using System.Text.Json;

namespace TodoX.Landing.Services;

/// <summary>Latest build of a desktop app, as listed in its manifest.json.</summary>
public sealed record AppRelease(string App, string Version, DateTimeOffset? PublishedAt, string FileName, long Size, string FilePath, string? Sha256 = null);

/// <summary>
/// Desktop app downloads served by the landing site ("Tải app"). Each app has a folder on the server,
/// <c>{root}/{app}</c>, holding the builds and a manifest.json written last by the release script
/// (tools/reupvideo/Build-ReupVideoRelease.ps1). Only the file the manifest names can be downloaded, so nothing
/// else in the folder is reachable. A missing folder or broken manifest just means "no download yet".
/// </summary>
public static class AppDownloads
{
    /// <summary>Server folder shared with the EditVideo downloads (readable by the IIS app pools).</summary>
    public const string DefaultRoot = "C:/TodoXDeploy/downloads";

    /// <summary>Apps the landing site offers. Lower case, also the URL segment: /downloads/{app}/latest.</summary>
    public static readonly IReadOnlySet<string> Apps = new HashSet<string>(StringComparer.Ordinal) { "reupvideo" };

    public static AppRelease? Latest(string root, string app)
    {
        if (!Apps.Contains(app))
        {
            return null;
        }

        try
        {
            var folder = Path.GetFullPath(Path.Combine(root, app));
            var manifest = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifest))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var rootElement = doc.RootElement;
            var version = Str(rootElement, "version");
            if (version is null || !rootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(Str(rootElement, "publishedAt"), out var at) ? at : null;
            foreach (var file in files.EnumerateArray())
            {
                var name = Str(file, "name");
                if (Str(file, "kind") != "app" || !IsSafeFileName(name))
                {
                    continue;
                }

                var path = Path.Combine(folder, name!);
                if (!File.Exists(path))
                {
                    continue;
                }

                return new AppRelease(app, version, published, name!, new FileInfo(path).Length, path, Str(file, "sha256"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // No readable release: the page keeps its text and the button explains the download is not ready.
        }

        return null;
    }

    /// <summary>A plain "name.exe" — no folders, no "..", nothing hidden.</summary>
    public static bool IsSafeFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 120
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith('.')
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
