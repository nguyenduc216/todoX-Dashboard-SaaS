-- TodoX.EditVideoTool: skill todox-nhac-suno (AI music with Suno for a video), version 1 ("Bản chuẩn").
-- Needs app 1.1.3+ (todox_music_create) and the music price in Dashboard › Cài đặt EditVideo. Idempotent; an
-- existing slug is left untouched. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

CREATE TEMP TABLE tmp_music_skill (
    slug text, title text, description text, category text, sort_order int, settings jsonb, content text
);

INSERT INTO tmp_music_skill VALUES
('todox-nhac-suno', 'TodoX – Nhạc AI (Suno) cho video',
 'Tạo nhạc nền hoặc bài hát có lời bằng AI (Suno) cho video: chọn phong cách, viết lời, tạo nhạc, chọn bản hay rồi ghép vào video. Dùng khi khách cần nhạc riêng, nhạc không bản quyền, jingle, bài hát quảng cáo. Tốn điểm TodoX.',
 'Âm thanh', 15,
 '{}',
$sk$## Bước 1 – Hiểu video cần nhạc gì
- Mở dự án bằng todox_project_folder (luôn truyền `project`). Nếu 03-am-thanh đã có nhạc khách muốn dùng thì không cần tạo.
- Hỏi khách: nhạc nền không lời hay bài hát có lời; cảm xúc (vui, sang trọng, sôi động, ấm áp, hùng tráng); độ dài video; có tên thương hiệu/khẩu hiệu muốn đưa vào lời không.

## Bước 2 – Phong cách và lời
- Viết `styles` bằng tiếng Anh ngắn gọn: thể loại, nhạc cụ, nhịp BPM, giọng hát. Ví dụ: "upbeat pop, acoustic guitar, claps, 118 BPM, bright female vocal" hoặc "cinematic orchestral, piano, strings, 80 BPM, instrumental".
- Bài có lời: viết lời tiếng Việt có thẻ [Intro], [Verse], [Chorus], [Outro]; điệp khúc ngắn, dễ nhớ, có tên sản phẩm/thương hiệu. Video ngắn 15–30 giây chỉ cần một đoạn [Verse] + [Chorus].
- Nhạc nền: bỏ trống lời.
- Đặt tên bài (ít nhất 4 ký tự). Cho khách duyệt phong cách và lời.

## Bước 3 – Báo điểm và tạo nhạc
- Gọi todox_ai_models type music để xem model và giá điểm TodoX. Báo khách số điểm; khi tạo, TodoX EditVideo hiện cửa sổ xác nhận giá để khách bấm đồng ý (từ chối thì không tạo, không gọi lại). Nếu công cụ báo TodoX chưa đặt giá nhạc, nói khách là chưa dùng được, không thử lại.
- Gọi todox_music_create với `name`, `styles`, `lyrics` (nếu có lời), `gender` (m/f nếu có giọng hát), `project`. Thường ra 2 bản, lưu trong 03-am-thanh.
- Chưa xong thì todox_music_status khoảng mỗi phút.

## Bước 4 – Chọn bản và ghép vào video
- Báo khách đường dẫn các bản nhạc để nghe và chọn.
- Ghép vào video: todox_audio với `music` = bản đã chọn (hoặc truyền `music` khi todox_edit_start). Có giọng đọc thì để nhạc nhỏ (khoảng 0.15–0.25) cho rõ lời.
- todox_export, báo đường dẫn video và điểm đã dùng.
$sk$);

INSERT INTO editvideo.skills (tenant_id, slug, title, description, category, content, settings, is_active, version, sort_order)
SELECT t.id, s.slug, s.title, s.description, s.category, s.content, s.settings, true, 1, s.sort_order
  FROM tmp_music_skill s
 CROSS JOIN system.tenants t
ON CONFLICT (tenant_id, slug) DO NOTHING;

INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content, notes, settings)
SELECT k.tenant_id, k.id, 1, 'Bản chuẩn', k.title, k.description, k.content, 'Nhạc AI Suno (16/11/2026)', k.settings
  FROM editvideo.skills k
  JOIN tmp_music_skill s ON s.slug = k.slug AND s.content = k.content
 WHERE NOT EXISTS (SELECT 1 FROM editvideo.skill_versions v WHERE v.skill_id = k.id);

DROP TABLE tmp_music_skill;
