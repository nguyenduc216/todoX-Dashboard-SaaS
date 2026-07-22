# Task 3 Runtime Impact

The malformed Task 2 impact report was replaced by a concrete runtime impact report.

Fixed runtime replacements:
- Provider route lookup now uses `public.todox_ai_provider_capability` and provider account metadata instead of `public.todox_ai_feature_provider_route`.
- Dance Sell operation logging now adapts to `render.render_jobs`.
- Dance Sell operation assets now use `render.render_artifacts`.
- Image billing runtime SQL no longer references image-only billing table names.
- Render snapshot helper no longer writes to dropped `render.render_job_snapshots`; it appends a render job event.

Output CSV:
- `docs/database-refactor/task-3-runtime-impact.csv`
