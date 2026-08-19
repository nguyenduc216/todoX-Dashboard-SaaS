# RVIDEO 79AI Image Provider Report

Date: 2026-08-19
Branch: `integration/rdance-on-construction-video-core`

## Git

- Starting SHA: `58f6e6b114e766652240b8b32b1cae7c9c9820c4`
- Implementation commit: `6252545` (`fix(rvideo): persist provider polling across workers`)
- Report commit/final SHA: recorded after the report commit.
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
- Image polling is persisted: submit once, save `id_base`, then poll `/image` once per worker pass.
- Pending and transient poll states requeue the same render job and retain the same task ID.
- RVIDEO video polling was also changed from an in-memory `Task.Delay` loop to one persisted submit/poll pass in `SceneVideoWorkerHandler`.
- Poll cadence remains configurable; no pending age timeout causes fallback.
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
Pending statuses never trigger fallback. Terminal attempts advance the configured policy only after the task ID is cleared, so the next model submits a new provider task. `/images` recovery requires an exact `id_base` match.

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

## Reference Image and Billing

- RVIDEO references are resolved through `IMediaFileService` and converted to JPEG/PNG/WebP data URLs.
- `editImage=true` is sent only with a valid `base64Image`; unavailable requested references fail before provider submission with `RVIDEO_REFERENCE_IMAGE_UNAVAILABLE`.
- Polling and `/images` recovery do not charge. Existing Phase 1 customer image billing remains deferred/zero; provider response metadata is retained for reconciliation without inventing a price.
- Completion uses guarded version updates to prevent stale attempts replacing a newer selected version.

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
- `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --no-restore`: 45 passed, 0 failed
- Focused hotfix/regression tests: 66 passed, 0 failed
- Full `TodoX.Web.Tests`: 665 passed, 0 failed
- `git diff --check`
- `dotnet publish TodoX.Web/TodoX.Web.csproj --no-restore -c Release -o artifacts/publish/todox-dashboard`

The previous three failures were resolved: the RDance assertion now scopes the correct client registration; the approved RVIDEO UI has five tabs; and the approved RVIDEO route is `/jobs/rvideo/new`.

## Compatibility

Timelapse, RDance, YEScale, OpenRouter, Telegram/n8n contracts, and existing billing/router architecture were preserved. TTS, Music, and Finalizer Phase 2 were not started. No live 79AI request was made because production credentials and provider seed are environment/database concerns.

## Sanitized Trace

`RVIDEO scene -> POST /generateImage -> persist id_base -> worker returns -> later POST /image once -> PENDING requeue or SUCCESS -> result URL persisted through TodoX media/versioning`

No access token is included in this report.
