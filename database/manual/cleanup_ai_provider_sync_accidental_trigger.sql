-- Run only after the canonical sync persistence code is deployed
-- and a manual provider sync succeeds.
ALTER TABLE public.todox_ai_provider_sync
DROP COLUMN IF EXISTS "trigger";
