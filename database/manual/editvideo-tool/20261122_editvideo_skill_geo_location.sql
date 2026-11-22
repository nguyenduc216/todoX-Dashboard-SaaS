-- TodoX.EditVideoTool: skill todox-bds-vi-tri (real-estate location video on TodoX's own map: plots, amenities,
-- routes), version 1 ("Bản chuẩn"). Needs app 1.2.7+ (todox_geo_video). Idempotent; an existing slug is left
-- untouched. Run through the deploy (database/manual) or tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TEMP TABLE tmp_geo_skill (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_geo_skill VALUES
('todox-bds-vi-tri', 'TodoX – Video vị trí bất động sản (bản đồ Geo Layer)',
 'Dựng video vị trí dự án bất động sản trên bản đồ thật của TodoX: camera bay tới dự án, viền lô đất phát sáng theo màu kèm loại đất / diện tích / hướng, icon tiện ích xung quanh với khoảng cách, đường đi phát sáng tới các điểm quan trọng, la bàn và thẻ hotline. Dùng khi khách muốn video vị trí, bản đồ dự án, geo layer, phân lô quy hoạch, tiện ích xung quanh, kết nối giao thông.',
 'Bất động sản', 2,
 '{}',
$sk$## Khi nào dùng
Khách muốn video **vị trí** dự án / lô đất / nhà trên bản đồ: "geo layer", "bản đồ vị trí", "vẽ lô đất", "phân lô quy hoạch", "tiện ích xung quanh", "kết nối giao thông", "cách trung tâm bao xa". Video giới thiệu nhà bằng clip quay thì dùng skill todox-bds-gioi-thieu.

## Bước 1 – Dự án và vị trí
- Hỏi khách làm trong **dự án** TodoX nào (todox_projects liệt kê; không tự đặt tên).
- Xin **toạ độ chính xác** của dự án. Hướng dẫn khách: mở Google Maps, bấm chuột phải đúng vị trí, dòng đầu tiên là "vĩ độ, kinh độ" (ví dụ 10.8170, 106.7650) — bấm vào là chép được. Không tự đoán toạ độ.
- Hỏi tên hiển thị (ví dụ "DỰ ÁN THE GLOBAL CITY") và dòng địa danh (ví dụ "Phước Long · TP. Thủ Đức").

## Bước 2 – Lô đất (nếu có)
- Hỏi khách có muốn vẽ lô đất không. Mỗi lô cần: ký hiệu (A, B, C…), loại đất (ví dụ "Đất ở liền kề (ODT)"), diện tích ("8,1 ha", "120 m²"), hướng ("Hướng Đông Nam"), màu nếu khách muốn (đỏ, xanh, vàng…).
- Hình lô: tốt nhất là toạ độ các góc lấy từ Google Maps (chuột phải từng góc). Nếu khách chỉ có kích thước: dùng chiều ngang × chiều sâu (mét) và lô nằm cách điểm ghim bao nhiêu mét về phía Đông/Bắc, xoay bao nhiêu độ.
- Số liệu quy hoạch do khách cung cấp; nếu chưa chắc chắn thì thêm ghi chú "Số liệu quy hoạch tham khảo" (plotNote).

## Bước 3 – Tiện ích xung quanh
- Hỏi khách các tiện ích muốn nêu (tối đa 10): ga metro, trường học, đại học, chợ/siêu thị, bệnh viện/trạm y tế, công viên, trung tâm thương mại, ngân hàng — kèm toạ độ từng nơi (chuột phải trên Google Maps).
- Loại icon: metro, school, uni, market, health, park, mall, bank. Khoảng cách tới dự án TodoX tự tính.

## Bước 4 – Kết nối giao thông
- Hỏi 1–3 điểm đến quan trọng (trung tâm quận, sân bay, cao tốc, khu công nghiệp…) kèm toạ độ. TodoX tự tìm đường xe chạy thật trên bản đồ, tính quãng đường và thời gian.
- Nếu dịch vụ tìm đường chưa trả lời, video vẽ đường chim bay và ghi "(chim bay)" — báo khách biết.

## Bước 5 – Dựng video
- Hỏi khung hình (9:16 TikTok/Reels mặc định, 16:9 YouTube, 1:1), hotline trên thẻ cuối, màu thương hiệu nếu có.
- Gọi todox_geo_video với project, siteLon, siteLat (kinh độ trước, vĩ độ sau — ngược với thứ tự trên Google Maps), siteName, place, plots, amenities, destinations, hotline, aspect, skill="todox-bds-vi-tri", skillVersion theo cuối skill. Việc này **không tốn điểm**.
- Dựng mất vài phút (bản đồ được tải từ máy chủ TodoX). Báo khách đường dẫn file khi xong.

## Bước 6 – Hoàn thiện
- Muốn thêm nhạc nền / giọng đọc: dùng file vừa xuất làm clip cho todox_edit_start (giữ nguyên hình) rồi todox_audio, todox_export.
- Nhắc khách: video nằm trong thư mục output của dự án và hiện trong "Jobs của tôi" ở trạng thái chưa đồng bộ — mở TodoX EditVideo và bấm **Đồng bộ** để đưa lên Dashboard.
- Video luôn ghi "Bản đồ TodoX · © OpenStreetMap contributors" (bắt buộc theo giấy phép dữ liệu bản đồ).
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_geo_skill s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Video vị trí BĐS trên bản đồ TodoX (22/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_geo_skill s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_geo_skill;
