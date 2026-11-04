-- TodoX Landing: extra images for the video sample library (/mau-video).
-- Manual script: run once on the TodoX SaaS database, after todox_landing_video_samples_service.sql.
-- Not executed automatically. Safe to re-run (idempotent). Only adds nullable columns; no data changes.
--   thumbnail_portrait_url  : 9:16 thumbnail (cards / player for portrait videos)
--   thumbnail_landscape_url : 16:9 thumbnail (cards / player for landscape videos, link-preview fallback)
--   share_image_url         : ~1.91:1 image (1200x630) used for Facebook / Zalo / Messenger link previews
-- poster_url stays required and keeps holding the thumbnail that matches the video's aspect ratio.
-- Dashboard and Landing keep working before this script is run; the new image slots stay disabled until then.

alter table landing.video_samples add column if not exists thumbnail_portrait_url text;
alter table landing.video_samples add column if not exists thumbnail_landscape_url text;
alter table landing.video_samples add column if not exists share_image_url text;

-- Verification (read-only):
select column_name, data_type, is_nullable
from information_schema.columns
where table_schema = 'landing' and table_name = 'video_samples'
  and column_name in ('thumbnail_portrait_url', 'thumbnail_landscape_url', 'share_image_url');
