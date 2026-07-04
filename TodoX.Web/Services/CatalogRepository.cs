using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record ServicePackageView(Guid Id, string Code, string Name, string ServiceType,
    decimal BaseTokens, decimal TokenPerSecond, decimal VoiceTokens, decimal CaptionTokens, bool IsActive);

public sealed record RenderOrderView(Guid Id, string OrderCode, string CustomerName, string ServiceType,
    int VideoSeconds, bool HasVoice, bool HasCaption, decimal EstimatedTokens, decimal ActualTokens,
    string Status, string? ResultUrl, DateTime CreatedAt);

public sealed record RenderStats(long Processing, long Completed, long Queued, long Failed, long Total);

/// <summary>Read access to public.service_packages and public.render_orders (Foundation V2).</summary>
public sealed class CatalogRepository
{
    private readonly TodoXConnectionFactory _factory;

    public CatalogRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ServicePackageView>> GetServicePackagesAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ServicePackageView>(
            """
            SELECT id AS Id, code AS Code, name AS Name, service_type AS ServiceType,
                   base_tokens AS BaseTokens, token_per_second AS TokenPerSecond,
                   voice_tokens AS VoiceTokens, caption_tokens AS CaptionTokens, is_active AS IsActive
              FROM public.service_packages
             ORDER BY service_type, name;
            """);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RenderOrderView>> GetRenderOrdersAsync(string? statusFilter = null, Guid? customerId = null)
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<RenderOrderView>(
            """
            SELECT o.id AS Id, o.order_code AS OrderCode,
                   COALESCE(NULLIF(c.company_name,''), c.full_name) AS CustomerName,
                   o.service_type AS ServiceType, o.video_seconds AS VideoSeconds,
                   o.has_voice AS HasVoice, o.has_caption AS HasCaption,
                   o.estimated_tokens AS EstimatedTokens, o.actual_tokens AS ActualTokens,
                   o.status AS Status, o.result_url AS ResultUrl, o.created_at AS CreatedAt
              FROM public.render_orders o
              JOIN crm.customers c ON c.id = o.customer_id
             WHERE (@status IS NULL OR o.status = @status)
               AND (@cid IS NULL OR o.customer_id = @cid)
             ORDER BY o.created_at DESC
             LIMIT 200;
            """, new { status = statusFilter, cid = customerId });
        return rows.ToList();
    }

    public async Task<RenderStats> GetRenderStatsAsync(Guid? customerId = null)
    {
        using var conn = await _factory.OpenAsync();
        var row = await conn.QuerySingleAsync<RenderStats>(
            """
            SELECT
                count(*) FILTER (WHERE status IN ('Processing','Rendering','Running')) AS Processing,
                count(*) FILTER (WHERE status IN ('Completed','Done','Success')) AS Completed,
                count(*) FILTER (WHERE status IN ('Queued','Draft','Pending')) AS Queued,
                count(*) FILTER (WHERE status IN ('Failed','Error')) AS Failed,
                count(*) AS Total
              FROM public.render_orders
             WHERE (@cid IS NULL OR customer_id = @cid);
            """, new { cid = customerId });
        return row;
    }
}
