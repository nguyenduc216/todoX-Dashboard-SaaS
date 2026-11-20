using System.Text.Json;

namespace TodoX.ReupVideo.Storage;

public enum TokenEventKind
{
    /// <summary>The user saved a token by hand (Cách 1 / Cách 2).</summary>
    Updated,
    /// <summary>Short-lived token exchanged for a long-lived one with the user's App ID + Secret.</summary>
    Exchanged,
    /// <summary>A page token replaced by itself before it expired.</summary>
    Renewed,
    /// <summary>Daily / manual check summary.</summary>
    Checked,
    /// <summary>Token expires soon and could not be renewed by itself.</summary>
    Warning,
    /// <summary>Token expired or no longer valid.</summary>
    Expired,
    /// <summary>A renewal or exchange attempt failed.</summary>
    Failed
}

public sealed class TokenEvent
{
    public DateTime At { get; set; } = DateTime.Now;
    public TokenEventKind Kind { get; set; }
    /// <summary>null = the Facebook account / app, not one fanpage.</summary>
    public string? PageId { get; set; }
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Expiry after this event; null = never / not applicable (see <see cref="NeverExpires"/>).</summary>
    public DateTime? ExpiresAt { get; set; }
    public bool NeverExpires { get; set; }
}

/// <summary>Token renewal history (token-history.json), newest first. Never holds a token value.</summary>
public sealed class TokenHistory
{
    public const int Keep = 1000;

    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<TokenEvent> _events;

    public event Action? Changed;

    private TokenHistory(string file, List<TokenEvent> events)
    {
        _file = file;
        _events = events;
    }

    public static TokenHistory Load(string file)
    {
        List<TokenEvent> events = [];
        try
        {
            if (File.Exists(file))
            {
                events = JsonSerializer.Deserialize<List<TokenEvent>>(File.ReadAllText(file), AppSettings.Json) ?? [];
            }
        }
        catch (Exception)
        {
            AppSettings.TryBackup(file);
        }

        return new TokenHistory(file, events);
    }

    public IReadOnlyList<TokenEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }

    public void Add(TokenEventKind kind, string? pageId, string name, string detail, DateTime? expiresAt = null, bool neverExpires = false)
    {
        lock (_gate)
        {
            _events.Insert(0, new TokenEvent { Kind = kind, PageId = pageId, Name = name, Detail = detail, ExpiresAt = expiresAt, NeverExpires = neverExpires });
            if (_events.Count > Keep)
            {
                _events.RemoveRange(Keep, _events.Count - Keep);
            }

            try
            {
                var temp = _file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_events, AppSettings.Json));
                File.Move(temp, _file, overwrite: true);
            }
            catch (Exception)
            {
                // informational only
            }
        }

        Changed?.Invoke();
    }
}
