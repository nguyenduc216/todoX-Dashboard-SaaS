BEGIN;

CREATE SCHEMA IF NOT EXISTS billing;

CREATE TABLE IF NOT EXISTS billing.point_rate_config (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    resource_type varchar NOT NULL,
    quality_tier varchar NOT NULL,
    rate numeric NOT NULL,
    unit varchar NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    description text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    created_by uuid NULL,
    updated_by uuid NULL
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_rate_config_resource_type'
    ) THEN
        ALTER TABLE billing.point_rate_config
            ADD CONSTRAINT chk_point_rate_config_resource_type CHECK (lower(resource_type) IN ('image', 'video', 'voice'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_rate_config_quality_tier'
    ) THEN
        ALTER TABLE billing.point_rate_config
            ADD CONSTRAINT chk_point_rate_config_quality_tier CHECK (lower(quality_tier) IN ('standard', 'premium'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_rate_config_rate_nonnegative'
    ) THEN
        ALTER TABLE billing.point_rate_config
            ADD CONSTRAINT chk_point_rate_config_rate_nonnegative CHECK (rate >= 0);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_rate_config_resource_unit'
    ) THEN
        ALTER TABLE billing.point_rate_config
            ADD CONSTRAINT chk_point_rate_config_resource_unit CHECK (
                (lower(resource_type) = 'video' AND lower(unit) = 'per_second') OR
                (lower(resource_type) = 'image' AND lower(unit) = 'per_render') OR
                (lower(resource_type) = 'voice' AND lower(unit) = 'per_render')
            );
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_point_rate_config_tenant_resource_quality
    ON billing.point_rate_config (tenant_id, resource_type, quality_tier);

CREATE TABLE IF NOT EXISTS billing.service_point_rate_override (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    service_id uuid NOT NULL,
    resource_type varchar NOT NULL,
    quality_tier varchar NOT NULL,
    rate numeric NOT NULL,
    unit varchar NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    created_by uuid NULL,
    updated_by uuid NULL
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_service_point_rate_override_resource_type'
    ) THEN
        ALTER TABLE billing.service_point_rate_override
            ADD CONSTRAINT chk_service_point_rate_override_resource_type CHECK (lower(resource_type) IN ('image', 'video', 'voice'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_service_point_rate_override_quality_tier'
    ) THEN
        ALTER TABLE billing.service_point_rate_override
            ADD CONSTRAINT chk_service_point_rate_override_quality_tier CHECK (lower(quality_tier) IN ('standard', 'premium'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_service_point_rate_override_rate_nonnegative'
    ) THEN
        ALTER TABLE billing.service_point_rate_override
            ADD CONSTRAINT chk_service_point_rate_override_rate_nonnegative CHECK (rate >= 0);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_service_point_rate_override_resource_unit'
    ) THEN
        ALTER TABLE billing.service_point_rate_override
            ADD CONSTRAINT chk_service_point_rate_override_resource_unit CHECK (
                (lower(resource_type) = 'video' AND lower(unit) = 'per_second') OR
                (lower(resource_type) = 'image' AND lower(unit) = 'per_render') OR
                (lower(resource_type) = 'voice' AND lower(unit) = 'per_render')
            );
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_service_point_rate_override_tenant_service_resource_quality
    ON billing.service_point_rate_override (tenant_id, service_id, resource_type, quality_tier);

CREATE TABLE IF NOT EXISTS billing.point_vouchers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    voucher_code varchar NOT NULL,
    point_amount numeric NOT NULL,
    status varchar NOT NULL DEFAULT 'active',
    max_redemptions integer NULL,
    redeemed_count integer NOT NULL DEFAULT 0,
    valid_from timestamptz NULL,
    valid_until timestamptz NULL,
    created_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_vouchers_status'
    ) THEN
        ALTER TABLE billing.point_vouchers
            ADD CONSTRAINT chk_point_vouchers_status CHECK (lower(status) IN ('active', 'inactive', 'disabled'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_vouchers_points_nonnegative'
    ) THEN
        ALTER TABLE billing.point_vouchers
            ADD CONSTRAINT chk_point_vouchers_points_nonnegative CHECK (point_amount >= 0);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_point_vouchers_tenant_code
    ON billing.point_vouchers (tenant_id, upper(voucher_code));

CREATE TABLE IF NOT EXISTS billing.point_voucher_redemptions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    voucher_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    points numeric NOT NULL,
    transaction_id uuid NOT NULL,
    redeemed_at timestamptz NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_point_voucher_redemptions_points_nonnegative'
    ) THEN
        ALTER TABLE billing.point_voucher_redemptions
            ADD CONSTRAINT chk_point_voucher_redemptions_points_nonnegative CHECK (points >= 0);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_point_voucher_redemptions_voucher_customer
    ON billing.point_voucher_redemptions (voucher_id, customer_id);

INSERT INTO billing.point_rate_config (
    id, tenant_id, resource_type, quality_tier, rate, unit, is_active, description, created_at
)
SELECT gen_random_uuid(), tenant_id, resource_type, quality_tier, rate, unit, true, description, now()
FROM (
    SELECT id AS tenant_id, 'image' AS resource_type, 'standard' AS quality_tier, 3000::numeric AS rate, 'per_render' AS unit, 'Image standard' AS description FROM system.tenants
    UNION ALL SELECT id, 'image', 'premium', 5000, 'per_render', 'Image premium' FROM system.tenants
    UNION ALL SELECT id, 'video', 'standard', 1500, 'per_second', 'Video standard' FROM system.tenants
    UNION ALL SELECT id, 'video', 'premium', 2500, 'per_second', 'Video premium' FROM system.tenants
    UNION ALL SELECT id, 'voice', 'standard', 500, 'per_render', 'Voice standard' FROM system.tenants
    UNION ALL SELECT id, 'voice', 'premium', 800, 'per_render', 'Voice premium' FROM system.tenants
) seed
WHERE NOT EXISTS (
    SELECT 1
      FROM billing.point_rate_config cfg
     WHERE cfg.tenant_id = seed.tenant_id
       AND lower(cfg.resource_type) = lower(seed.resource_type)
       AND lower(cfg.quality_tier) = lower(seed.quality_tier)
);

COMMIT;
