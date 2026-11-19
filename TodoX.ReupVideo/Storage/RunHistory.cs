using System.Text.Json;

namespace TodoX.ReupVideo.Storage;

public enum RunEntryKind
{
    Posted,
    Failed,
    /// <summary>TikTok link no longer active: not posted.</summary>
    Inactive
}

/// <summary>One thing that happened to one link in a run (one fanpage post, or the link found inactive).</summary>
public sealed class RunEntry
{
    public RunEntryKind Kind { get; set; }
    public string VideoId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Text { get; set; }
    public string? PageId { get; set; }
    public string? PageName { get; set; }
    public string? FacebookId { get; set; }
    public string? Message { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>One reup run ("Reup ngay", scheduled, or "Reup lại").</summary>
public sealed class RunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Trigger { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }
    public int NewLinks { get; set; }
    public int Posted { get; set; }
    public int Failed { get; set; }
    public int Inactive { get; set; }
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
    public List<RunEntry> Entries { get; set; } = [];
}

/// <summary>History of the last runs (history.json), newest first.</summary>
public sealed class RunHistory
{
    public const int Keep = 500;

    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<RunRecord> _runs;

    public event Action? Changed;

    private RunHistory(string file, List<RunRecord> runs)
    {
        _file = file;
        _runs = runs;
    }

    public static RunHistory Load(string file)
    {
        List<RunRecord> runs = [];
        try
        {
            if (File.Exists(file))
            {
                runs = JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(file), AppSettings.Json) ?? [];
            }
        }
        catch (Exception)
        {
            AppSettings.TryBackup(file);
        }

        return new RunHistory(file, runs);
    }

    public IReadOnlyList<RunRecord> Snapshot()
    {
        lock (_gate)
        {
            return _runs.ToList();
        }
    }

    public void Save(RunRecord run)
    {
        lock (_gate)
        {
            if (!_runs.Contains(run))
            {
                _runs.Insert(0, run);
            }

            if (_runs.Count > Keep)
            {
                _runs.RemoveRange(Keep, _runs.Count - Keep);
            }

            try
            {
                var temp = _file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_runs, AppSettings.Json));
                File.Move(temp, _file, overwrite: true);
            }
            catch (Exception)
            {
                // history is informational: a locked file must not stop a run
            }
        }

        Changed?.Invoke();
    }
}
