using System.Text.Json;

namespace TodoX.EditVideoTool.Setup;

/// <summary>
/// Skill runs in progress: a short id for the assistant (r1, r2…) mapped to the server-signed run state. Kept in a
/// small file so a run survives a restart of the tool; only the last few runs are remembered.
/// </summary>
public sealed class SkillRunStore
{
    private const int Keep = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _file;
    private readonly Lock _gate = new();

    public SkillRunStore(AgentPaths paths)
    {
        _file = Path.Combine(paths.Root, "skill-runs.json");
    }

    private sealed record Entry(string Id, string State, DateTimeOffset Updated);

    /// <summary>Stores a new state under <paramref name="id"/> (a new id when null) and returns the id.</summary>
    public string Save(string? id, string state)
    {
        lock (_gate)
        {
            var entries = Load();
            id ??= $"r{(entries.Select(e => int.TryParse(e.Id.TrimStart('r'), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1)}";
            entries.RemoveAll(e => e.Id == id);
            entries.Add(new Entry(id, state, DateTimeOffset.Now));
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(entries.OrderByDescending(e => e.Updated).Take(Keep).ToList(), Json));
            return id;
        }
    }

    /// <summary>The state of run <paramref name="id"/>, or of the most recent run when no id is given.</summary>
    public (string Id, string State)? Find(string? id)
    {
        lock (_gate)
        {
            var entries = Load();
            var entry = string.IsNullOrWhiteSpace(id) ? entries.MaxBy(e => e.Updated) : entries.FirstOrDefault(e => e.Id == id.Trim());
            return entry is null ? null : (entry.Id, entry.State);
        }
    }

    private List<Entry> Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }
}
