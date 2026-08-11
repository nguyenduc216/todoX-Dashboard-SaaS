-- Verify 79AI model duration/mode/resolution pricing after a catalog sync.
-- Safe read-only query.

SELECT
    p.provider_code AS provider,
    m.display_name AS model,
    pr.mode,
    pr.duration_seconds,
    pr.resolution,
    pr.ratio,
    pr.provider_price,
    pr.provider_price_default,
    pr.provider_price_unit,
    pr.internal_cost_points,
    pr.sell_points,
    pr.sell_price_mode,
    pr.active
FROM public.todox_ai_provider p
JOIN public.todox_ai_provider_model m
  ON m.provider_id = p.id
LEFT JOIN public.todox_ai_model_price pr
  ON pr.model_id = m.id
WHERE p.provider_code = '79ai'
ORDER BY
    m.media_type,
    m.display_name,
    pr.mode NULLS FIRST,
    pr.resolution NULLS FIRST,
    pr.duration_seconds NULLS FIRST,
    pr.ratio NULLS FIRST;
