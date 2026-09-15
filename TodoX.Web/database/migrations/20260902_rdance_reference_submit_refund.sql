BEGIN;

DO $$
BEGIN
    IF to_regclass('dance_sell.dance_sell_provider_operations') IS NULL
       OR to_regclass('dance_sell.dance_sell_jobs') IS NULL
       OR to_regclass('billing.token_wallets') IS NULL
       OR to_regclass('billing.token_transactions') IS NULL THEN
        RAISE EXCEPTION 'Required rDance/billing tables are missing.';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION dance_sell.refund_reference_submit_failure()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_tenant_id uuid;
    v_customer_id uuid;
    v_user_id uuid;
    v_wallet_id uuid;
    v_before numeric;
    v_after numeric;
    v_amount numeric;
    v_already_refunded boolean;
BEGIN
    -- Compensate only a reference-image charge that failed before a provider task id
    -- was obtained. Poll/callback/provider failures after submission are intentionally
    -- excluded because provider_task_id is already present and SYSTEM_RETRY must not
    -- create another customer debit/refund cycle.
    IF NEW.operation_type <> 'reference_image'
       OR NEW.status <> 'failed'
       OR OLD.status = 'failed'
       OR NEW.provider_task_id IS NOT NULL
       OR COALESCE(NEW.todox_points_charged, 0) <= 0
       OR COALESCE(NEW.todox_points_refunded, 0) > 0
       OR COALESCE(NEW.billing_status, '') <> 'charged' THEN
        RETURN NEW;
    END IF;

    SELECT j.tenant_id, j.customer_id, j.user_id
      INTO v_tenant_id, v_customer_id, v_user_id
      FROM dance_sell.dance_sell_jobs j
     WHERE j.id = NEW.dance_sell_job_id;

    IF v_customer_id IS NULL OR v_tenant_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT w.id, w.balance
      INTO v_wallet_id, v_before
      FROM billing.token_wallets w
     WHERE w.tenant_id = v_tenant_id
       AND w.customer_id = v_customer_id
     FOR UPDATE;

    IF v_wallet_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM billing.token_transactions t
         WHERE t.tenant_id = v_tenant_id
           AND t.wallet_id = v_wallet_id
           AND t.transaction_type = 'refund'
           AND t.reference_type = 'dance_sell_reference_submit_refund'
           AND t.reference_id = NEW.id
    ) INTO v_already_refunded;

    IF v_already_refunded THEN
        NEW.todox_points_refunded := GREATEST(COALESCE(NEW.todox_points_refunded, 0), COALESCE(NEW.todox_points_charged, 0));
        NEW.refund_status := 'refunded';
        NEW.billing_status := 'refunded';
        NEW.refunded_at := COALESCE(NEW.refunded_at, now());
        RETURN NEW;
    END IF;

    v_amount := COALESCE(NEW.todox_points_charged, 0);
    v_after := v_before + v_amount;

    UPDATE billing.token_wallets
       SET balance = v_after,
           updated_at = now()
     WHERE id = v_wallet_id;

    INSERT INTO billing.token_transactions
        (id, tenant_id, wallet_id, transaction_type, amount,
         balance_before, balance_after, reference_type, reference_id,
         description, created_at, created_by)
    VALUES
        (gen_random_uuid(), v_tenant_id, v_wallet_id, 'refund', v_amount,
         v_before, v_after, 'dance_sell_reference_submit_refund', NEW.id,
         'Refund rDance reference image charge because provider submit failed before task id was obtained.',
         now(), v_user_id);

    NEW.todox_points_refunded := v_amount;
    NEW.refund_status := 'refunded';
    NEW.billing_status := 'refunded';
    NEW.balance_after := v_after;
    NEW.refunded_at := now();

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_refund_reference_submit_failure
    ON dance_sell.dance_sell_provider_operations;

CREATE TRIGGER trg_refund_reference_submit_failure
BEFORE UPDATE OF status, provider_task_id, billing_status
ON dance_sell.dance_sell_provider_operations
FOR EACH ROW
EXECUTE FUNCTION dance_sell.refund_reference_submit_failure();

COMMIT;
