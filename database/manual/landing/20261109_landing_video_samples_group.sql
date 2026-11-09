-- TodoX Landing: built-in video sample groups that are not services.
-- Additive and idempotent (landing.* only, no Foundation V2 object is touched).
-- service_id set           -> the sample belongs to that service's group.
-- service_id null, sample_group null  -> "Video truyền thông" (formerly "Dịch vụ khác").
-- service_id null, sample_group 'tvc' -> "Video TVC doanh nghiệp".

alter table landing.video_samples
    add column if not exists sample_group text;

do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_video_samples_sample_group'
          and conrelid = 'landing.video_samples'::regclass
    ) then
        alter table landing.video_samples
            add constraint ck_video_samples_sample_group check (sample_group is null or sample_group in ('tvc'));
    end if;
end $$;
