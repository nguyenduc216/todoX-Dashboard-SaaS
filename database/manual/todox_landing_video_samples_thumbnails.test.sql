-- Verifies todox_landing_video_samples_thumbnails.sql. Raises (and so rolls back) when anything is missing.

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(c, ', ') INTO missing
      FROM unnest(ARRAY['thumbnail_portrait_url', 'thumbnail_landscape_url', 'share_image_url']) AS c
     WHERE NOT EXISTS (
           SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'landing' AND table_name = 'video_samples'
              AND column_name = c AND data_type = 'text' AND is_nullable = 'YES');
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'video sample thumbnails test: missing nullable text columns: %', missing;
    END IF;

    -- Existing rows must still have their main poster (the script must not touch data).
    IF EXISTS (SELECT 1 FROM landing.video_samples WHERE poster_url IS NULL OR poster_url = '') THEN
        RAISE EXCEPTION 'video sample thumbnails test: a sample lost its poster_url';
    END IF;
END $$;
