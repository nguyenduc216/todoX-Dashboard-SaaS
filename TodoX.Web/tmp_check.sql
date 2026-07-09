SELECT conname, pg_get_constraintdef(oid) AS def
FROM pg_constraint
WHERE conname = 'chk_todox_ai_character_render_status';
