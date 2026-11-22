-- TodoX.EditVideoTool: skill todox-bds-chia-se-canh-chen (CEO / expert talking to camera, covered with cutaways:
-- the voice runs on while project footage, map shots and the customer's own edited videos illustrate each sentence;
-- sparkle captions, cut effects), version 1 ("Bản chuẩn"). Needs app 1.2.10+ (todox_broll, fx, caption style
-- sparkle). Idempotent; an existing slug is left untouched.

CREATE TEMP TABLE tmp_broll_skill (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_broll_skill VALUES
('todox-bds-chia-se-canh-chen', 'TodoX – Video CEO BĐS có cảnh chen: giọng nói liền, hình minh hoạ từng câu',
 'Dựng video dọc 9:16 từ clip CEO / chuyên gia BĐS nói trước camera, phủ cảnh chen (B-roll) lên phần lớn lời nói: flycam dự án, nhà mẫu, tiện ích, bản đồ vị trí, đoạn trích từ video khách đã làm; giọng người nói chạy liền, cảnh chen cắt bỏ chữ in sẵn, hiệu ứng chuyển cảnh (xoẹt, chớp sáng), phụ đề ô sticker lấp lánh. Dùng khi khách gửi clip nói chuyện kèm tư liệu dự án hoặc video mẫu, muốn "đừng để người nói đứng hoài", "chen cảnh vào", "giống video ATA".',
 'Bất động sản', 4,
 '{"aspect":"9:16","captionStyle":"sparkle","transition":"fade","transitionSeconds":0,"musicVolume":0.12}',
$sk$## Khi nào dùng
Khách có clip một người nói trước camera (CEO, môi giới, chuyên gia BĐS) VÀ có tư liệu hình để minh hoạ: cảnh quay dự án, flycam, nhà mẫu, tiện ích, hoặc video đã làm trước đó (dù có chữ in sẵn). Mục tiêu: người nói chỉ xuất hiện ở câu mở đầu, các ý chính và lời kêu gọi; còn lại là cảnh chen khớp từng câu, giọng không bị ngắt. Chỉ có clip nói, không có tư liệu: dùng todox-bds-chia-se.

## Bước 1 – Dự án và tư liệu
- Hỏi khách dự án TodoX nào (todox_projects; không tự đặt tên). Clip nói để trong 02-video; tư liệu chen (flycam, nhà mẫu, video mẫu đã edit) cũng để trong 02-video.
- Hỏi: logo (.png), độ dài mong muốn (mặc định giữ đủ ý, 60–100 giây), hotline / lời kêu gọi, các con số cần kiểm tra (diện tích, số sản phẩm, giá).

## Bước 2 – Nghe clip nói, xem tư liệu
- todox_probe, todox_transcribe (language vi) clip nói; sửa chính tả tên riêng (tên dự án, xã, đường) trước khi làm phụ đề.
- Xem từng file tư liệu (todox_probe; nếu cần xem nội dung thì nhờ khách mô tả hoặc dùng ảnh chụp khách gửi) và ghi lại: đoạn nào là flycam khu công nghiệp, dãy nhà, nội thất, hồ bơi, đường giao thông, đội ngũ tư vấn…; đoạn nào có chữ in sẵn ở trên hay ở dưới khung.
- Kiểm tra số liệu người nói đưa ra với nguồn công khai nếu được; số chưa chắc thì báo khách, không tự sửa lời người nói.

## Bước 3 – Khung chính (người nói)
- todox_edit_start với clip nói, project, aspect 9:16, không title, skill todox-bds-chia-se-canh-chen.
- todox_join: các câu đã chọn trên clip nói (cắt sát đầu/cuối câu, bỏ đoạn lạc đề), transitionSeconds 0, xen kẽ zoom 0 và 0.18–0.22.
- todox_audio keepClipAudio=true (bắt buộc: giọng lấy từ clip nói), nhạc nền nhẹ musicVolume 0.12.

## Bước 4 – Bản đồ (chỉ khi lời nói nhắc vị trí)
- Chỉ làm bản đồ khi câu nói nhắc vị trí dự án / khu vực (vẽ ranh giới trung tâm) hoặc nói về đi lại, khoảng cách (vẽ đường đi). Câu không nói di chuyển thì không vẽ đường.
- Ranh giới, toạ độ phải có nguồn (OpenStreetMap, bản vẽ quy hoạch khách gửi). Ghi đúng địa danh hành chính và diện tích quy hoạch; không ghép con số của khu lớn hơn vào viền khu nhỏ.
- todox_geo_video (skill todox-bds-vi-tri cho chi tiết) xuất file bản đồ; rồi đưa vào bằng todox_broll: from = giây bắt đầu đoạn cần dùng (thường lúc camera bay tới), speed 1.2–2 cho vừa câu nói, fx zoom.

## Bước 5 – Cảnh chen
- todox_broll với danh sách đầy đủ cảnh chen; mỗi cảnh khớp một cụm lời: at = giây trên video đã cắt khi cụm đó bắt đầu, seconds = tới khi cụm kết thúc (1–4 giây mỗi cảnh; liệt kê nhanh như "hồ bơi, gym, cà phê" thì mỗi từ một cảnh 0,7–1,2 giây).
- Phủ 55–75% thời lượng; để người nói hiện ở: 3–4 giây đầu, câu chuyển ý quan trọng, lời kêu gọi cuối.
- Tư liệu có chữ in sẵn: crop top (chữ ở đáy) hoặc bottom (chữ ở trên), keep 0.7–0.76. Video mẫu có chữ ở giữa khung thì chọn đoạn khác.
- Lấy đoạn từ video đã edit: chọn đoạn nằm trọn trong một cảnh; TodoX tự dời điểm bắt đầu ≤0,6 giây khi dính cảnh khác (đọc thông báo trả về).
- Hiệu ứng: fx whip cho cảnh chen đổi chủ đề, flash cho con số / ý quan trọng, zoom khi vào bản đồ; khoảng 1 hiệu ứng mỗi 5–7 giây, không đặt liền nhau.

## Bước 6 – Phụ đề, tiêu đề, xuất
- todox_caption captionStyle sparkle, cụm 2–6 chữ đúng thời gian; bọc từ khoá trong *dấu sao* (tên dự án, con số, tiện ích).
- todox_headlines 4–7 tiêu đề lớn ở ý chính (tên dự án + quy mô, số sản phẩm, "5–10 NĂM NỮA", lời kêu gọi); logo nếu khách có.
- todox_timeline kiểm tra (scenes, broll), rồi todox_export. Báo khách những số liệu chưa kiểm chứng; nhắc bấm Đồng bộ trong TodoX EditVideo.
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_broll_skill s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Video CEO BĐS phủ cảnh chen, giọng liền, phụ đề sparkle, hiệu ứng chuyển cảnh (22/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_broll_skill s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_broll_skill;
