-- TodoX Landing: video sample library shown on the /mau-video page ("Mẫu video").
-- Manual script: run once on the TodoX SaaS database. Not executed automatically.
-- Safe to re-run (idempotent). Requires catalog.service_categories (video service groups).

create schema if not exists landing;

create table if not exists landing.video_samples (
    id uuid primary key,
    title text not null check (char_length(title) between 1 and 200),
    description text not null default '' check (char_length(description) <= 2000),
    aspect_ratio text not null check (aspect_ratio in ('9:16', '16:9')),
    video_url text not null,
    poster_url text not null,
    category_id uuid not null references catalog.service_categories (id) on delete restrict,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    updated_by text
);

create index if not exists ix_video_samples_category on landing.video_samples (category_id, created_at desc);

-- Verification (read-only):
select to_regclass('landing.video_samples');
select count(*) as sample_count from landing.video_samples;
