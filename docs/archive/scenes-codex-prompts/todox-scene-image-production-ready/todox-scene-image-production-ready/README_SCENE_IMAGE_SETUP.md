# TodoX — chuẩn bị database và chạy render ảnh scene

Gói này ghép hai bộ SQL độc lập:

- `yescale-v2-fixed`: provider, tariff, billing, ví hệ thống và quyền.
- `scene-media-versioning`: lịch sử ảnh/video/final và backfill dữ liệu cũ.
- `scene-image-runtime`: chỉ bật và kiểm tra render ảnh scene; video vẫn tắt.

## Nguyên tắc

- Backup database trước khi chạy.
- Chạy từng file, dừng ngay nếu file nào lỗi.
- Không chạy bản `database/manual/yescale/02_add_yescale_billing_support.sql` cũ.
- Chỉ dùng bản `v2-fixed`, vì bản này xử lý `billing.token_wallets.customer_id` cho ví hệ thống.
- Chưa bật scene video hoặc final video.

## Thứ tự

### 1. YEScale và billing

1. `yescale-v2-fixed/00_preflight_yescale_image_demo.sql`
2. `yescale-v2-fixed/01_add_or_update_billing_support.sql`
3. `yescale-v2-fixed/02_seed_yescale_image_tariffs.sql`
4. `yescale-v2-fixed/03_seed_system_wallet_and_permissions.sql`
5. `yescale-v2-fixed/verify_yescale_image_demo.sql`

Không chạy file 04 ở bước này.

### 2. Scene media versioning

1. `scene-media-versioning/00_preflight_scene_media_versioning.sql`
2. `scene-media-versioning/01_add_scene_media_versioning.sql`
3. `scene-media-versioning/02_backfill_existing_scene_media.sql`
4. `scene-media-versioning/03_seed_scene_media_versioning_settings.sql`
5. `scene-media-versioning/04_mark_orphan_queued_versions.sql`
6. `scene-media-versioning/verify_scene_media_versioning.sql`

### 3. Build và deploy code

Phải hoàn tất `dotnet restore`, `dotnet test`, `dotnet build -c Release` và `dotnet publish` trước khi enable.

### 4. Bật provider demo và chỉ bật version ảnh

1. `yescale-v2-fixed/04_enable_yescale_image_demo.sql`
2. `yescale-v2-fixed/verify_yescale_image_demo.sql`
3. `scene-image-runtime/05_enable_scene_image_versioning.sql`
4. `scene-image-runtime/06_verify_scene_image_runtime_ready.sql`

Kết quả mong đợi:

- `features.scene_render_versioning=true`
- `features.scene_video_versioning=false`
- `features.final_video_versioning=false`
- YEScale `nano-banana-2` là default của `scene_image_generation`
- Có đúng một ví `TODOX_AI_IMAGE_SYSTEM` hoạt động
- Không có duplicate logical request hoặc orphan queued version cũ

## Rollback

1. Chạy `scene-image-runtime/rollback_scene_image_runtime.sql`.
2. Nếu cần khôi phục provider mặc định trước demo, chạy `yescale-v2-fixed/rollback_yescale_image_demo.sql`.

Rollback không xóa lịch sử ảnh, billing, job hoặc media.

