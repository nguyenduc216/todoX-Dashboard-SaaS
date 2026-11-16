-- TodoX.EditVideoTool: starter skill library (todox-...), published as version 1 ("Bản chuẩn") so every customer's app
-- downloads them on the next skill sync. Content is split into steps by "## " headings; Claude receives one step at a
-- time through todox_skill_start / todox_skill_next. Data only, idempotent: a skill whose slug already exists is left
-- untouched (edit it in Dashboard › EditVideo skills instead). Run through tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TEMP TABLE tmp_starter_skills (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_starter_skills VALUES
('todox-ban-hang-tiktok', 'TodoX – Video bán hàng TikTok/Reels',
 'Dựng video bán hàng ngắn 9:16 (TikTok, Reels, Shorts) từ clip sản phẩm: mở bằng câu hook, khoe điểm mạnh, giá/ưu đãi và kêu gọi mua. Dùng khi khách muốn video bán hàng, chốt đơn, quảng cáo sản phẩm ngắn.',
 'Bán hàng', 10,
 '{"aspect":"9:16","targetSeconds":25,"styleId":"sales","captionStyle":"pop","transition":"slide","transitionSeconds":0.3,"musicVolume":0.25}',
$sk$## Bước 1 – Hỏi thông tin và mở dự án
- Hỏi ngắn gọn (một lượt): tên sản phẩm, 2–3 điểm mạnh, giá hoặc ưu đãi, kêu gọi hành động (nhắn tin, bấm giỏ hàng, gọi số...). Thiếu gì thì đề xuất sẵn để khách chọn.
- Gọi todox_projects để xem dự án có sẵn, hoặc todox_project_folder để mở/tạo dự án. Từ đây luôn truyền `project` cho mọi công cụ TodoX.
- Clip nằm trong 02-video, ảnh trong 01-anh, nhạc trong 03-am-thanh. Gọi todox_probe với thư mục dự án để biết độ dài, hướng quay của từng clip.

## Bước 2 – Viết kịch bản 25 giây
Viết kịch bản lời thoại/caption theo khung:
1. Hook 0–3s: một câu chạm nỗi đau hoặc gây tò mò ("Da khô nứt nẻ mùa lạnh?").
2. Sản phẩm 3–8s: tên + lợi ích chính.
3. Điểm mạnh 8–18s: 2–3 ý, mỗi ý một câu ngắn ≤ 10 từ.
4. Giá/ưu đãi 18–22s.
5. Kêu gọi 22–25s.
Câu ngắn, dễ đọc trên điện thoại. Cho khách xem kịch bản và sửa trước khi dựng.

## Bước 3 – Dựng bản đầu
- Gọi todox_edit_start với các clip theo thứ tự kể chuyện (clip đẹp nhất lên đầu), `title` = câu hook, `script` = kịch bản, `outroText` = câu kêu gọi, nhạc trong 03-am-thanh nếu có. Khung hình, độ dài, style, caption, chuyển cảnh đã được skill đặt sẵn — không tự đổi.
- Nếu khách muốn giọng đọc: gọi todox_voice_list, cho khách chọn giọng, báo trước là giọng đọc tốn điểm, rồi truyền `voice`.
- Ghi `skill` = todox-ban-hang-tiktok và `skillVersion` theo cuối skill.

## Bước 4 – Rà và chỉnh
- Đọc timeline trả về. Cảnh nào dài hơn 4 giây thì cắt ngắn bằng todox_cut; cảnh mờ/rung thì bỏ bằng todox_join.
- Caption chồng lên chữ trong clip hoặc sai chính tả: sửa bằng todox_caption.
- Sản phẩm có màu thương hiệu: todox_look với `accentColor`.
- Tóm tắt timeline cho khách bằng lời dễ hiểu (cảnh 1: ..., cảnh 2: ...), hỏi có muốn đổi gì không.

## Bước 5 – Xuất video
- Gọi todox_export. Video nằm trong thư mục output của dự án.
- Báo khách đường dẫn file, độ dài, và gợi ý 3 caption đăng bài kèm hashtag phù hợp sản phẩm.
$sk$),

('todox-bds-gioi-thieu', 'TodoX – Giới thiệu bất động sản',
 'Dựng video giới thiệu nhà phố, căn hộ, đất nền từ clip/ảnh dự án: vị trí, tiện ích, mặt bằng, giá và liên hệ môi giới. Dùng khi khách làm video bất động sản, rao bán/cho thuê nhà đất.',
 'Bất động sản', 20,
 '{"aspect":"9:16","targetSeconds":45,"styleId":"real-estate","captionStyle":"box","transition":"fade","transitionSeconds":0.6,"musicVolume":0.2}',
$sk$## Bước 1 – Thu thập thông tin căn
- Hỏi: loại BĐS, địa chỉ/khu vực, diện tích, số phòng, hướng, pháp lý, giá, tiện ích xung quanh, tên + số điện thoại môi giới. Không bịa số liệu; thiếu thì để trống hoặc hỏi lại.
- Mở dự án bằng todox_project_folder (truyền `project` cho mọi công cụ sau). Gọi todox_probe để xem clip trong 02-video và ảnh trong 01-anh.
- Logo/thông tin sàn trong 05-thuong-hieu nếu có.

## Bước 2 – Sắp thứ tự cảnh
Thứ tự gợi ý: mặt tiền/toàn cảnh → đường vào, khu vực → phòng khách → bếp → phòng ngủ → WC → ban công/view → tiện ích xung quanh. Mỗi cảnh 3–5 giây. Ảnh tĩnh dùng xen kẽ khi thiếu clip. Đưa danh sách thứ tự cho khách duyệt.

## Bước 3 – Kịch bản và dựng
- Kịch bản 45 giây: mở bằng điểm bán mạnh nhất ("Nhà 4 tầng mặt tiền, cách chợ 300m"), rồi lần lượt thông số, tiện ích, pháp lý, giá, liên hệ. Câu ngắn, có số cụ thể.
- Gọi todox_edit_start: `title` = loại BĐS + khu vực, `subtitle` = giá, `script` = kịch bản, `outroText` = "Liên hệ <tên> – <số điện thoại>". Khung, style real-estate, caption box, chuyển cảnh fade đã đặt sẵn.
- Giọng đọc (tốn điểm) chỉ dùng khi khách đồng ý: todox_voice_list → `voice`.
- Ghi `skill` = todox-bds-gioi-thieu và `skillVersion`.

## Bước 4 – Chỉnh và xuất
- Rà timeline: cảnh tối/rung thì bỏ (todox_join), cảnh dài thì cắt (todox_cut), số liệu trên caption phải khớp thông tin khách đưa (todox_caption).
- Khách cần bản YouTube ngang: làm thêm một bản bằng todox_edit_start với `aspect` = 16:9.
- todox_export, báo đường dẫn trong output và gợi ý nội dung đăng tin (tiêu đề, mô tả, hashtag khu vực).
$sk$),

('todox-review-san-pham', 'TodoX – Review / mở hộp sản phẩm',
 'Dựng video review hoặc mở hộp (unbox) sản phẩm theo kiểu KOC: cảm nhận thật, cận cảnh chi tiết, ưu nhược điểm, chốt có nên mua. Dùng khi khách quay review, unbox, trải nghiệm sản phẩm.',
 'Bán hàng', 30,
 '{"aspect":"9:16","targetSeconds":40,"styleId":"energetic","captionStyle":"karaoke","transition":"mix","transitionSeconds":0.3,"musicVolume":0.15}',
$sk$## Bước 1 – Xem tư liệu
- Mở dự án bằng todox_project_folder, luôn truyền `project`.
- Gọi todox_probe với 02-video. Clip có người nói: dùng todox_transcribe để lấy lời thoại có mốc thời gian — đây là xương sống của video review.

## Bước 2 – Chọn đoạn hay
Từ lời thoại, chọn các đoạn theo khung: mở hộp/khoe sản phẩm (hook) → cận cảnh chi tiết → trải nghiệm thật → 1 ưu điểm nổi bật + 1 điểm cần lưu ý → kết luận có nên mua. Bỏ đoạn ấp úng, lặp ý. Liệt kê các đoạn (clip, giây bắt đầu–kết thúc) cho khách duyệt.

## Bước 3 – Dựng
- Gọi todox_edit_start với các clip đã chọn, `title` = câu hook ngắn, giữ tiếng gốc của người nói (không đặt `voice`). Caption karaoke theo lời thoại: truyền `script` là lời thoại đã chọn.
- Cắt đúng đoạn bằng todox_cut theo mốc đã duyệt; sắp thứ tự bằng todox_join.
- todox_audio: nhạc nền nhỏ để không lấn giọng, `keepClipAudio` = true.
- Ghi `skill` = todox-review-san-pham và `skillVersion`.

## Bước 4 – Kiểm tra và xuất
- Kiểm tra caption khớp lời nói (todox_voice_check nếu cần), sửa chữ sai bằng todox_caption.
- todox_export, báo đường dẫn file và gợi ý caption đăng kèm link sản phẩm.
$sk$),

('todox-gioi-thieu-doanh-nghiep', 'TodoX – Giới thiệu doanh nghiệp / YouTube',
 'Dựng video giới thiệu công ty, cửa hàng, dịch vụ khung ngang 16:9 cho YouTube, website, Facebook: câu chuyện thương hiệu, sản phẩm/dịch vụ, khách hàng, liên hệ. Dùng khi khách cần video profile, giới thiệu doanh nghiệp, video cho website.',
 'Doanh nghiệp', 40,
 '{"aspect":"16:9","targetSeconds":60,"styleId":"clean","captionStyle":"box","transition":"fade","transitionSeconds":0.8,"musicVolume":0.25}',
$sk$## Bước 1 – Tìm hiểu doanh nghiệp
- Hỏi: tên doanh nghiệp, lĩnh vực, khách hàng mục tiêu, 3 điểm khác biệt, con số đáng tự hào (năm hoạt động, số khách...), thông tin liên hệ, màu thương hiệu.
- Mở dự án bằng todox_project_folder (luôn truyền `project`). Logo và màu trong 05-thuong-hieu; gọi todox_probe để xem clip và ảnh.

## Bước 2 – Kịch bản 60 giây
Khung: vấn đề khách hàng gặp (0–8s) → doanh nghiệp là ai (8–18s) → sản phẩm/dịch vụ (18–38s) → điểm khác biệt + con số (38–50s) → lời mời liên hệ (50–60s). Giọng văn chuyên nghiệp, tránh khoa trương, không bịa số liệu. Cho khách duyệt kịch bản.

## Bước 3 – Dựng
- Gọi todox_edit_start: clip theo kịch bản, `title` = tên doanh nghiệp, `subtitle` = khẩu hiệu, `script` = kịch bản, `outroText` = website/số điện thoại, `accentColor` = màu thương hiệu nếu có.
- Giọng đọc chuyên nghiệp: todox_voice_list, cho khách chọn, báo tốn điểm rồi truyền `voice`.
- Ghi `skill` = todox-gioi-thieu-doanh-nghiep và `skillVersion`.

## Bước 4 – Chỉnh và xuất
- Rà nhịp: cảnh 4–7 giây, chuyển cảnh mượt; chỉnh bằng todox_cut / todox_join.
- Cần bản dọc cho Reels: làm thêm bản `aspect` = 9:16, `targetSeconds` = 30 với kịch bản rút gọn.
- todox_export, báo đường dẫn và gợi ý tiêu đề + mô tả YouTube có từ khoá.
$sk$),

('todox-highlight-su-kien', 'TodoX – Highlight sự kiện',
 'Dựng video highlight sự kiện, khai trương, hội thảo, tiệc, team building từ nhiều clip: nhịp nhanh theo nhạc, khoảnh khắc đẹp nhất, logo và lời cảm ơn. Dùng khi khách có nhiều clip sự kiện cần tóm tắt thành video ngắn.',
 'Sự kiện', 50,
 '{"aspect":"9:16","targetSeconds":30,"styleId":"energetic","captionStyle":"pop","transition":"mix","transitionSeconds":0.25,"musicVolume":0.6}',
$sk$## Bước 1 – Gom tư liệu
- Mở dự án bằng todox_project_folder (luôn truyền `project`). Gọi todox_probe với 02-video để xem toàn bộ clip.
- Hỏi: tên sự kiện, ngày, địa điểm, đơn vị tổ chức, lời cảm ơn/kêu gọi cuối video, có nhạc riêng không (03-am-thanh).

## Bước 2 – Chọn khoảnh khắc
Chọn 8–12 khoảnh khắc mạnh: toàn cảnh địa điểm, khách đến, phát biểu/cắt băng, khoảnh khắc cười, nhóm chụp ảnh, cao trào. Mỗi khoảnh khắc 2–3 giây. Mở đầu bằng cảnh đông vui nhất. Liệt kê cho khách duyệt.

## Bước 3 – Dựng theo nhịp nhạc
- Gọi todox_edit_start với các clip đã chọn, `title` = tên sự kiện, `subtitle` = ngày + địa điểm, nhạc sôi động, `outroText` = lời cảm ơn. Không cần lời thoại dài: caption chỉ vài chữ điểm nhấn.
- Cắt từng cảnh còn 2–3 giây bằng todox_cut; tiếng hiện trường giữ nhỏ hoặc tắt bằng todox_audio.
- Ghi `skill` = todox-highlight-su-kien và `skillVersion`.

## Bước 4 – Xuất
- Rà timeline, bỏ cảnh trùng lặp, todox_export.
- Báo đường dẫn file và gợi ý caption đăng bài cảm ơn khách tham dự.
$sk$),

('todox-video-ai-quang-cao', 'TodoX – Video quảng cáo bằng AI',
 'Tạo video quảng cáo bằng AI từ ảnh sản phẩm hoặc mô tả (ảnh AI, video AI) rồi ghép thành video hoàn chỉnh có caption, nhạc, giọng đọc. Dùng khi khách không có clip quay sẵn, muốn tạo video/ảnh bằng AI, TVC ngắn từ ảnh sản phẩm. Tốn điểm TodoX.',
 'Video AI', 60,
 '{"aspect":"9:16","targetSeconds":20,"styleId":"todox","captionStyle":"pop","transition":"fade","transitionSeconds":0.4,"musicVolume":0.3}',
$sk$## Bước 1 – Ý tưởng và tư liệu
- Mở dự án bằng todox_project_folder (luôn truyền `project`, kết quả AI tự lưu vào 06-ai của dự án).
- Hỏi: sản phẩm, thông điệp chính, phong cách hình ảnh (sang trọng, tươi sáng, đời thường...), có ảnh sản phẩm thật không (01-anh).
- Đề xuất 3–4 cảnh, mỗi cảnh một câu mô tả hình ảnh + chuyển động máy quay. Khách duyệt trước khi tạo.

## Bước 2 – Chọn model và báo điểm
- Gọi todox_ai_models. Chọn model phù hợp (video từ ảnh nếu có ảnh sản phẩm thật).
- Không tự đoán tỉ lệ khung, độ phân giải, chế độ, thời lượng: chỉ dùng giá trị model liệt kê và khách đã xác nhận.
- Báo tổng điểm dự kiến (số cảnh × giá mỗi lần tạo) và chờ khách đồng ý. Chưa đồng ý thì không tạo.

## Bước 3 – Tạo cảnh AI
- Ảnh: todox_image_create; video: todox_video_create (ảnh sản phẩm thật dùng làm ảnh đầu vào). Truyền `project`.
- Video chạy lâu sẽ trả về task: kiểm tra bằng todox_video_status (ảnh: todox_image_status), todox_ai_tasks để xem danh sách. Báo khách tiến độ, không tạo lại khi task còn đang chạy.
- Cảnh lỗi hoặc xấu: hỏi khách trước khi tạo lại (tốn thêm điểm).

## Bước 4 – Ghép thành video
- Gọi todox_edit_start với các video AI trong 06-ai theo thứ tự cảnh, `title`, `script` (caption ngắn), `outroText` = kêu gọi mua/liên hệ, nhạc nếu có. Giọng đọc (tốn điểm) chỉ khi khách muốn.
- Ghi `skill` = todox-video-ai-quang-cao và `skillVersion`.
- Rà timeline, chỉnh bằng todox_cut / todox_caption / todox_look, rồi todox_export.
- Báo đường dẫn file và tổng điểm đã dùng.
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_starter_skills s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Bộ skill khởi đầu của TodoX (16/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_starter_skills s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_starter_skills;
