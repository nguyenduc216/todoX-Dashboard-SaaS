-- TodoX Landing: vị trí sắp xếp của video mẫu (2026-11-25). Kho mẫu video in the dashboard reorders samples by
-- drag and drop; /mau-video and the homepage list them in this order. Existing samples keep today's order
-- (newest first); a sample added later goes to the top until it is moved.
-- Additive and idempotent (landing.* only, no Foundation V2 object is touched).

alter table landing.video_samples
    add column if not exists sort_order integer;

update landing.video_samples s
   set sort_order = r.position
  from (select id, row_number() over (order by created_at desc, id) as position from landing.video_samples) r
 where r.id = s.id and s.sort_order is null;

create index if not exists ix_video_samples_sort_order on landing.video_samples (sort_order, created_at desc);
