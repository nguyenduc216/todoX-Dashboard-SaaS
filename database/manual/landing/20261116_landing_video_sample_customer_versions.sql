-- TodoX Landing: a "Video truyền thông" sample may belong to a customer (optional) and be linked to the
-- customer's video (studio.customer_videos); every video file a sample used is kept as a version so an admin can
-- restore or delete old versions.
-- Additive and idempotent (landing.* only, no Foundation V2 object is touched). Existing samples get no customer.

alter table landing.video_samples
    add column if not exists customer_id uuid;
alter table landing.video_samples
    add column if not exists customer_video_id uuid;

create table if not exists landing.video_sample_versions (
    id          uuid primary key,
    sample_id   uuid not null references landing.video_samples (id) on delete cascade,
    video_url   text not null,
    poster_url  text,
    created_at  timestamptz not null default now(),
    created_by  text,
    constraint uq_video_sample_versions_url unique (sample_id, video_url)
);

create index if not exists ix_video_sample_versions_sample
    on landing.video_sample_versions (sample_id, created_at desc);

-- The video each existing sample uses now is its first version.
insert into landing.video_sample_versions (id, sample_id, video_url, poster_url, created_at, created_by)
select md5(s.id::text || s.video_url)::uuid, s.id, s.video_url, s.poster_url, coalesce(s.updated_at, now()), s.updated_by
  from landing.video_samples s
 where s.video_url is not null and s.video_url <> ''
on conflict (sample_id, video_url) do nothing;
