-- Review and execute manually before deploying the matching application build.
-- This script only adds nullable Prompt Assistant diagnostic columns.
ALTER TABLE settings.service_prompt_generations
    ADD COLUMN IF NOT EXISTS assembled_content_sanitized text,
    ADD COLUMN IF NOT EXISTS http_content_type text,
    ADD COLUMN IF NOT EXISTS sse_data_count integer,
    ADD COLUMN IF NOT EXISTS sse_done_received boolean,
    ADD COLUMN IF NOT EXISTS raw_response_captured_length integer,
    ADD COLUMN IF NOT EXISTS raw_response_truncated boolean,
    ADD COLUMN IF NOT EXISTS assembled_content_length integer,
    ADD COLUMN IF NOT EXISTS parser_error_message text,
    ADD COLUMN IF NOT EXISTS parser_error_line_number bigint,
    ADD COLUMN IF NOT EXISTS parser_error_byte_position_in_line bigint;

DO $$
DECLARE
    missing_or_invalid text;
BEGIN
    WITH expected(column_name, data_type, is_nullable) AS
    (
        VALUES
            ('assembled_content_sanitized', 'text', 'YES'),
            ('http_content_type', 'text', 'YES'),
            ('sse_data_count', 'integer', 'YES'),
            ('sse_done_received', 'boolean', 'YES'),
            ('raw_response_captured_length', 'integer', 'YES'),
            ('raw_response_truncated', 'boolean', 'YES'),
            ('assembled_content_length', 'integer', 'YES'),
            ('parser_error_message', 'text', 'YES'),
            ('parser_error_line_number', 'bigint', 'YES'),
            ('parser_error_byte_position_in_line', 'bigint', 'YES')
    )
    SELECT string_agg(
               e.column_name || ' expected ' || e.data_type || '/' || e.is_nullable
               || ' but found ' || COALESCE(c.data_type, 'missing') || '/' || COALESCE(c.is_nullable, 'missing'),
               ', ' ORDER BY e.column_name)
      INTO missing_or_invalid
      FROM expected e
      LEFT JOIN information_schema.columns c
        ON c.table_schema = 'settings'
       AND c.table_name = 'service_prompt_generations'
       AND c.column_name = e.column_name
     WHERE c.column_name IS NULL
        OR c.data_type <> e.data_type
        OR c.is_nullable <> e.is_nullable;

    IF missing_or_invalid IS NOT NULL THEN
        RAISE EXCEPTION 'Prompt Assistant diagnostic verification failed: %', missing_or_invalid;
    END IF;
END $$;
