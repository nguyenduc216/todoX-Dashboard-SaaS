using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.EditVideoTool.Projects;

namespace TodoX.EditVideoTool.Server;

/// <summary>An exported video in a project's output folder, with what TodoX knows about it.</summary>
public sealed record LocalVideo(string Project, string Path, string ClientId, string Title, double? Seconds, string? Aspect, string? Style,
    string? Skill, int? SkillVersion, long Size, DateTime Rendered, Guid? ServerId, bool Synced)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Keeps the dashboard's "Jobs của tôi" in step with this computer: project and video information goes up by
/// itself, the video file only when the person presses "Đồng bộ". What TodoX answered is kept in the small
/// ".todox.json" file next to each video (clientId, serverId, synced).
/// </summary>
public sealed class ProjectSync
{
    public const int ChunkBytes = 8 * 1024 * 1024;
    private static readonly string[] VideoExt = [".mp4"];

    private readonly TodoXServerClient _server;
    private readonly ProjectFolders _projects;

    public ProjectSync(TodoXServerClient server, ProjectFolders projects)
    {
        _server = server;
        _projects = projects;
    }

    /// <summary>Every exported video of every project (or of one project), newest first.</summary>
    public IReadOnlyList<LocalVideo> Videos(string? project = null)
    {
        var list = new List<LocalVideo>();
        IEnumerable<ProjectFolder> folders;
        try
        {
            folders = project is null ? _projects.List() : _projects.Find(project, create: false).Folder is { } one ? [one] : [];
        }
        catch (IOException)
        {
            return list;
        }

        foreach (var folder in folders)
        {
            var output = System.IO.Path.Combine(folder.Path, ProjectFolders.OutputDir);
            if (!Directory.Exists(output))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(output).Where(f => VideoExt.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                list.Add(Read(folder.Name, file));
            }
        }

        return list.OrderByDescending(v => v.Rendered).ToList();
    }

    /// <summary>
    /// Only the last export of each project goes to the dashboard: earlier exports are drafts of the same video.
    /// Returns each project's newest video when it is not synced yet.
    /// </summary>
    public static IReadOnlyList<LocalVideo> LatestToSync(IEnumerable<LocalVideo> videos) =>
        videos.GroupBy(v => v.Project, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.Rendered).First())
            .Where(v => !v.Synced)
            .OrderByDescending(v => v.Rendered)
            .ToList();

    private static string InfoPath(string video) => video + ".todox.json";

    private static JsonObject LoadInfo(string video)
    {
        try
        {
            return File.Exists(InfoPath(video)) ? JsonNode.Parse(File.ReadAllText(InfoPath(video)))?.AsObject() ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static void SaveInfo(string video, JsonObject info)
    {
        try
        {
            File.WriteAllText(InfoPath(video), info.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (IOException)
        {
            // The video is what matters; the info file is rewritten on the next sync.
        }
    }

    private static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    private static double? Num(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    private static int? Int(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static LocalVideo Read(string project, string file)
    {
        var info = LoadInfo(file);
        var fi = new FileInfo(file);
        var clientId = Str(info, "clientId");
        if (clientId is null)
        {
            // Stable for this file: project, name, size and write time.
            clientId = "v-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{project}|{fi.Name}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}")))[..24].ToLowerInvariant();
            info["clientId"] = clientId;
            info["project"] ??= project;
            SaveInfo(file, info);
        }

        return new LocalVideo(project, file, clientId, System.IO.Path.GetFileNameWithoutExtension(file), Num(info, "seconds"), Str(info, "aspect"),
            Str(info, "style"), Str(info, "skill"), Int(info, "skillVersion"), fi.Length, fi.LastWriteTime,
            Guid.TryParse(Str(info, "serverId"), out var id) ? id : null, info["synced"] is JsonValue s && s.TryGetValue<bool>(out var b) && b);
    }

    /// <summary>
    /// Sends project and video information (never the files) and records TodoX's ids. Returns the videos that are
    /// still not synced, or an error message (offline, not activated).
    /// </summary>
    public async Task<(IReadOnlyList<LocalVideo> Pending, string? Error)> SyncInfoAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ProjectFolder> folders;
        try
        {
            folders = _projects.List();
        }
        catch (IOException ex)
        {
            return ([], ex.Message);
        }

        var videos = Videos();
        var body = new
        {
            projects = folders.Select(f => new
            {
                name = f.Name,
                videos = videos.Where(v => v.Project == f.Name).Select(v => new
                {
                    clientId = v.ClientId,
                    title = v.Title,
                    fileName = v.FileName,
                    durationSeconds = v.Seconds,
                    aspect = v.Aspect,
                    style = v.Style,
                    skill = v.Skill,
                    skillVersion = v.SkillVersion,
                    sizeBytes = v.Size,
                    renderedAt = new DateTimeOffset(v.Rendered).ToUniversalTime()
                })
            })
        };
        var result = await _server.SyncProjectsAsync(body, ct);
        if (!result.Ok)
        {
            return (LatestToSync(videos), result.Message ?? result.ErrorCode);
        }

        var answer = result.Value.ValueKind == JsonValueKind.Array ? result.Value.EnumerateArray().ToList() : [];
        var updatedVideos = new List<LocalVideo>();
        foreach (var v in videos)
        {
            var row = answer.FirstOrDefault(a => a.TryGetProperty("clientId", out var c) && c.GetString() == v.ClientId);
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = row.GetProperty("id").GetGuid();
            var synced = row.TryGetProperty("status", out var st) && st.GetString() == "synced";
            var info = LoadInfo(v.Path);
            info["serverId"] = id.ToString();
            info["synced"] = synced;
            SaveInfo(v.Path, info);
            updatedVideos.Add(v with { ServerId = id, Synced = synced });
        }

        return (LatestToSync(updatedVideos), null);
    }

    /// <summary>Uploads one video (the "Đồng bộ" button). Progress is 0..100.</summary>
    public async Task<string?> UploadAsync(LocalVideo video, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (video.ServerId is not { } id)
        {
            var (_, error) = await SyncInfoAsync(ct);
            if (error is not null)
            {
                return error;
            }

            video = Read(video.Project, video.Path);
            if (video.ServerId is not { } fresh)
            {
                return "TodoX chưa nhận thông tin video này.";
            }

            id = fresh;
        }

        var size = new FileInfo(video.Path).Length;
        var buffer = new byte[ChunkBytes];
        await using var file = File.OpenRead(video.Path);
        long offset = 0;
        while (offset < size)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(ChunkBytes, size - offset)), ct);
            if (read <= 0)
            {
                break;
            }

            var result = await _server.ProjectVideoChunkAsync(id, size, offset, buffer, read, ct);
            if (!result.Ok)
            {
                return result.Message ?? result.ErrorCode;
            }

            offset += read;
            progress?.Report(offset * 100.0 / size);
        }

        var info = LoadInfo(video.Path);
        info["serverId"] = id.ToString();
        info["synced"] = true;
        info["syncedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SaveInfo(video.Path, info);
        return null;
    }
}
