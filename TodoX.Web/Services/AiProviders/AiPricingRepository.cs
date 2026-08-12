using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiPricingRepository
{
    private readonly TodoXConnectionFactory _factory;

    public AiPricingRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<AiPricingPolicyDto>> GetPoliciesAsync(long providerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiPricingPolicyDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, policy_code AS PolicyCode, policy_name AS PolicyName,
                   provider_credit_per_internal_point AS ProviderCreditPerInternalPoint,
                   internal_point_value_vnd AS InternalPointValueVnd,
                   default_markup_percent AS DefaultMarkupPercent,
                   minimum_sell_points AS MinimumSellPoints, rounding_rule AS RoundingRule,
                   allow_auto_sell_update AS AllowAutoSellUpdate, enabled AS Enabled, is_default AS IsDefault
              FROM public.todox_ai_pricing_policy
             WHERE provider_id = @providerId
             ORDER BY is_default DESC, policy_name;
            """, new { providerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiModelPriceDto>> GetPricesAsync(long modelId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiModelPriceDto>(
            """
            SELECT id AS Id, model_id AS ModelId, mode AS Mode, resolution AS Resolution,
                   duration_seconds AS DurationSeconds, ratio AS Ratio, rate_type AS RateType,
                   unit_type AS UnitType, provider_price AS ProviderPrice,
                   provider_price_default AS ProviderPriceDefault, provider_price_unit AS ProviderPriceUnit,
                   internal_cost_points AS InternalCostPoints, sell_points AS SellPoints,
                   sell_price_mode AS SellPriceMode, markup_percent AS MarkupPercent,
                   minimum_points AS MinimumPoints, rounding_rule AS RoundingRule,
                   price_source AS PriceSource, effective_from AS EffectiveFrom,
                   effective_to AS EffectiveTo, active AS Active
              FROM public.todox_ai_model_price
             WHERE model_id = @modelId
             ORDER BY mode, resolution, duration_seconds, ratio, effective_from DESC;
            """, new { modelId });
        return rows.ToList();
    }

    public async Task UpsertPolicyAsync(AiPricingPolicyDto policy, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO public.todox_ai_pricing_policy
                (provider_id, policy_code, policy_name, provider_credit_per_internal_point, internal_point_value_vnd,
                 default_markup_percent, minimum_sell_points, rounding_rule, allow_auto_sell_update, enabled, is_default,
                 created_by, updated_by, created_at, updated_at)
            VALUES
                (@ProviderId, @PolicyCode, @PolicyName, @ProviderCreditPerInternalPoint, @InternalPointValueVnd,
                 @DefaultMarkupPercent, @MinimumSellPoints, @RoundingRule, @AllowAutoSellUpdate, @Enabled, @IsDefault,
                 @userId, @userId, now(), now())
            ON CONFLICT (provider_id, policy_code)
            DO UPDATE SET
                policy_name = EXCLUDED.policy_name,
                provider_credit_per_internal_point = EXCLUDED.provider_credit_per_internal_point,
                internal_point_value_vnd = EXCLUDED.internal_point_value_vnd,
                default_markup_percent = EXCLUDED.default_markup_percent,
                minimum_sell_points = EXCLUDED.minimum_sell_points,
                rounding_rule = EXCLUDED.rounding_rule,
                allow_auto_sell_update = EXCLUDED.allow_auto_sell_update,
                enabled = EXCLUDED.enabled,
                is_default = EXCLUDED.is_default,
                updated_by = @userId,
                updated_at = now();
            """,
            new
            {
                policy.ProviderId,
                policy.PolicyCode,
                policy.PolicyName,
                policy.ProviderCreditPerInternalPoint,
                policy.InternalPointValueVnd,
                policy.DefaultMarkupPercent,
                policy.MinimumSellPoints,
                policy.RoundingRule,
                policy.AllowAutoSellUpdate,
                policy.Enabled,
                policy.IsDefault,
                userId
            });
    }

    public async Task UpsertPriceAsync(AiModelPriceDto price, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO public.todox_ai_model_price
                (model_id, mode, resolution, duration_seconds, ratio, rate_type, unit_type, provider_price,
                 provider_price_default, provider_price_unit, internal_cost_points, sell_points, sell_price_mode,
                 markup_percent, minimum_points, rounding_rule, price_source, effective_from, effective_to, active,
                 created_by, updated_by, created_at, updated_at)
            VALUES
                (@ModelId, @Mode, @Resolution, @DurationSeconds, @Ratio, @RateType, @UnitType, @ProviderPrice,
                 @ProviderPriceDefault, @ProviderPriceUnit, @InternalCostPoints, @SellPoints, @SellPriceMode,
                 @MarkupPercent, @MinimumPoints, @RoundingRule, @PriceSource, @EffectiveFrom, @EffectiveTo, @Active,
                 @userId, @userId, now(), now())
            ON CONFLICT (
                model_id,
                (COALESCE(mode, ''::character varying)),
                (COALESCE(resolution, ''::character varying)),
                (COALESCE(duration_seconds, (0)::numeric)),
                (COALESCE(ratio, ''::character varying)),
                rate_type,
                unit_type
            )
            WHERE active = true
              AND effective_to IS NULL
            DO UPDATE SET
                rate_type = EXCLUDED.rate_type,
                unit_type = EXCLUDED.unit_type,
                provider_price = EXCLUDED.provider_price,
                provider_price_default = EXCLUDED.provider_price_default,
                provider_price_unit = EXCLUDED.provider_price_unit,
                internal_cost_points = EXCLUDED.internal_cost_points,
                sell_points = EXCLUDED.sell_points,
                sell_price_mode = EXCLUDED.sell_price_mode,
                markup_percent = EXCLUDED.markup_percent,
                minimum_points = EXCLUDED.minimum_points,
                rounding_rule = EXCLUDED.rounding_rule,
                price_source = EXCLUDED.price_source,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                active = EXCLUDED.active,
                updated_by = @userId,
                updated_at = now();
            """,
            new
            {
                price.ModelId,
                price.Mode,
                price.Resolution,
                price.DurationSeconds,
                price.Ratio,
                price.RateType,
                price.UnitType,
                price.ProviderPrice,
                price.ProviderPriceDefault,
                price.ProviderPriceUnit,
                price.InternalCostPoints,
                price.SellPoints,
                price.SellPriceMode,
                price.MarkupPercent,
                price.MinimumPoints,
                price.RoundingRule,
                price.PriceSource,
                price.EffectiveFrom,
                price.EffectiveTo,
                price.Active,
                userId
            });
    }

    public async Task MarkPriceInactiveAsync(long priceId, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_model_price
               SET active = false,
                   effective_to = COALESCE(effective_to, now()),
                   updated_by = @userId,
                   updated_at = now()
             WHERE id = @priceId;
            """,
            new { priceId, userId });
    }
}
