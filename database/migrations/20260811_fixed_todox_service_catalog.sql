-- Phase 1 fixed todoX service catalog.
-- Safe to run multiple times. Preserves existing catalog.services IDs and unrelated services.

ALTER TABLE catalog.services
    ADD COLUMN IF NOT EXISTS workflow_code text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_catalog_services_service_code
    ON catalog.services (service_code);

WITH fixed_services AS (
    SELECT *
    FROM (VALUES
        (
            'TIMELAPSE',
            'Video Timelapse AI',
            'timelapse',
            'CONSTRUCTION_TIMELAPSE',
            'Tạo video mô phỏng quá trình xây dựng, thi công và hoàn thiện từ một ảnh thành phẩm.',
            'enabled',
            10
        ),
        (
            'RVIDEO',
            'Render Video AI',
            'rvideo',
            'TODOX_RENDERVIDEO',
            'Tạo video theo scene từ hình ảnh và prompt, hỗ trợ nội dung, giọng đọc và nhạc nền.',
            'enabled',
            20
        ),
        (
            'RDANCE',
            'R Dance AI',
            'rdance',
            'RDANCE_79AI',
            'Tạo video chuyển động theo video mẫu bằng AI Motion Control.',
            'enabled',
            30
        )
    ) AS v(service_code, service_name, service_type, workflow_code, short_description, status, sort_order)
)
INSERT INTO catalog.services (
    id,
    service_code,
    service_name,
    service_type,
    workflow_code,
    description,
    short_description,
    status,
    sort_order,
    created_at,
    updated_at
)
SELECT
    gen_random_uuid(),
    service_code,
    service_name,
    service_type,
    workflow_code,
    short_description,
    short_description,
    status,
    sort_order,
    now(),
    now()
FROM fixed_services
ON CONFLICT (service_code)
DO UPDATE SET
    service_name = EXCLUDED.service_name,
    service_type = EXCLUDED.service_type,
    workflow_code = EXCLUDED.workflow_code,
    description = EXCLUDED.description,
    short_description = EXCLUDED.short_description,
    status = EXCLUDED.status,
    sort_order = EXCLUDED.sort_order,
    updated_at = now();

-- Verification: expected TIMELAPSE, RVIDEO, RDANCE with enabled status and sort orders 10, 20, 30.
SELECT service_code, service_name, status, sort_order
FROM catalog.services
WHERE service_code IN ('TIMELAPSE', 'RVIDEO', 'RDANCE')
ORDER BY sort_order;
