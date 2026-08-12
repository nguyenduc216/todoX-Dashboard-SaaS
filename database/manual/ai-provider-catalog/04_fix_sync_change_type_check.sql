-- Restore the complete sync change_type contract.
-- Safe to re-run on an existing environment and preserves legacy values.

ALTER TABLE public.todox_ai_provider_sync_change
    DROP CONSTRAINT IF EXISTS ck_todox_ai_provider_sync_change_type;

ALTER TABLE public.todox_ai_provider_sync_change
    ADD CONSTRAINT ck_todox_ai_provider_sync_change_type
    CHECK (change_type IN (
        'insert',
        'update',
        'status_change',
        'price_change',
        'disable',
        'enable',
        'no_change',
        'MODEL_ADDED',
        'MODEL_UPDATED',
        'MODEL_STATUS_CHANGED',
        'MODE_ADDED',
        'DURATION_ADDED',
        'DURATION_REMOVED',
        'RESOLUTION_ADDED',
        'PRICE_ADDED',
        'PRICE_CHANGED',
        'PRICE_DISABLED'
    ));
