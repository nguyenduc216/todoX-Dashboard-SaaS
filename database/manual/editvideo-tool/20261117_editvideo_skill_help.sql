-- TodoX.EditVideoTool: skill todox-tro-giup (what the customer can do and what their account already has),
-- version 1 ("Bản chuẩn"). Needs app 1.1.4+ (todox_library, todox_sync). Idempotent; an existing slug is left
-- untouched. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TEMP TABLE tmp_help_skill (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_help_skill VALUES
('todox-tro-giup', 'TodoX – Trợ giúp: làm được gì, tài khoản đang có gì',
 'Hướng dẫn dùng TodoX EditVideo và xem nhanh tài nguyên của tài khoản: danh sách dự án, video đã làm và trạng thái đồng bộ Dashboard, nhân vật AI, giọng đọc, nhạc, VFX, model AI và giá điểm, số dư. Dùng khi khách hỏi "trợ giúp", "help", "làm được gì", "tôi có những gì", "có nhân vật/giọng/nhạc/VFX nào".',
 'Trợ giúp', 1,
 '{}',
$sk$## Khi nào dùng
Khách gõ "trợ giúp", "help", "hướng dẫn", "làm được gì", hoặc hỏi tài khoản đang có gì (nhân vật, giọng đọc, nhạc, VFX, dự án, video, điểm).

## Bước 1 – Hỏi khách cần gì
Nếu khách chỉ gõ "trợ giúp", trả lời ngắn bằng danh sách dưới đây và hỏi khách muốn xem mục nào. Không gọi công cụ tốn điểm.

| Khách muốn | Lệnh TodoX |
|---|---|
| Xem dự án của tôi | todox_projects |
| Mở / tạo dự án | todox_project_folder (hỏi tên dự án, không tự đặt) |
| Video đã làm, đã lên Dashboard chưa | todox_sync |
| Nhân vật AI của tài khoản | todox_library kind=characters |
| Giọng đọc | todox_library kind=voices (hoặc todox_voice_list để xem giá) |
| Nhạc nền có sẵn + nhạc AI đã tạo | todox_library kind=music |
| Hiệu ứng VFX | todox_library kind=vfx |
| Model AI và giá điểm | todox_ai_models (type image / video / music) |
| Phong cách dựng | todox_styles |
| Gói, hạn, số điểm, dung lượng | todox_status |
| Các skill dựng video có sẵn | todox_skill_versions |

## Bước 2 – Trả lời theo mục khách chọn
- Gọi đúng lệnh ở bảng trên, tóm tắt kết quả thành danh sách ngắn (tên, mô tả, đường dẫn file trên máy nếu có). Không dán JSON dài.
- Nhân vật / nhạc / giọng mẫu đã được tải về thư mục "_Thu vien TodoX" trong thư mục tài nguyên; báo khách đường dẫn để mở xem. Muốn cập nhật mới nhất từ Dashboard: todox_library với refresh=true.
- Nhạc trong thư viện dùng được ngay làm nhạc nền: truyền đường dẫn file vào `music` của todox_auto_edit / todox_edit_start / todox_audio.
- VFX: dùng với todox_video_create (model VFX + clip nguồn là link https); xem skill todox-vfx.

## Bước 3 – Nhắc các quy tắc quan trọng
- Mọi việc dựng video, tạo ảnh/video/nhạc AI đều làm trong một **dự án**: hỏi khách tên dự án trước.
- Lệnh tốn điểm (ảnh, video, nhạc, giọng đọc AI) luôn hiện cửa sổ xác nhận giá TodoX; khách bấm đồng ý mới tạo, từ chối thì không trừ điểm.
- Video làm xong hiện trong "Jobs của tôi" trên Dashboard ở trạng thái **chưa đồng bộ**; khách mở cửa sổ TodoX EditVideo và bấm **Đồng bộ lên Dashboard** để đưa file video lên.
- Muốn làm video theo mẫu: gợi ý skill phù hợp (todox-ban-hang-tiktok, todox-bds-gioi-thieu, todox-review-san-pham, todox-gioi-thieu-doanh-nghiep, todox-highlight-su-kien, todox-video-ai-quang-cao, todox-dung-video, todox-vfx, todox-nhac-suno).
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_help_skill s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Trợ giúp + thư viện tài khoản (17/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_help_skill s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_help_skill;
