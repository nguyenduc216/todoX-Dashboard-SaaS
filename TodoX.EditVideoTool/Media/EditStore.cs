using System.Text.Json;

namespace TodoX.EditVideoTool.Media;

/// <summary>One source clip of an edit: the local file and the metadata the server sees (never the path).</summary>
public sealed record EditClip(string Id, string Path, double Seconds, int Width, int Height, bool HasVideo, bool HasAudio);

/// <summary>
/// A video being edited step by step. <see cref="Document"/> is the TodoX-signed plan; each todox_* change sends it
/// back to the server and stores the re-signed one. Media maps plan ids (c0, music, voiceover) to local files.
/// </summary>
public sealed class EditProject
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public string Aspect { get; set; } = "9:16";
    public string Document { get; set; } = string.Empty;
    public List<EditClip> Clips { get; set; } = [];
    public Dictionary<string, string> Media { get; set; } = [];
    public string? Skill { get; set; }
    public int? SkillVersion { get; set; }
    public string? LastOutput { get; set; }

    /// <summary>What the server is allowed to know about the clips.</summary>
    public IEnumerable<object> ClipMetadata() =>
        Clips.Select(c => new { id = c.Id, durationSeconds = c.Seconds, width = c.Width, height = c.Height, hasVideo = c.HasVideo, hasAudio = c.HasAudio });

    public string NextClipId() => $"c{Clips.Count}";
}

/// <summary>Edits live under the tool's data folder (edits\&lt;id&gt;.json); the most recent one is the default.</summary>
public sealed class EditStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AgentPaths _paths;

    public EditStore(AgentPaths paths)
    {
        _paths = paths;
    }

    private string Dir => Path.Combine(_paths.Root, "edits");

    public EditProject Create(string aspect)
    {
        var now = DateTimeOffset.Now;
        return new EditProject { Id = $"v{now:MMdd-HHmmss}", Created = now, Updated = now, Aspect = aspect };
    }

    public void Save(EditProject edit)
    {
        Directory.CreateDirectory(Dir);
        edit.Updated = DateTimeOffset.Now;
        var file = Path.Combine(Dir, edit.Id + ".json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(edit, Json));
        File.Move(file + ".tmp", file, overwrite: true);
    }

    /// <summary>The named edit, or the most recently changed one when no name is given.</summary>
    public EditProject? Find(string? id)
    {
        if (!Directory.Exists(Dir))
        {
            return null;
        }

        var file = string.IsNullOrWhiteSpace(id)
            ? new DirectoryInfo(Dir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
            : Path.Combine(Dir, Path.GetFileName(id.Trim()) + ".json");
        if (file is null || !File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EditProject>(File.ReadAllText(file), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
