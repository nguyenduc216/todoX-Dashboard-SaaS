-- TodoX.EditVideoTool: skill todox-bds-chia-se (real-estate CEO / expert talking to camera, TikTok/Reels style:
-- tight cuts, punch-in, big yellow headlines, keyword captions, logo), version 1 ("Bản chuẩn"). Needs app 1.2.8+
-- (todox_headlines, zoom, caption style tiktok). Idempotent; an existing slug is left untouched.

CREATE TEMP TABLE tmp_talk_skill (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_talk_skill VALUES
('todox-bds-chia-se', 'TodoX – Video chia sẻ BĐS: CEO / chuyên gia nói trước camera',
 'Dựng video dọc 9:16 từ clip CEO, chủ doanh nghiệp, môi giới bất động sản nói trước camera: cắt bỏ chỗ ngập ngừng, giữ câu hay, cắt cận (punch-in) đổi nhịp, tiêu đề lớn chữ vàng viền đỏ, phụ đề chữ đậm tô vàng từ khoá, logo thương hiệu. Dùng khi khách gửi clip nói chuyện, chia sẻ kinh nghiệm, giới thiệu dịch vụ BĐS, "edit giống video CEO", "video talking head".',
 'Bất động sản', 3,
 '{"aspect":"9:16","captionStyle":"tiktok","transition":"fade","transitionSeconds":0,"musicVolume":0.15}',
$sk$## Khi nào dùng
Khách có clip một người (CEO, chủ doanh nghiệp, môi giới, chuyên gia BĐS) nói trước camera và muốn video ngắn kiểu TikTok/Reels: nhịp nhanh, chữ to, có điểm nhấn. Video tham quan nhà/dự án bằng cảnh quay thì dùng todox-bds-gioi-thieu; video vị trí trên bản đồ dùng todox-bds-vi-tri.

## Bước 1 – Dự án và tư liệu
- Hỏi khách làm trong dự án TodoX nào (todox_projects; không tự đặt tên). Clip gốc để trong 02-video của dự án (todox_project_folder).
- Hỏi: thương hiệu / logo (file .png trong 01-anh), độ dài mong muốn (mặc định 45–75 giây), thông điệp chính, hotline hoặc câu kêu gọi cuối video.

## Bước 2 – Nghe clip
- todox_probe để biết độ dài, todox_transcribe (language vi) để có lời nói kèm thời gian từng câu.
- Đọc toàn bộ lời, chọn các câu hay nhất theo thứ tự: câu mở đầu gây tò mò (hook) → vấn đề khách hàng gặp → giải pháp / dịch vụ → bằng chứng (con số, khách thật) → kêu gọi hành động. Bỏ câu lặp, câu nói vấp, "à, ờ", khoảng lặng dài. Không đổi ý người nói.

## Bước 3 – Dựng khung cảnh
- todox_edit_start với clip gốc, project, aspect 9:16, không title (vào thẳng hook), skill todox-bds-chia-se.
- todox_join với scenes là các đoạn đã chọn trên cùng clip (clip c0, from = giây bắt đầu câu, seconds = độ dài câu, cắt sát đầu và cuối câu ±0,1 giây), transitionSeconds 0 (cắt thẳng).
- Đổi nhịp: xen kẽ zoom 0 và 0.15–0.22 giữa các câu liền nhau (cảnh cận), câu quan trọng dùng zoom 0.25.

## Bước 4 – Phụ đề và tiêu đề lớn
- todox_caption với captions là từng cụm 3–7 chữ đúng thời gian trên video đã cắt (cộng dồn độ dài các cảnh trước), captionStyle tiktok. Bọc từ khoá trong *dấu sao* để tô vàng (tên dự án, con số, lợi ích: *TỪ A–Z*, *2 NĂM*, *MIỄN PHÍ*).
- todox_headlines: 4–8 tiêu đề lớn 1–3 giây ở các ý chính (tên dự án, câu hỏi gây chú ý, lời hứa dịch vụ), chữ in hoa ngắn; sub là dòng nhỏ nếu cần. Không đặt tiêu đề trùng lúc có phụ đề dài.
- Logo: todox_headlines với logo = đường dẫn ảnh logo, logoCorner top-right.

## Bước 5 – Âm thanh và xuất
- Giữ giọng gốc: todox_audio keepClipAudio=true. Nhạc nền nhẹ từ thư viện (todox_library kind=music) với musicVolume 0.12–0.18 để không lấn giọng.
- Câu kết: todox_titles outroText = hotline / lời kêu gọi (nếu khách có).
- todox_timeline kiểm tra lại, rồi todox_export. Báo khách đường dẫn file; nhắc bấm Đồng bộ trong TodoX EditVideo để đưa lên Dashboard.
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_talk_skill s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Video CEO/chuyên gia BĐS nói trước camera, kiểu TikTok (22/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_talk_skill s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_talk_skill;
