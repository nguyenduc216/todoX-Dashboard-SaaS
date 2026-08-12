SELECT
    a.provider_code,
    a.account_code,
    a.account_name,
    a.environment,
    a.enabled AS account_enabled,
    a.is_default AS account_default,
    a.health_status,
    m.secure_credential_id,
    m.credential_role,
    m.enabled AS mapping_enabled,
    m.priority,
    s.token_fingerprint,
    s.masked_hint,
    s.status,
    s.encryption_algorithm,
    s.key_version,
    s.valid_from,
    s.expires_at,
    s.last_used_at
FROM public.todox_ai_provider_account a
LEFT JOIN public.todox_ai_provider_account_credential m
    ON m.provider_account_id = a.id
    AND m.credential_role = 'access_token'
LEFT JOIN system.ai_provider_credentials_secure s
    ON s.id = m.secure_credential_id
WHERE a.id = '5ab72966-c0a7-40b0-b8db-c5c85b39e407'::uuid;
