-- ============================================================
-- V009_Chibi_Generation_Uniform_Reference.sql
-- Persist uniform/clothing reference media for chibi generations.
-- Additive and idempotent: enables rerender to reuse the same
-- uploaded uniform reference image.
-- ============================================================

ALTER TABLE auth.user_chibi_generations
    ADD COLUMN IF NOT EXISTS reference_uniform_media_id uuid REFERENCES media.media_files(id);

CREATE INDEX IF NOT EXISTS idx_user_chibi_generations_uniform_ref
    ON auth.user_chibi_generations(reference_uniform_media_id)
    WHERE reference_uniform_media_id IS NOT NULL;

COMMENT ON COLUMN auth.user_chibi_generations.reference_uniform_media_id IS
    'Optional uniform/clothing reference media used for Gemini inlineData reference rendering and rerender reuse.';

INSERT INTO system.foundation_versions (version_code, script_name, status, message)
SELECT 'V009', 'V009_Chibi_Generation_Uniform_Reference.sql', 'success', 'Persist chibi uniform reference media'
WHERE NOT EXISTS (SELECT 1 FROM system.foundation_versions WHERE version_code = 'V009');
