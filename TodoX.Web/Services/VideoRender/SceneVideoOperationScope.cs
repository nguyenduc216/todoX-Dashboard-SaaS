using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

// Carries VIDEO ownership only. Image/audio/user edits do not acquire this scope.
// Database row triggers recheck it while holding the generation head SHARE lock.
public sealed class SceneVideoOperationScope : IDisposable
{
    private sealed record Identity(Guid? JobId, string? MergeSnapshot, bool UserSelection = false);
    private static readonly AsyncLocal<Identity?> Slot = new();
    private readonly Identity? _previous;
    private SceneVideoOperationScope(Identity identity) { _previous = Slot.Value; Slot.Value = identity; }
    public static SceneVideoOperationScope Enter(Guid jobId) => new(new(jobId, null));
    public static SceneVideoOperationScope Merge(string snapshot) => new(new(null, snapshot));
    public static SceneVideoOperationScope UserSelection() => new(new(null, null, true));
    public static Guid? JobId => Slot.Value?.JobId;
    public void Dispose() => Slot.Value = _previous;

    public static async Task BindConnectionAsync(IDbConnection connection)
    {
        var identity = Slot.Value;
        await connection.ExecuteAsync("SELECT set_config('todox.video_job',@job,false),set_config('todox.video_merge',@merge,false),set_config('todox.video_selection',@selection,false);",
            new { job = identity?.JobId?.ToString() ?? "", merge = identity?.MergeSnapshot ?? "", selection = identity?.UserSelection == true ? "true" : "false" });
    }

    public static async Task AssertCurrentAsync(IDbConnection conn, Guid tenant, Guid job, IDbTransaction? tx = null)
    {
        if (!await conn.ExecuteScalarAsync<bool>("SELECT video_render.scene_video_job_is_current(@tenant,@job);", new { tenant, job }, tx))
            throw new RenderJobPendingReconciliationException("Scene-video operation generation is no longer current; submit/mutation is fenced.");
    }
}
