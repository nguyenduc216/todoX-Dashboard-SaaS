using System.Collections.Concurrent;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Stop point for automatic per-scene work the lifecycle retries on every pass (scene voice today): after a failure the
/// next try waits 1, 2, 4 ... minutes (at most 60), and after <see cref="MaxFailures"/> failures in a row the scene is
/// left alone until the job's settings change (the user fixed something) or the app restarts. Without it a scene that
/// always throws (e.g. an out-of-range TTS rate) failed and logged every few seconds for hours.
/// </summary>
public static class RVideoLifecycleBackoff
{
    public const int MaxFailures = 6;

    public sealed record Entry(int Failures, DateTime RetryAtUtc, DateTime SettingsUpdatedAt);
    public sealed record Outcome(int Failures, TimeSpan Delay, bool Halted);

    private static readonly ConcurrentDictionary<long, Entry> Entries = new();

    public static bool ShouldAttempt(long sceneId, DateTime settingsUpdatedAt, DateTime nowUtc)
    {
        if (!Entries.TryGetValue(sceneId, out var entry)) return true;
        if (entry.SettingsUpdatedAt != settingsUpdatedAt)
        {
            Entries.TryRemove(sceneId, out _);
            return true;
        }
        return nowUtc >= entry.RetryAtUtc;
    }

    public static Outcome Failed(long sceneId, DateTime settingsUpdatedAt, DateTime nowUtc)
    {
        var previous = Entries.TryGetValue(sceneId, out var e) && e.SettingsUpdatedAt == settingsUpdatedAt ? e.Failures : 0;
        var failures = previous + 1;
        var halted = failures >= MaxFailures;
        var delay = halted ? TimeSpan.MaxValue : TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, failures - 1)));
        Entries[sceneId] = new Entry(failures, halted ? DateTime.MaxValue : nowUtc + delay, settingsUpdatedAt);
        return new Outcome(failures, halted ? TimeSpan.Zero : delay, halted);
    }

    public static void Succeeded(long sceneId) => Entries.TryRemove(sceneId, out _);

    internal static void Reset() => Entries.Clear();
}
