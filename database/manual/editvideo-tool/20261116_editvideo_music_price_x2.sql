-- TodoX.EditVideoTool: AI music is sold at twice its 79AI cost, per model (owner's decision 16/11/2026):
-- 1 credit = 1đ, 1 point = 1.000đ -> Suno 4.5/5.0/5.5 (3.000 credit) = 6 points, Suno 6.0 (4.000) = 8 points.
-- Data only (system.app_settings), idempotent. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
VALUES
    (gen_random_uuid(), 'editvideo.ai.music_markup', 'editvideo', 'text', '2', 'Cài đặt EditVideo AI', true, now()),
    (gen_random_uuid(), 'editvideo.ai.vnd_per_point', 'editvideo', 'text', '1000', 'Cài đặt EditVideo AI', true, now()),
    (gen_random_uuid(), 'editvideo.ai.vnd_per_credit', 'editvideo', 'text', '1', 'Cài đặt EditVideo AI', true, now()),
    (gen_random_uuid(), 'editvideo.ai.cost_floor_markup', 'editvideo', 'text', '2', 'Cài đặt EditVideo AI', true, now())
ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, is_active = true, updated_at = now();
