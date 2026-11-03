create schema if not exists landing;

create table if not exists landing.intro_video_settings (
    id integer primary key default 1 check (id = 1),
    is_enabled boolean not null default false,
    video_url text,
    poster_url text,
    playback_mode text not null default 'AUTOPLAY_LOOP' check (playback_mode in ('AUTOPLAY_LOOP', 'CLICK_TO_PLAY', 'AUTOPLAY_ONCE')),
    muted_by_default boolean not null default true,
    show_controls boolean not null default true,
    updated_at timestamptz not null default now(),
    updated_by text
);

insert into landing.intro_video_settings (id) values (1) on conflict (id) do nothing;

-- Verification (read-only):
select to_regclass('landing.intro_video_settings');
select id, is_enabled, video_url, poster_url, playback_mode, muted_by_default, show_controls, updated_at
from landing.intro_video_settings where id = 1;