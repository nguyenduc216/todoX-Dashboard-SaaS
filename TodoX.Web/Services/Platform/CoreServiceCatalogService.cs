using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Platform;

public interface ICoreServiceCatalogService
{
    Task<IReadOnlyList<CoreServiceView>> ListAsync(CancellationToken ct = default);
    Task<CoreServiceView?> GetByCodeAsync(string serviceCode, CancellationToken ct = default);
}

/// <summary>
/// Canonical service catalog projection for every TodoX client.
/// Dynamic form definitions are stored under catalog.services.default_options.form_schema so the
/// platform can ship the shared contract before introducing any additional catalog tables.
/// </summary>
public sealed class CoreServiceCatalogService : ICoreServiceCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;

    public CoreServiceCatalogService(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<CoreServiceView>> ListAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<CoreServiceRow>(new CommandDefinition(
            SelectSql +
            """
             WHERE lower(s.status) = 'active'
             ORDER BY s.sort_order, s.service_name;
            """,
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    public async Task<CoreServiceView?> GetByCodeAsync(string serviceCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceCode))
        {
            return null;
        }

        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CoreServiceRow>(new CommandDefinition(
            SelectSql +
            """
             WHERE upper(s.service_code) = upper(@serviceCode)
             LIMIT 1;
            """,
            new { serviceCode = serviceCode.Trim() },
            cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    private static CoreServiceView Map(CoreServiceRow row)
    {
        var formSchema = EmptyObject();
        if (!string.IsNullOrWhiteSpace(row.FormSchemaJson))
        {
            try
            {
                using var document = JsonDocument.Parse(row.FormSchemaJson);
                if (document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    formSchema = document.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                // Invalid legacy/default configuration must not break the catalog listing.
                formSchema = EmptyObject();
            }
        }

        IReadOnlyList<CoreServicePriceView> prices = Array.Empty<CoreServicePriceView>();
        if (!string.IsNullOrWhiteSpace(row.PricesJson))
        {
            try
            {
                prices = JsonSerializer.Deserialize<List<CoreServicePriceView>>(row.PricesJson, JsonOptions)
                    ?? new List<CoreServicePriceView>();
            }
            catch (JsonException)
            {
                prices = Array.Empty<CoreServicePriceView>();
            }
        }

        return new CoreServiceView(
            row.Id,
            row.ServiceCode,
            row.Name,
            row.ServiceType,
            row.Description,
            row.WorkflowCode,
            row.ThumbnailUrl,
            formSchema,
            prices,
            row.Enabled,
            row.SortOrder);
    }

    private static JsonElement EmptyObject()
        => JsonSerializer.SerializeToElement(new { }, JsonOptions);

    private const string SelectSql =
        """
        SELECT s.id AS Id,
               s.service_code AS ServiceCode,
               s.service_name AS Name,
               s.service_type AS ServiceType,
               COALESCE(NULLIF(s.short_description, ''), s.description) AS Description,
               s.workflow_code AS WorkflowCode,
               s.thumbnail_url AS ThumbnailUrl,
               COALESCE(s.default_options->'form_schema', '{}'::jsonb)::text AS FormSchemaJson,
               COALESCE((
                   SELECT jsonb_agg(jsonb_build_object(
                       'assetType', p.asset_type,
                       'qualityTier', p.quality_tier,
                       'durationSeconds', p.duration_seconds,
                       'sellPoints', p.sell_points,
                       'displayLabel', p.display_label
                   ) ORDER BY p.sort_order, p.asset_type, p.quality_tier, p.duration_seconds)
                     FROM catalog.service_sell_prices p
                    WHERE p.service_id = s.id
                      AND p.is_active = true
               ), '[]'::jsonb)::text AS PricesJson,
               CASE WHEN lower(s.status) = 'active' THEN true ELSE false END AS Enabled,
               s.sort_order AS SortOrder
          FROM catalog.services s
        """;

    private sealed class CoreServiceRow
    {
        public Guid Id { get; set; }
        public string ServiceCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? WorkflowCode { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string FormSchemaJson { get; set; } = "{}";
        public string PricesJson { get; set; } = "[]";
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
    }
}
