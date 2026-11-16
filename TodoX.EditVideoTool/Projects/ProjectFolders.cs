using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TodoX.EditVideoTool.Projects;

public sealed record ProjectFolder(string Name, string Path, int Images, int Videos, int Audio, int Other, DateTime? LastChanged);

/// <summary>
/// The customer's resource folder: one sub-folder per video project ("Dự án An Đông"), laid out the same way every
/// time (see <see cref="Layout"/>) with a CLAUDE.md that tells Claude what the project is and which TodoX tools to
/// use. Finished videos go to the project's "output" folder.
/// </summary>
public sealed class ProjectFolders
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".bmp", ".gif", ".svg" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".mts", ".3gp" };
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac" };

    public const string ImagesDir = "01-anh";
    public const string VideosDir = "02-video";
    public const string AudioDir = "03-am-thanh";
    public const string ScriptDir = "04-kich-ban";
    public const string BrandDir = "05-thuong-hieu";
    public const string AiDir = "06-ai";
    public const string OutputDir = "output";
    public const string ClaudeFile = "CLAUDE.md";

    /// <summary>Every project's sub-folders and what goes in each (also written into its CLAUDE.md).</summary>
    public static readonly IReadOnlyList<(string Name, string Holds)> Layout =
    [
        (ImagesDir, "ảnh sản phẩm, chân dung, bối cảnh khách gửi"),
        (VideosDir, "clip quay thô"),
        (AudioDir, "nhạc nền, giọng thu sẵn"),
        (ScriptDir, "brief, kịch bản, lời đọc, phụ đề (.md, .txt, .docx)"),
        (BrandDir, "logo, bảng màu, font, mẫu thương hiệu"),
        (AiDir, "ảnh / video AI TodoX tạo cho dự án"),
        (OutputDir, "video TodoX xuất ra")
    ];

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
            .Where(d => !System.IO.Path.GetFileName(d).StartsWith('.') && !System.IO.Path.GetFileName(d).StartsWith('_')) // _Thu vien TodoX is not a project
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
            EnsureLayout(match);
            return (Describe(match), false);
        }

        if (!create)
        {
            return (null, false);
        }

        var dir = System.IO.Path.Combine(Root, SafeName(project));
        Directory.CreateDirectory(dir);
        EnsureLayout(dir);
        return (Describe(dir), true);
    }

    /// <summary>
    /// The project folder a file belongs to (its first folder under the resource folder), or null outside it.
    /// </summary>
    public string? ProjectOf(string file)
    {
        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(Root)) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(file);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var first = full[root.Length..].Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], 2)[0];
        var dir = System.IO.Path.Combine(root, first);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Adds the standard sub-folders and CLAUDE.md a project is missing. Files already there are never moved, and a
    /// CLAUDE.md the customer edited is kept.
    /// </summary>
    public static void EnsureLayout(string dir)
    {
        foreach (var (name, _) in Layout)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(dir, name));
        }

        var claude = System.IO.Path.Combine(dir, ClaudeFile);
        if (!File.Exists(claude))
        {
            File.WriteAllText(claude, ClaudeBrief(System.IO.Path.GetFileName(dir), dir, DateTime.Now), new UTF8Encoding(false));
        }
    }

    /// <summary>CLAUDE.md of a project: Claude (Desktop, Code, Cowork) reads it when a task is opened on the folder.</summary>
    internal static string ClaudeBrief(string name, string dir, DateTime created)
    {
        var folders = string.Join("\n", Layout.Select(f => $"- `{f.Name}/` — {f.Holds}"));
        return $"""
            # Dự án video: {name}

            Thư mục dự án TodoX EditVideo, tạo ngày {created:dd/MM/yyyy}.
            Đường dẫn: `{dir}`

            ## Thông tin dự án (khách hoặc Claude ghi thêm)

            - Thương hiệu / sản phẩm:
            - Mục tiêu video:
            - Nền tảng đăng (TikTok, Reels, YouTube...):
            - Khung hình (9:16 / 16:9 / 1:1):
            - Độ dài mong muốn:
            - Giọng đọc, màu thương hiệu, lời kêu gọi cuối video:

            ## Thư mục

            {folders}

            ## Quy tắc khi làm việc trên dự án này

            - Chỉ dùng công cụ TodoX (`todox_*`, máy chủ MCP `todox`) để xem, cắt, ghép, phụ đề, nhạc, giọng đọc, hiệu ứng
              và xuất video. Không dùng chương trình dựng video khác, lệnh dòng lệnh hay viết code để xử lý ảnh/video.
            - Bắt đầu bằng `todox_project_folder` với tên dự án `{name}`, rồi `todox_probe` thư mục `{VideosDir}` và `{AiDir}`.
            - Đọc brief / kịch bản trong `{ScriptDir}` trước khi hỏi khách. Kịch bản, lời đọc Claude viết thì lưu vào đó.
            - Logo, màu trong `{BrandDir}` dùng cho `todox_titles` và `todox_look`.
            - Có skill TodoX hợp với loại video (todox-...) thì chạy bằng `todox_skill_start` rồi `todox_skill_next`.
            - Ảnh/video AI tốn điểm: `todox_ai_models` trước, báo giá điểm và chờ khách đồng ý, rồi `todox_image_create` /
              `todox_video_create` với `project: "{name}"` (kết quả vào `{AiDir}/`).
            - Video xuất ra nằm trong `{OutputDir}/`. Báo khách đường dẫn và độ dài.
            - Cuối mỗi buổi, ghi tóm tắt việc đã làm và bản video đã gửi khách vào mục Nhật ký bên dưới để lần sau làm tiếp.

            ## Nhật ký dự án

            """;
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
