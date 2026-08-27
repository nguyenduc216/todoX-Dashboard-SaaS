-- Manual, idempotent customer catalog update for the Timelapse service split.
-- Database: todo_saas. Do not execute automatically.
-- The profile-category audit must be run in the automation database before applying.

BEGIN;

WITH timelapse_services(service_code, service_name, short_description, description, sort_order) AS (
    VALUES
        ('TIMELAPSE_CONSTRUCTION', 'Timelapse Xây dựng công trình', 'Tạo video Timelapse cho công trình xây dựng.', 'Tạo video Timelapse theo tiến độ thi công công trình xây dựng.', 11),
        ('TIMELAPSE_LIVING_ROOM', 'Timelapse Phòng khách', 'Tạo video Timelapse cho không gian phòng khách.', 'Tạo video Timelapse theo tiến độ hoàn thiện phòng khách.', 12),
        ('TIMELAPSE_BEDROOM', 'Timelapse Phòng ngủ', 'Tạo video Timelapse cho không gian phòng ngủ.', 'Tạo video Timelapse theo tiến độ hoàn thiện phòng ngủ.', 13),
        ('TIMELAPSE_KITCHEN', 'Timelapse Nhà bếp', 'Tạo video Timelapse cho không gian nhà bếp.', 'Tạo video Timelapse theo tiến độ hoàn thiện nhà bếp.', 14),
        ('TIMELAPSE_POOL', 'Timelapse Hồ bơi', 'Tạo video Timelapse cho khu vực hồ bơi.', 'Tạo video Timelapse theo tiến độ thi công hồ bơi.', 15),
        ('TIMELAPSE_INFRASTRUCTURE', 'Timelapse Cầu đường / Hạ tầng', 'Tạo video Timelapse cho cầu đường và hạ tầng.', 'Tạo video Timelapse theo tiến độ thi công cầu đường và hạ tầng.', 16),
        ('TIMELAPSE_LANDSCAPE', 'Timelapse Cảnh quan / Sân vườn / Cây xanh', 'Tạo video Timelapse cho cảnh quan và sân vườn.', 'Tạo video Timelapse theo tiến độ thi công, lắp đặt hoặc phát triển cảnh quan.', 17)
)
INSERT INTO catalog.services (
    id, service_code, service_name, service_type, description, short_description,
    status, sort_order, created_at, updated_at)
SELECT
    COALESCE(existing.id, gen_random_uuid()),
    seed.service_code,
    seed.service_name,
    'timelapse',
    seed.description,
    seed.short_description,
    'active',
    seed.sort_order,
    COALESCE(existing.created_at, now()),
    now()
FROM timelapse_services seed
LEFT JOIN catalog.services existing
  ON lower(existing.service_code) = lower(seed.service_code)
ON CONFLICT (service_code) DO UPDATE SET
    service_name = EXCLUDED.service_name,
    service_type = EXCLUDED.service_type,
    description = EXCLUDED.description,
    short_description = EXCLUDED.short_description,
    status = 'active',
    sort_order = EXCLUDED.sort_order,
    updated_at = now();

WITH service_ids AS (
    SELECT id
      FROM catalog.services
     WHERE service_code IN (
        'TIMELAPSE_CONSTRUCTION',
        'TIMELAPSE_LIVING_ROOM',
        'TIMELAPSE_BEDROOM',
        'TIMELAPSE_KITCHEN',
        'TIMELAPSE_POOL',
        'TIMELAPSE_INFRASTRUCTURE',
        'TIMELAPSE_LANDSCAPE')
),
price_seed AS (
    SELECT *
      FROM (VALUES
        ('video_scene', 'standard', 6::numeric, 10::numeric, '10 điểm / scene 6 giây', 40),
        ('video_scene', 'premium', 6::numeric, 15::numeric, '15 điểm / scene 6 giây', 70)
      ) AS p(asset_type, quality_tier, duration_seconds, sell_points, display_label, sort_order)
)
INSERT INTO catalog.service_sell_prices (
    service_id, asset_type, quality_tier, duration_seconds, sell_points, display_label,
    is_active, sort_order, created_at, updated_at)
SELECT
    service_ids.id,
    price_seed.asset_type,
    price_seed.quality_tier,
    price_seed.duration_seconds,
    price_seed.sell_points,
    price_seed.display_label,
    true,
    price_seed.sort_order,
    now(),
    now()
FROM service_ids
CROSS JOIN price_seed
ON CONFLICT (service_id, asset_type, quality_tier, (COALESCE(duration_seconds, 0)))
DO UPDATE SET
    display_label = EXCLUDED.display_label,
    is_active = true,
    sort_order = EXCLUDED.sort_order,
    updated_at = now();

UPDATE catalog.services
   SET status = 'inactive',
       updated_at = now()
 WHERE lower(service_code) = 'construction_video';

COMMIT;

-- Profile audit to run against the automation database before application:
-- SELECT select_no, profile_code, profile_name, category, enabled
--   FROM public.todox_timelapse_prompt_profiles
--  WHERE enabled = true
--  ORDER BY select_no, profile_code;

-- Verify the customer-facing service split in todo_saas:
-- SELECT service_code, service_name, service_type, status, sort_order
--   FROM catalog.services
--  WHERE service_code LIKE 'TIMELAPSE_%' OR service_code = 'CONSTRUCTION_VIDEO'
--  ORDER BY sort_order, service_code;
