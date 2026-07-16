# Scene Media Versioning — thứ tự xử lý

Gói này là thiết kế SQL và prompt để Codex đối chiếu với code/schema thật. Codex không được tự chạy SQL.

Sau khi Codex hoàn thành code, build, test, publish và xác nhận SQL phù hợp, người vận hành mới chạy thủ công:

1. Backup database.
2. `00_preflight_scene_media_versioning.sql`
3. `01_add_scene_media_versioning.sql`
4. `02_backfill_existing_scene_media.sql`
5. `03_seed_scene_media_versioning_settings.sql`
6. `verify_scene_media_versioning.sql`
7. Deploy code đã build/test.
8. Bật feature flags sau smoke test.

`rollback_scene_media_versioning.sql` chỉ tắt feature flags và không xóa lịch sử/media.

Gửi `PROMPT_FOR_CODEX.md` cho Codex cùng toàn bộ các SQL trong gói này.
