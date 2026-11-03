# PROMPT-ASSISTANT-CREATIVE-MODE

Incident 6ab614f2-160e-4e0c-af38-6c2699de42b9. Checkpoint A1 remains frozen.

CreativeMode is stored per project in `video_render.rvideo_job_settings`. Nullable request values preserve prior choices for legacy writers. UI discards a stale settings response after a project switch.

The SQL files `database/DRAFT_prompt_assistant_creative_mode_forward.sql` and `database/DRAFT_prompt_assistant_creative_mode_rollback.sql` are operator-review drafts, not executed. Forward adds a non-null boolean default false for existing projects; rollback loses saved choices. The operator must confirm the deployed schema and manually apply the forward SQL before deploying code relying on the new column.

Local tests do not establish PostgreSQL persistence or Dashboard behavior. No production database, provider, publish or deploy was used. Manual project A/B switching, save/reopen and creative/verbatim Dashboard checks are pending.