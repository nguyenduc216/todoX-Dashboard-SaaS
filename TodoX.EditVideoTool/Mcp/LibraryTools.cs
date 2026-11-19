using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

/// <summary>The account's dashboard resources and the "Jobs của tôi" sync, for the assistant.</summary>
[McpServerToolType]
public sealed class LibraryTools
{
    private readonly LibrarySync _library;
    private readonly ProjectSync _sync;

    public LibraryTools(LibrarySync library, ProjectSync sync)
    {
        _library = library;
        _sync = sync;
    }

    [McpServerTool(Name = "todox_library", ReadOnly = true, Idempotent = true)]
    [Description("What this TodoX account already has, downloaded to this computer: AI characters (name, description, image), TodoX voices (code, sample), music (TodoX background music and the customer's own AI songs, as local .mp3 files usable as music in todox_auto_edit / todox_audio) and VFX models for todox_video_create. Use it to answer 'what characters / voices / music / VFX do I have' and to pick files for a video.")]
    public async Task<string> Library(
        CancellationToken ct,
        [Description("characters | voices | music | vfx | all (default all).")] string? kind = null,
        [Description("Fetch the latest from the dashboard first (default false: use the copy refreshed in the last 15 minutes).")] bool refresh = false)
    {
        var (index, error) = await _library.GetAsync(refresh, ct);
        var k = string.IsNullOrWhiteSpace(kind) ? "all" : kind.Trim().ToLowerInvariant();
        var result = new JsonObject
        {
            ["folder"] = _library.Folder,
            ["updatedAt"] = index["updatedAt"]?.DeepClone()
        };
        foreach (var part in new[] { "characters", "voices", "music", "vfx" })
        {
            if (k == "all" || k == part)
            {
                result[part] = index[part]?.DeepClone() ?? new JsonArray();
            }
        }

        if (error is not null)
        {
            result["note"] = "Không cập nhật được từ TodoX (" + error + "); đang dùng bản đã tải trước đó.";
        }

        return result.ToJsonString(StatusTools.Json);
    }

    [McpServerTool(Name = "todox_sync", Destructive = false)]
    [Description("Show which exported videos are on the dashboard ('Jobs của tôi'). Project and video information is sent automatically; the video FILES are uploaded only when the user presses 'Đồng bộ' in the TodoX EditVideo window — tell the user to do that; this tool never uploads videos.")]
    public async Task<string> Sync(CancellationToken ct, [Description("Only this project (optional).")] string? project = null)
    {
        var (pending, error) = await _sync.SyncInfoAsync(ct);
        var videos = _sync.Videos(project);
        return JsonSerializer.Serialize(new
        {
            note = error is null
                ? "Chỉ video xuất cuối cùng của mỗi dự án được đồng bộ (bản cũ hơn bỏ qua). Video chưa đồng bộ: mở cửa sổ TodoX EditVideo và bấm 'Đồng bộ' để đưa lên Dashboard (Jobs của tôi)."
                : "Không kết nối được TodoX: " + error,
            pending = pending.Count,
            videos = videos.Select(v => new { project = v.Project, file = v.Path, synced = v.Synced })
        }, StatusTools.Json);
    }
}
