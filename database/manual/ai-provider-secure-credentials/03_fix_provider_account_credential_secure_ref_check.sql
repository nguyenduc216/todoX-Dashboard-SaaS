ALTER TABLE public.todox_ai_provider_account_credential
DROP CONSTRAINT IF EXISTS todox_ai_provider_account_credential_ref_ck;

ALTER TABLE public.todox_ai_provider_account_credential
ADD CONSTRAINT todox_ai_provider_account_credential_ref_ck
CHECK(
    credential_id IS NOT NULL
    OR credential_key IS NOT NULL
    OR credential_config_name IS NOT NULL
    OR secure_credential_id IS NOT NULL
);
