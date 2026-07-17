# AI Provider Scene Internal Provider SQL

Manual SQL only. Do not run from the application or as a migration.

Run order for the owner/DBA:

1. `00_preflight_scene_internal_provider.sql`
2. `01_seed_image_ai_creative_render_scene.sql`
3. `02_verify_scene_internal_provider.sql`

Rollback:

- `rollback_scene_internal_provider.sql`

What this prepares:

- Provider `image_ai_creative_render` when neither `image_ai_creative_render` nor `todox_image` exists.
- Capability row `scene_image_generation` / `internal_default`.
- `unit_cost_points = 3` and `unit_type = image`, matching the current internal ImageAICreativeRender convention.
- `is_default = false`; admins choose the default through AI Providers quick defaults.

YEScale video models are intentionally not seeded here. The current Codex session has no YEScale MCP tools, so model IDs, payload schema, pricing, and limits for `grok-video`, `grok-video-1.5`, and `omni-flash` were not verified as required by `AGENTS.md`.
