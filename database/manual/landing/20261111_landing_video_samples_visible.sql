-- TodoX Landing: show / hide a video sample on the landing page (/mau-video, homepage) without deleting it.
-- Additive and idempotent (landing.* only, no Foundation V2 object is touched). Existing samples stay visible.

alter table landing.video_samples
    add column if not exists is_visible boolean not null default true;
