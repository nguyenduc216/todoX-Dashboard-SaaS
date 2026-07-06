using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed class ServiceCategoryDto
{
    public Guid Id { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ServiceDto
{
    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "render_video";
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
    public int TierCount { get; set; }
    public decimal? MinTokenCost { get; set; }
}

public sealed class ServiceIllustrationDialogValue
{
    public string SelectedImageUrl { get; set; } = string.Empty;
    public string? PromptUsed { get; set; }
}

public sealed class ServicePricingTierDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string TierCode { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public decimal TokenCost { get; set; }
    public decimal? CurrencyPrice { get; set; }
    public int? MaxDurationSec { get; set; }
    public int? MaxSceneCount { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Read/write access to catalog.* (services, categories, pricing tiers). Schema installed by V003.</summary>
public sealed class CatalogAdminRepository
{
    private readonly TodoXConnectionFactory _factory;

    public CatalogAdminRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    // ---------- Categories ----------
    public async Task<IReadOnlyList<ServiceCategoryDto>> GetCategoriesAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ServiceCategoryDto>(
            """
            SELECT id AS Id, category_code AS CategoryCode, category_name AS CategoryName,
                   description AS Description, sort_order AS SortOrder, is_active AS IsActive
              FROM catalog.service_categories
             ORDER BY sort_order, category_name;
            """);
        return rows.ToList();
    }

    // ---------- Services ----------
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ServiceDto>(
            """
            SELECT s.id AS Id, s.category_id AS CategoryId, c.category_name AS CategoryName,
                   s.service_code AS ServiceCode, s.service_name AS ServiceName, s.service_type AS ServiceType,
                   s.description AS Description, s.short_description AS ShortDescription,
                   s.thumbnail_url AS ThumbnailUrl, s.cover_image_url AS CoverImageUrl,
                   s.status AS Status, s.sort_order AS SortOrder,
                   (SELECT count(*) FROM catalog.service_pricing_tiers t WHERE t.service_id = s.id) AS TierCount,
                   (SELECT min(token_cost) FROM catalog.service_pricing_tiers t WHERE t.service_id = s.id AND t.is_active) AS MinTokenCost
              FROM catalog.services s
              LEFT JOIN catalog.service_categories c ON c.id = s.category_id
             ORDER BY s.sort_order, s.service_name;
            """);
        return rows.ToList();
    }

    public async Task<bool> ServiceCodeExistsAsync(string code, Guid? excludeId = null)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM catalog.services WHERE lower(service_code)=lower(@c) AND (@id IS NULL OR id<>@id));",
            new { c = code, id = excludeId });
    }

    public async Task<Guid> InsertServiceAsync(ServiceDto s)
    {
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO catalog.services
                (id, category_id, service_code, service_name, service_type, description,
                 short_description, thumbnail_url, cover_image_url, status, sort_order, created_at)
            VALUES
                (@id, @cat, @code, @name, @type, @desc, @short, @thumb, @cover, @status, @sort, now());
            """,
            new
            {
                id, cat = s.CategoryId, code = s.ServiceCode, name = s.ServiceName, type = s.ServiceType,
                desc = s.Description, @short = s.ShortDescription, thumb = s.ThumbnailUrl,
                cover = s.CoverImageUrl, status = s.Status, sort = s.SortOrder
            });
        return id;
    }

    public async Task UpdateServiceAsync(ServiceDto s)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE catalog.services
               SET category_id=@cat, service_code=@code, service_name=@name, service_type=@type,
                   description=@desc, short_description=@short, thumbnail_url=@thumb,
                   cover_image_url=@cover, status=@status, sort_order=@sort, updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id = s.Id, cat = s.CategoryId, code = s.ServiceCode, name = s.ServiceName, type = s.ServiceType,
                desc = s.Description, @short = s.ShortDescription, thumb = s.ThumbnailUrl,
                cover = s.CoverImageUrl, status = s.Status, sort = s.SortOrder
            });
    }

    public async Task DeleteServiceAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        // pricing tiers & assets cascade on service delete.
        await conn.ExecuteAsync("DELETE FROM catalog.services WHERE id=@id;", new { id });
    }

    // ---------- Pricing tiers ----------
    public async Task<IReadOnlyList<ServicePricingTierDto>> GetTiersAsync(Guid serviceId)
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ServicePricingTierDto>(
            """
            SELECT id AS Id, service_id AS ServiceId, tier_code AS TierCode, tier_name AS TierName,
                   token_cost AS TokenCost, currency_price AS CurrencyPrice,
                   max_duration_sec AS MaxDurationSec, max_scene_count AS MaxSceneCount,
                   is_default AS IsDefault, is_active AS IsActive
              FROM catalog.service_pricing_tiers
             WHERE service_id=@sid
             ORDER BY token_cost;
            """, new { sid = serviceId });
        return rows.ToList();
    }

    public async Task InsertTierAsync(ServicePricingTierDto t)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO catalog.service_pricing_tiers
                (id, service_id, tier_code, tier_name, token_cost, currency_price,
                 max_duration_sec, max_scene_count, is_default, is_active, created_at)
            VALUES
                (gen_random_uuid(), @sid, @code, @name, @cost, @price, @dur, @scene, @def, @active, now());
            """,
            new
            {
                sid = t.ServiceId, code = t.TierCode, name = t.TierName, cost = t.TokenCost,
                price = t.CurrencyPrice, dur = t.MaxDurationSec, scene = t.MaxSceneCount,
                def = t.IsDefault, active = t.IsActive
            });
    }

    public async Task DeleteTierAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM catalog.service_pricing_tiers WHERE id=@id;", new { id });
    }
}
