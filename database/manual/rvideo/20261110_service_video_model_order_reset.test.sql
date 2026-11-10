DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM catalog.services
         WHERE jsonb_typeof(default_options->'job_defaults') = 'object'
           AND ((default_options->'job_defaults') ? 'videoModelOrder'
                OR (default_options->'job_defaults') ? 'VideoModelOrder')
    ) THEN
        RAISE EXCEPTION 'a service still overrides the scene-video model order';
    END IF;
END $$;
