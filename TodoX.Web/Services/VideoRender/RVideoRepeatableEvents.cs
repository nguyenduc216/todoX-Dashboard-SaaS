using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// "State" events the background lifecycle re-evaluates every few seconds (skipped / not ready / validation failed).
/// Writing them on every pass filled video_project_events (2026-11-11 audit: ~4,400 rows/day, one job 343 rows of the
/// same "voice skipped" line). Such an event is written once and then again only when its content changes or after
/// <see cref="Window"/>, so the history still shows the state and when it persisted.
/// </summary>
public static class RVideoRepeatableEvents
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED",
        "SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED",
        "SCENE_AUDIO_AUTO_ENQUEUE_FAILED",
        "PROJECT_FINAL_MERGE_NOT_READY",
        "RVIDEO_AUTO_VALIDATION_FAILED",
        "SCENE_IMAGE_REFERENCE_VALIDATION_FAILED",
        "RVIDEO_IMAGE_REFERENCE_VALIDATION_FAILED"
    };

    /// <summary>Per-attempt identifiers that change every pass without changing the state being reported.</summary>
    private static readonly string[] VolatileKeys = ["jobId", "activeJobId", "renderJobId", "checkedAt", "timestamp", "_sig"];

    public const string SignatureKey = "_sig";

    public static bool IsRepeatable(string eventType) => Types.Contains(eventType);

    /// <summary>Signature of the event's content, ignoring per-pass identifiers.</summary>
    public static string Signature(string eventType, string dataJson)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(dataJson); }
        catch (System.Text.Json.JsonException) { node = null; }
        if (node is JsonObject obj)
            foreach (var key in VolatileKeys)
                foreach (var existing in obj.Select(x => x.Key).Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList())
                    obj.Remove(existing);
        var canonical = eventType + "|" + (node?.ToJsonString() ?? dataJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    /// <summary>The data to store, carrying its signature.</summary>
    public static string WithSignature(string dataJson, string signature)
    {
        try
        {
            if (JsonNode.Parse(dataJson) is JsonObject obj)
            {
                obj[SignatureKey] = signature;
                return obj.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException) { }
        return dataJson;
    }
}
