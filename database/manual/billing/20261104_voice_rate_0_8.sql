-- Owner decision 2026-11-04: one shared voice price for every TodoX service (incl. EditVideo),
-- standard and premium alike: 0.8 point (= 800 VND) per voice render. Data-only, idempotent.
UPDATE billing.point_rate_config
   SET rate = 0.8,
       updated_at = now(),
       description = COALESCE(description, '') || CASE WHEN description LIKE '%2026-11-04%' THEN '' ELSE ' (0.8 từ 2026-11-04)' END
 WHERE lower(resource_type) = 'voice'
   AND lower(quality_tier) IN ('standard', 'premium')
   AND is_active = true
   AND rate <> 0.8;
