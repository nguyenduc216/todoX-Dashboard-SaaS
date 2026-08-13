# Commercial Video Service Catalog Report

Date: 2026-08-13

## Architecture

TodoX now separates customer-facing commercial services from internal processing engines. Customer catalog rows live in `catalog.services` and use `service_code` as the commercial identity. The internal engine is stored in `service_type` and is limited by `TodoXServiceEngineTypes` to `timelapse`, `rvideo`, and `rdance`.

The fixed legacy 3-service catalog remains only as a compatibility reference for existing legacy records. It is no longer used as the customer-facing authority for `/create`, and admin edits are no longer overwritten by source-controlled fixed definitions.

## Seeded Commercial Services

1. `CONSTRUCTION_VIDEO` - Xây dựng & Công trình - `timelapse`
2. `BUDDHISM_CONTENT_VIDEO` - Phật pháp & Nội dung tu học - `rvideo`
3. `HEALTHCARE_VIDEO` - Sức khỏe - `rvideo`
4. `COSMETICS_VIDEO` - Mỹ phẩm - `rvideo`
5. `FASHION_VIDEO` - Thời trang - `rdance`
6. `FOOD_SNACK_VIDEO` - Ẩm thực & Đồ ăn vặt - `rvideo`
7. `ETHICAL_KNOWLEDGE_VIDEO` - Video kiến thức đạo lý - `rvideo`
8. `REAL_ESTATE_VIDEO` - Bất động sản - `rvideo`
9. `LIVESTREAM_MODEL_VIDEO` - Livestream - Người mẫu - `rdance`
10. `PERSONAL_BRAND_CHANNEL_VIDEO` - Xây kênh nhãn hiệu - `rvideo`

## Thumbnail Mapping

The runtime could not access the 10 uploaded images from the current ChatGPT conversation. No replacement images were generated and no invented URLs were written. A source-controlled thumbnail manifest was added at `TodoX.Web/docs/commercial-thumbnail-manifest.md` for deterministic mapping:

- `nganh-xay-dung`
- `nganh-phat-phap`
- `nganh-suc-khoe`
- `nganh-my-pham`
- `nganh-thoi-trang`
- `nganh-am-thuc-do-an-vat`
- `video-kien-thuc-dao-ly`
- `nganh-bat-dong-san`
- `nganh-livestream-nguoi-mau`
- `xay-kenh-nhan-hieu`

The migration preserves existing `thumbnail_url` and `cover_image_url`; admins can upload or paste final URLs in the service dialog. The service dialog uses the existing `SystemImageStorage.SaveServiceThumbnailAsync` workflow, which stores thumbnails under the app's existing `/uploads/system/` public static file convention.

## DB Changes

Added idempotent source-controlled migration:

`database/migrations/20260813_commercial_video_service_catalog.sql`

It creates `catalog.service_sell_prices` with checks for:

- `asset_type`: `image`, `video_scene`
- `quality_tier`: `standard`, `premium`
- `sell_points >= 0`
- image rows require `duration_seconds IS NULL`
- video scene rows require positive `duration_seconds`

It uses a normalized unique identity on `(service_id, asset_type, quality_tier, COALESCE(duration_seconds, 0))` and seeds editable bootstrap prices without overwriting admin-customized `sell_points` on repeated runs.

No database migration was executed by Codex.

## Sell Pricing Architecture

Customer sell price data is stored in `catalog.service_sell_prices`. Provider/model cost remains separate in AI provider pricing tables and is not used as the primary customer commercial price.

Added `IServiceSellPriceResolver` / `ServiceSellPriceResolver` for:

- active price lookup by service
- image price resolution by quality tier
- video scene price resolution by quality tier and duration
- estimate calculation by image count, scene count, duration, and quality

Bootstrap defaults are admin-editable:

- image standard: 3 points / image
- image premium: 5 points / image
- video standard 4s: 8 points / scene
- video standard 6s: 10 points / scene
- video standard 8s: 12 points / scene
- video premium 4s: 12 points / scene
- video premium 6s: 15 points / scene
- video premium 8s: 18 points / scene

## Admin Behavior

Admin service management now allows editing:

- service name
- category
- short description
- description
- thumbnail URL
- thumbnail upload/replace with preview
- cover image URL
- service type / engine
- status
- sort order

`service_code` is editable for a new service and read-only after creation. New service codes are normalized to uppercase and must use only uppercase letters, digits, and underscores. The services page shows thumbnail, service name, short description, engine badge, enabled/disabled status, sort order, and sell price summary. A new `Giá bán` dialog manages service-level sell price rows.

`workflow_code` remains optional for backward compatibility and is shown under the advanced/internal section instead of as a primary customer-facing setting.

## Customer Behavior

`/create` loads all enabled `catalog.services` ordered by `sort_order`. It no longer filters by the legacy fixed service codes. Customer cards show thumbnail, service name, short description, dynamic starting price summary, and `Tạo video` CTA. Provider/model/workflow/internal engine details are not exposed in the customer card.

## Routing Behavior

Routing is based on `service_type` engine:

- `timelapse` -> `/jobs/timelapse/new?serviceId=<id>&serviceCode=<code>`
- `rvideo` -> RVideo destination placeholder while preserving selected service identity
- `rdance` -> RDance destination placeholder while preserving selected service identity

The Timelapse draft flow reads `serviceId` and `serviceCode` from query string and writes the selected commercial service identity into the job snapshot.

## Legacy Compatibility

Legacy `TIMELAPSE`, `RVIDEO`, and `RDANCE` records are not deleted. They remain available for historical references and compatibility. The new customer gallery is DB-driven and will show any enabled service, including an 11th admin-created service, without code changes.

## Validation

- `git diff --check`: passed. Git reported line-ending normalization warnings only.
- `dotnet build TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false`: passed with 45 existing CS8669 Razor generated-code warnings and 0 errors.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false`: passed, 351/351 tests.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard /p:UseSharedCompilation=false`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.
