# RVIDEO 79AI Image Provider Report

Date: 2026-08-19
Branch: `integration/rdance-on-construction-video-core`

## Git

- Starting SHA: `67f24b4aaecbabf99e60c1cd2ac1e60d0c6409c`
- Implementation commit: `faddd89` (`feat(rvideo): add 79ai image provider and synced render status`)
- Final SHA: updated after this report commit
- No force push and no destructive Git operation was used.

## 79AI Provider

- Provider code: `79ai`
- Factory key: `79ai_task_image`
- Adapter: `Gommo79AiImageService`
- Auth: `IProviderCredentialResolver.ResolveAsync("79ai", "access_token")`
- Base URL: provider/config value, compatibility fallback `https://api.gommo.net/ai`
- Endpoints:
  - `POST /generateImage`
  - `POST /image`
  - `POST /images` only for SUCCESS-without-URL recovery
- Polling defaults: 10 seconds, 18 attempts; both are configurable.
- Secrets are never logged or persisted in diagnostics.

## RVIDEO Routing

- RVIDEO capability: `rvideo_scene_image_generation`
- Existing shared `scene_image_generation` routing was not changed.
- The background worker continues polling server-side; Razor does not call 79AI.
- The Core Job UUID remains the public job identity. `project_id` remains internal.

Default model policy:

1. `google_image_gen_banana_2`, mode `vip`, resolution `1k`
2. `imagegen_2_0`, mode `low_basic`, resolution `1k`
3. `seedream_4_5`, mode `vip`, resolution `2k`

Ratio conversion is provider-specific: `9:16 -> 9_16`, `16:9 -> 16_9`.
Fallback occurs only after terminal failure, invalid final output, or polling timeout. A single task ID is reused for all polls.

## Title and Status

- My Jobs now reads the title from the Core Job input snapshot instead of displaying literal `RVIDEO`.
- Service display uses the Core service catalog when available.
- Save Changes updates the Core Job `input_json` snapshot and the linked project title/prompt/settings.
- Persisted provider events are mapped to:
  - `Đang chờ lượt`
  - `Đã gửi 79AI`
  - `79AI đang tạo ảnh`
  - `Đang tải kết quả`
  - `Hoàn thành`
  - `Lỗi`
- Header includes the job UUID, service, aspect ratio, and persisted stage.

## Database

- SQL file: `database/migrations/20260819_rvideo_79ai_image_provider.sql`
- Migration/database update required: only when the deployment database lacks the provider/capability seed.
- SQL is additive and idempotent.
- SQL was not executed.
- Support diagnostic was extended to include scene image versions and provider usage records while accepting only `:job_uuid`.

## Validation

Passed:

- `dotnet build TodoX.Web/TodoX.Web.csproj --no-restore`
- `dotnet format Dashboard-web/TodoX.Dashboard.csproj --verify-no-changes --no-restore --include ...`
- `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --no-restore`: 45 passed
- Focused provider/catalog tests: 15 passed
- `git diff --check`
- `dotnet publish TodoX.Web/TodoX.Web.csproj --no-restore -c Release -o artifacts/publish/todox-dashboard`

The full `TodoX.Web.Tests` suite ran with 661 passed and 3 legacy expectation failures unrelated to the new adapter tests:

- `RDanceFashionDemoPageTests.DanceSell79AiMotionSubmitUsesRouteFieldsAndProviderMode`
- `RenderVideoJobsLayoutTests.ProjectDialog_KeepsFourTabs`
- `TimelapsePhase2ATests.CustomerServiceRouting_UsesEngineType` for `rvideo`

Focused new coverage passed for aliases, ratio normalization, exact fallback order, same-task polling, URL recovery, and progress events.

## Compatibility

Timelapse, RDance, YEScale, OpenRouter, Telegram/n8n contracts, and existing billing/router architecture were preserved. TTS, Music, and Finalizer Phase 2 were not started. No live 79AI request was made because production credentials and provider seed are environment/database concerns.

## Sanitized Trace

`RVIDEO scene -> POST /generateImage (model=google_image_gen_banana_2, ratio=9_16) -> id_base=<provider-task-id> -> POST /image using the same id_base -> SUCCESS -> result URL persisted through TodoX media/versioning`

No access token is included in this report.
