# AI Studio Voice/Music Catalog Progress Report

Date: 2026-08-17
Branch: `integration/rdance-on-construction-video-core`

## 1. Code đã thay đổi

`TodoX.Web/Models/AiStudioCatalogModels.cs`
→ Thêm DTO, filter, upload result, validation rules cho Voice Library và Music Library; giữ mapping compatibility alias Rvideo `a1/a2/a3/a4` dạng data contract.

`TodoX.Web/Services/AiStudioCatalogService.cs`
→ Thêm service Dapper cho CRUD voice/music, filter listing, runtime active-only lookup, soft-disable, transaction khi set default, upload audio vào media storage hiện có.

`TodoX.Web/Services/AiStudioCatalogEndpoints.cs`
→ Thêm admin API `/api/admin/ai-studio/voices`, `/api/admin/ai-studio/music`, upload endpoints, và runtime API `/api/ai-studio/voices`, `/api/ai-studio/music`.

`TodoX.Web/Program.cs`
→ Đăng ký `IAiStudioCatalogService` và map endpoint AI Studio catalog.

`TodoX.Web/Components/Layout/MainLayout.razor`
→ Bổ sung menu AI Studio → `Giọng đọc` và `Nhạc nền` bằng supplemental menu, chỉ hiện theo admin/system operator/root visibility hiện có.

`TodoX.Web/Components/Pages/AiStudioVoices.razor`
→ Thêm trang quản trị Voice Library: search/filter, list, form create/update, active/default, upload preview MP3, audio preview.

`TodoX.Web/Components/Pages/AiStudioMusic.razor`
→ Thêm trang quản trị Music Library: search/filter, list, form create/update, active/default, upload MP3/WAV/M4A, audio preview, volume 0-1.

`database/migrations/20260817_ai_studio_voice_music_catalog.sql`
→ Thêm SQL script idempotent tạo bảng/index/constraint/seed tối thiểu. Script chưa được chạy vào database.

`TodoX.Web.Tests/AiStudioCatalogTests.cs`
→ Thêm regression/source-contract tests cho validation, route/menu/API, default uniqueness transaction source, audio upload rules, runtime active-only contract.

## 2. Database

Table mới:
`public.ai_studio_voices`, `public.ai_studio_music`.

Voice columns:
`id`, `name`, `code`, `provider_code`, `provider_voice_id`, `compatibility_alias`, `gender`, `language_code`, `region`, `description`, `preview_file_name`, `preview_storage_key`, `preview_file_url`, `default_rate`, `min_rate`, `max_rate`, `is_active`, `is_default`, `sort_order`, `created_at`, `created_by`, `updated_at`, `updated_by`.

Music columns:
`id`, `name`, `code`, `description`, `category`, `file_name`, `storage_key`, `file_url`, `duration_seconds`, `mime_type`, `file_size`, `default_volume`, `loop_allowed`, `is_active`, `is_default`, `sort_order`, `created_at`, `created_by`, `updated_at`, `updated_by`.

Indexes/constraints:
unique lower-code indexes, active/sort listing indexes, provider/category indexes, partial unique active-default indexes, rate/volume/file-size/duration checks.

Seed:
`custom` voice for compatibility alias `a4`; `default_music` placeholder default music row.

Migration:
`database/migrations/20260817_ai_studio_voice_music_catalog.sql`.

## 3. Voice seed

| alias cũ | code mới | provider | provider_voice_id |
|---|---|---|---|
| a1 | vbee_phuthang | vbee | Chưa xác định được trong code/config hiện tại; không seed, không đoán |
| a2 | vbee_ngochuyen | vbee | Chưa xác định được trong code/config hiện tại; không seed, không đoán |
| a3 | vbee_minhduc | vbee | Chưa xác định được trong code/config hiện tại; không seed, không đoán |
| a4 | custom | custom | NULL |

## 4. API

Admin:
`GET /api/admin/ai-studio/voices`
`GET /api/admin/ai-studio/voices/{id}`
`POST /api/admin/ai-studio/voices`
`PUT /api/admin/ai-studio/voices/{id}`
`DELETE /api/admin/ai-studio/voices/{id}`
`POST /api/admin/ai-studio/voices/{id}/preview`
`GET /api/admin/ai-studio/music`
`GET /api/admin/ai-studio/music/{id}`
`POST /api/admin/ai-studio/music`
`PUT /api/admin/ai-studio/music/{id}`
`DELETE /api/admin/ai-studio/music/{id}`
`POST /api/admin/ai-studio/music/{id}/file`

Runtime:
`GET /api/ai-studio/voices`
`GET /api/ai-studio/voices/{code}`
`GET /api/ai-studio/music`
`GET /api/ai-studio/music/{code}`

## 5. UI

Routes:
`/admin/ai-studio/voices`
`/admin/ai-studio/music`

Menu:
`AI Studio → Giọng đọc`
`AI Studio → Nhạc nền`

Pages:
Voice Library page and Music Library page with CRUD form, filters, active/default toggles, upload controls, and `<audio controls>` preview.

## 6. Storage

Storage provider:
Existing TodoX `IMediaFileService` / `media.media_files` local storage path. No new storage mechanism was added.

Path convention:
Voice preview: `ai-studio/voices/{voice-code}/preview-{timestamp}-{guid}.mp3`
Music file: `ai-studio/music/{music-code}/{timestamp}-{guid}.{ext}`

URL strategy:
Use `media.media_files.public_url`/`file_url`, served from the existing `/uploads` public static-file convention.

## 7. Tests

Passed:
`dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false --filter AiStudioCatalogTests`
→ Passed 3/3.

Passed:
`dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false --no-build`
→ Passed 621/621.

Lint:
`dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --verbosity minimal`
→ Failed on pre-existing whitespace formatting in unrelated files: `AccountRepository.cs`, `AuditRepository.cs`, `ChibiAvatarService.Generate.cs`, `PromptTemplateRepository.cs`, `SettingsApiRepository.cs`, `SocialPageRepository.cs`, `WalletService.cs`.

Passed:
`git diff --check`
→ Passed; Git printed line-ending normalization warnings only.

## 8. Build

Build command:
`dotnet build ..\TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false`

Result:
Passed, 0 warnings, 0 errors.

Publish command:
`dotnet publish .\TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard /p:UseSharedCompilation=false`

Publish output directory:
`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

Result:
Passed.

## 9. Git

Branch:
`integration/rdance-on-construction-video-core`

Commit SHA 1:
`2db40ea56de38dbf5e0a49e179c57b2e8996b656`

Commit SHA 2:
`eda10b766876273ce85c16984be60268c91c2945`

## 10. Các việc cố ý chưa làm

Rvideo chưa chuyển sang catalog.
n8n chưa thay đổi.
Snapshot job chưa triển khai.
RDance chưa thay đổi.
Timelapse chưa thay đổi.
Không chạy migration vào database.
Không deploy hoặc restart production services.
