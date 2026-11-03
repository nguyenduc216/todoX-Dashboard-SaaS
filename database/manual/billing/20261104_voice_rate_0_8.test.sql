-- Verifies 20261104_voice_rate_0_8.sql: every active voice tier costs exactly 0.8 and nothing else changed shape.
DO $$
DECLARE bad int; total int;
BEGIN
    SELECT count(*) FILTER (WHERE rate <> 0.8), count(*) INTO bad, total
      FROM billing.point_rate_config
     WHERE lower(resource_type) = 'voice' AND is_active = true;
    IF total = 0 THEN
        RAISE EXCEPTION 'voice rate test: no active voice rate rows';
    END IF;
    IF bad > 0 THEN
        RAISE EXCEPTION 'voice rate test: % voice tiers are not 0.8', bad;
    END IF;
    IF EXISTS (SELECT 1 FROM billing.point_rate_config WHERE lower(resource_type) <> 'voice' AND rate = 0.8 AND updated_at > now() - interval '1 minute' AND description LIKE '%0.8 từ 2026-11-04%') THEN
        RAISE EXCEPTION 'voice rate test: a non-voice rate was touched';
    END IF;
    RAISE NOTICE 'voice rate test: OK (% tiers at 0.8)', total;
END $$;
