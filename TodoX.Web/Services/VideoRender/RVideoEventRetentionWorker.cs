using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Daily retention for video_render.video_project_events (user decision 2026-11-11): job logs older than
/// <c>RVideo:EventRetention:Days</c> (default 30) of finished jobs (status completed / failed) are deleted in small
/// batches. Kept regardless of age: events the application reads back (parent voice billing, scene link, provider
/// resources, image queue) and every billing / charge / refund / point line, so point history stays auditable.
/// </summary>
public sealed class RVideoEventRetentionWorker : BackgroundService
{
    /// <summary>Event types the application reads back at runtime; never removed by retention.</summary>
    internal static readonly string[] KeepEventTypes =
    [
        "RVIDEO_PARENT_BILLED",
        "RVIDEO_SCENE_LINK_STARTED",
        "RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE",
        "SCENE_IMAGE_QUEUED"
    ];

    /// <summary>Money-related events are kept for audits.</summary>
    internal const string KeepPattern = "(BILLING|BILLED|CHARGE|REFUND|POINT)";

    internal const string DeleteBatchSql = """
        WITH doomed AS (
            SELECT e.id
              FROM video_render.video_project_events e
              JOIN video_render.video_projects p ON p.id = e.project_id
             WHERE e.created_at < now() - make_interval(days => @days)
               AND p.status = ANY(@statuses)
               AND NOT (e.event_type = ANY(@keep))
               AND e.event_type !~ @keepPattern
             LIMIT @batch)
        DELETE FROM video_render.video_project_events e USING doomed d WHERE e.id = d.id;
        """;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RVideoEventRetentionWorker> _logger;

    public RVideoEventRetentionWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RVideoEventRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("RVideo:EventRetention:Enabled", true)) return;
        try { await Task.Delay(TimeSpan.FromMinutes(_configuration.GetValue("RVideo:EventRetention:StartDelayMinutes", 10)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "RVIDEO_EVENT_RETENTION_FAILED"); }

            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task<long> RunOnceAsync(CancellationToken ct)
    {
        var days = Math.Max(7, _configuration.GetValue("RVideo:EventRetention:Days", 30));
        var batch = Math.Clamp(_configuration.GetValue("RVideo:EventRetention:BatchSize", 5000), 100, 50000);
        var statuses = _configuration.GetSection("RVideo:EventRetention:Statuses").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : [Models.VideoProjectStatuses.Completed, Models.VideoProjectStatuses.Failed];

        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<TodoXConnectionFactory>();
        long total = 0;
        // Bounded: at most 2,000 batches per run (10 million rows at the default size), then the next day continues.
        for (var i = 0; i < 2000 && !ct.IsCancellationRequested; i++)
        {
            using var conn = await factory.OpenAsync(ct);
            var deleted = await conn.ExecuteAsync(new CommandDefinition(DeleteBatchSql,
                new { days, statuses, keep = KeepEventTypes, keepPattern = KeepPattern, batch }, commandTimeout: 120, cancellationToken: ct));
            total += deleted;
            if (deleted < batch) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        // The archive of purged repeated lines follows the same age limit.
        using (var conn = await factory.OpenAsync(ct))
        {
            if (await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('video_render.video_project_events_purged') IS NOT NULL;"))
                total += await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM video_render.video_project_events_purged WHERE purged_at < now() - make_interval(days => @days);",
                    new { days }, commandTimeout: 120, cancellationToken: ct));
        }

        _logger.LogInformation("RVIDEO_EVENT_RETENTION_DONE deleted={Deleted} days={Days} statuses={Statuses}", total, days, string.Join(",", statuses));
        return total;
    }
}
