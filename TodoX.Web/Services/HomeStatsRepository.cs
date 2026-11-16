using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

/// <summary>Customer jobs for the home page "Render Jobs" block.</summary>
public sealed record RenderJobHomeStats(int Total, int ThisMonth, int Running, int Completed, int FailedThisMonth);

/// <summary>
/// Read-only figures for the dashboard home page blocks (see <see cref="HomeWidgets"/>).
/// A "job" is what a customer ordered — the same kinds the job monitor lists: service jobs
/// (render.render_jobs core_service), timelapse jobs and fashion dance jobs. Internal child jobs
/// (one per scene image / scene video / merge) are not counted, and neither are drafts never started.
/// </summary>
public sealed class HomeStatsRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public HomeStatsRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    /// <summary>Statuses that mean the job is finished, stopped or not started — everything else is running.</summary>
    internal static readonly string[] NotRunning = ["draft", "completed", "failed", "timeout", "cancelled", "paused"];

    public async Task<RenderJobHomeStats> GetRenderJobStatsAsync(DateTime monthStartLocal)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var hasDance = await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('dance_sell.dance_sell_jobs') IS NOT NULL;");
        var dance = hasDance
            ? "UNION ALL SELECT lower(d.status), d.created_at FROM dance_sell.dance_sell_jobs d WHERE d.tenant_id = @tenant"
            : string.Empty;
        return await conn.QuerySingleAsync<RenderJobHomeStats>(
            $"""
            WITH jobs(status, created_at) AS (
                SELECT lower(j.status), j.created_at
                  FROM render.render_jobs j
                 WHERE j.tenant_id = @tenant AND j.job_type IN ('core_service', 'timelapse')
                {dance}
            )
            SELECT count(*) FILTER (WHERE status <> 'draft')::int AS Total,
                   count(*) FILTER (WHERE status <> 'draft' AND created_at >= @monthStart)::int AS ThisMonth,
                   count(*) FILTER (WHERE NOT (status = ANY(@notRunning)))::int AS Running,
                   count(*) FILTER (WHERE status = 'completed')::int AS Completed,
                   count(*) FILTER (WHERE status IN ('failed', 'timeout') AND created_at >= @monthStart)::int AS FailedThisMonth
              FROM jobs;
            """,
            new
            {
                tenant = _tenant.TenantId,
                monthStart = DateTime.SpecifyKind(monthStartLocal, DateTimeKind.Local).ToUniversalTime(),
                notRunning = NotRunning
            });
    }
}
