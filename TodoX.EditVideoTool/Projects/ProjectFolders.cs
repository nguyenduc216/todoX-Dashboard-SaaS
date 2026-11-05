using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TodoX.EditVideoTool.Projects;

public sealed record ProjectFolder(string Name, string Path, int Images, int Videos, int Audio, int Other, DateTime? LastChanged);

/// <summary>
/// The customer's resource folder: one sub-folder per video project ("Dự án An Đông") holding the photos, clips,
/// logos and music they add. Finished videos go to an "output" folder inside the project.
/// </summary>
public sealed class ProjectFolders
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".bmp", ".gif", ".svg" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".mts", ".3gp" };
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac" };

    private readonly AgentPaths _paths;

    public ProjectFolders(AgentPaths paths)
    {
        _paths = paths;
    }

    /// <summary>The resource folder, created if missing.</summary>
    public string Root
    {
        get
        {
            var root = UserSettings.Load(_paths).EffectiveResourceRoot;
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public IReadOnlyList<ProjectFolder> List() =>
        Directory.EnumerateDirectories(Root)
            .Where(d => !System.IO.Path.GetFileName(d).StartsWith('.'))
            .Select(Describe)
            .OrderByDescending(p => p.LastChanged)
            .ToList();

    /// <summary>
    /// Finds the project folder by name, ignoring case and Vietnamese accents ("an dong" finds "An Đông");
    /// creates it when <paramref name="create"/> is set and nothing matches.
    /// </summary>
    public (ProjectFolder? Folder, bool Created) Find(string project, bool create)
    {
        var wanted = Key(project);
        if (wanted.Length == 0)
        {
            return (null, false);
        }

        var dirs = Directory.EnumerateDirectories(Root).ToList();
        var match = dirs.FirstOrDefault(d => Key(System.IO.Path.GetFileName(d)) == wanted)
            // "dự án An Đông" also finds a folder named "An Đông" and the other way round.
            ?? dirs.FirstOrDefault(d => Key(System.IO.Path.GetFileName(d)) is { Length: > 2 } k && (wanted.EndsWith(" " + k) || k.EndsWith(" " + wanted)));
        if (match is not null)
        {
            return (Describe(match), false);
        }

        if (!create)
        {
            return (null, false);
        }

        var dir = System.IO.Path.Combine(Root, SafeName(project));
        Directory.CreateDirectory(dir);
        return (Describe(dir), true);
    }

    /// <summary>Opens the folder in Windows Explorer so the customer can drop files in.</summary>
    public static bool Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ProjectFolder Describe(string dir)
    {
        int images = 0, videos = 0, audio = 0, other = 0;
        DateTime? last = Directory.GetLastWriteTime(dir);
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ext = System.IO.Path.GetExtension(file);
            if (ImageExt.Contains(ext)) images++;
            else if (VideoExt.Contains(ext)) videos++;
            else if (AudioExt.Contains(ext)) audio++;
            else other++;
            var changed = File.GetLastWriteTime(file);
            if (changed > last)
            {
                last = changed;
            }
        }

        return new ProjectFolder(System.IO.Path.GetFileName(dir), dir, images, videos, audio, other, last);
    }

    /// <summary>Comparison key: lower case, no accents, single spaces.</summary>
    internal static string Key(string name)
    {
        var normalized = name.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Keeps the customer's wording (accents included) but drops characters Windows forbids.</summary>
    internal static string SafeName(string project)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(project.Trim().Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd();
        }

        return cleaned.Length == 0 ? "Dự án mới" : cleaned;
    }
}
