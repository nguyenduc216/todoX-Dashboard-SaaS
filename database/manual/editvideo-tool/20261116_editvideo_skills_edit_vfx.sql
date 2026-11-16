-- TodoX.EditVideoTool: two more skills in the starter library, published as version 1 ("Bản chuẩn"):
-- todox-dung-video (general step-by-step edit) and todox-vfx (AI effects on an existing clip). Same rules as
-- 20261116_editvideo_starter_skills.sql: steps split by "## ", idempotent, existing slugs left untouched.
-- Run through tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TEMP TABLE tmp_more_skills (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_more_skills VALUES
('todox-dung-video', 'TodoX – Dựng video từ clip có sẵn',
 'Dựng video từ clip, ảnh của khách theo từng bước: chọn cảnh, cắt, sắp xếp, chuyển cảnh, caption, tiêu đề, nhạc, giọng đọc, màu thương hiệu rồi xuất. Dùng khi khách muốn edit video, cắt ghép, thêm chữ/nhạc mà không thuộc loại video riêng nào.',
 'Dựng video', 5,
 '{}',
$sk$## Bước 1 – Mở dự án và xem tư liệu
- Gọi todox_project_folder với tên dự án khách nói (hoặc todox_projects để chọn). Luôn truyền `project` cho mọi công cụ TodoX sau đó.
- Đọc CLAUDE.md và thư mục 04-kich-ban của dự án trước khi hỏi khách.
- Gọi todox_probe với thư mục dự án: biết số clip, độ dài, khung dọc/ngang. Clip có lời nói thì todox_transcribe để biết nội dung.

## Bước 2 – Chốt yêu cầu
Chỉ hỏi những gì còn thiếu, gom một lượt:
- Đăng ở đâu (TikTok/Reels → 9:16, YouTube → 16:9, Facebook feed → 4:5 hoặc 1:1) và độ dài mong muốn.
- Tiêu đề mở đầu, nội dung caption/lời dẫn, câu kết (kêu gọi hành động).
- Nhạc: file trong 03-am-thanh, hay tạo nhạc AI (tốn điểm), hay không nhạc. Giọng đọc AI (tốn điểm) hay giữ tiếng gốc.
- Màu thương hiệu, logo trong 05-thuong-hieu.
Đề xuất thứ tự cảnh (clip nào, đoạn nào) và cho khách duyệt.

## Bước 3 – Dựng bản đầu
- Gọi todox_edit_start với clip theo thứ tự đã duyệt, `aspect`, `targetSeconds`, `title`, `script`, `outroText`, `music`, `style` (xem todox_styles), `accentColor`.
- Giọng đọc: todox_voice_list → khách chọn → báo giá điểm từ todox_voice_list → truyền `voice`.
- Ghi `skill` = todox-dung-video và `skillVersion` theo cuối skill.

## Bước 4 – Chỉnh theo từng cảnh
- Đọc timeline (todox_timeline), tóm tắt cho khách: cảnh 1 ..., cảnh 2 ...
- Cắt/đổi đoạn: todox_cut. Đổi thứ tự, bỏ, thêm clip, chuyển cảnh (fade, slide, mix): todox_join.
- Chữ: todox_caption (karaoke, box, pop, plain). Tiêu đề/kết: todox_titles. Nhạc, giọng, tiếng gốc: todox_audio. Màu/phong cách: todox_look.
- Thiếu một cảnh: gợi ý tạo ảnh/video AI (todox_ai_models → báo giá → todox_image_create / todox_video_create, lưu vào 06-ai) rồi thêm bằng todox_join.

## Bước 5 – Xuất và ghi lại
- todox_export, báo khách đường dẫn trong output và độ dài.
- Ghi một dòng vào cuối CLAUDE.md của dự án: ngày, tên video, khung, độ dài.
- Khách cần thêm khung khác (dọc ↔ ngang): làm lại từ Bước 3 với `aspect` mới.
$sk$),

('todox-vfx', 'TodoX – Hiệu ứng VFX bằng AI',
 'Thêm hiệu ứng VFX bằng AI vào clip có sẵn: đổi thời tiết, ánh sáng, phép thuật, biến hình, lửa, khói, tuyết, phong cách điện ảnh... rồi ghép vào video. Dùng khi khách muốn hiệu ứng đặc biệt, VFX, biến đổi cảnh quay. Tốn điểm TodoX.',
 'Video AI', 55,
 '{}',
$sk$## Bước 1 – Chọn cảnh cần hiệu ứng
- Mở dự án bằng todox_project_folder (luôn truyền `project`). Gọi todox_probe với 02-video.
- Hỏi khách: cảnh nào cần hiệu ứng, hiệu ứng gì, mạnh hay nhẹ. Mỗi lần hiệu ứng chỉ nên cho một đoạn ngắn (3–10 giây): cắt đoạn đó trước bằng todox_cut nếu clip dài.
- Gợi ý vài hiệu ứng hợp nội dung: tuyết rơi, mưa, hoàng hôn, ánh đèn neon, lửa/khói, tia sáng phép thuật, biến hình sản phẩm, chuyển ngày → đêm.

## Bước 2 – Chọn model và báo điểm
- Gọi todox_ai_models type video. Chỉ chọn model có khả năng sửa video (abilities có withEdit), ví dụ các model "edit" như veo_omni_edit, kling-o3-edit, seedance_25_edit.
- Không tự đoán khung, độ phân giải, thời lượng: chỉ dùng giá trị model liệt kê và khách đã xác nhận.
- Báo khách số điểm theo bảng giá TodoX mà todox_ai_models trả về và chờ đồng ý. Nếu công cụ báo TodoX chưa đặt giá, nói với khách là chưa dùng được, không thử lại.

## Bước 3 – Tạo hiệu ứng
- Gọi todox_video_create với `model` đã chọn, `videos` = clip gốc trên máy (TodoX tự tải lên kho của khách), `prompt` mô tả hiệu ứng thật cụ thể (giữ nguyên người/vật chính, chỉ thêm hiệu ứng gì, ở đâu, mạnh cỡ nào), `project`.
- Chưa xong thì kiểm tra bằng todox_video_status khoảng mỗi phút. Kết quả nằm trong 06-ai của dự án.
- Kết quả chưa đạt: hỏi khách trước khi tạo lại (tốn thêm điểm), sửa prompt rõ hơn.

## Bước 4 – Ghép vào video
- Đưa clip hiệu ứng vào video: todox_join thay cảnh gốc bằng file trong 06-ai (hoặc todox_edit_start nếu chưa có bản dựng).
- Chuyển cảnh mượt (fade/mix ngắn) quanh cảnh hiệu ứng; nhạc nhấn đúng lúc hiệu ứng xuất hiện bằng todox_audio.
- Ghi `skill` = todox-vfx và `skillVersion` khi dựng. todox_export, báo đường dẫn và tổng điểm đã dùng.
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_more_skills s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Bộ skill TodoX (16/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_more_skills s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_more_skills;
