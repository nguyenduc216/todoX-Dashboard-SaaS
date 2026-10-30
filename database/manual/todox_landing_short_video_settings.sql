-- TodoX Landing: settings for the /video-ngan page ("Video truyền thông").
-- Manual script: run once on the TodoX SaaS database. Not executed automatically.
-- Safe to re-run (idempotent).

create schema if not exists landing;

create table if not exists landing.short_video_settings (
    id integer primary key default 1 check (id = 1),
    show_on_landing boolean not null default true,
    intro_enabled boolean not null default false,
    video_url text,
    poster_url text,
    playback_mode text not null default 'CLICK_TO_PLAY' check (playback_mode in ('AUTOPLAY_LOOP', 'CLICK_TO_PLAY', 'AUTOPLAY_ONCE')),
    muted_by_default boolean not null default false,
    show_controls boolean not null default true,
    packages jsonb not null default '[]'::jsonb,
    updated_at timestamptz not null default now(),
    updated_by text
);

insert into landing.short_video_settings (id) values (1) on conflict (id) do nothing;

-- Verification (read-only):
select to_regclass('landing.short_video_settings');
select id, show_on_landing, intro_enabled, video_url, poster_url, playback_mode,
       muted_by_default, show_controls, jsonb_array_length(packages) as package_count, updated_at
from landing.short_video_settings where id = 1;
