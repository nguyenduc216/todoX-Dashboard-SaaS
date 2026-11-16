do $$
begin
    if (select count(*) from information_schema.columns
         where table_schema = 'landing' and table_name = 'video_samples'
           and column_name in ('customer_id', 'customer_video_id')) <> 2 then
        raise exception 'landing.video_samples customer columns are missing';
    end if;
    if to_regclass('landing.video_sample_versions') is null then
        raise exception 'landing.video_sample_versions is missing';
    end if;
    -- Existing samples are untouched: no customer was assigned by the migration.
    if exists (select 1 from landing.video_samples where customer_id is not null or customer_video_id is not null) then
        raise exception 'existing samples must not get a customer';
    end if;
    -- Every sample with a video has its current video as a version.
    if exists (select 1 from landing.video_samples s
                where coalesce(s.video_url, '') <> ''
                  and not exists (select 1 from landing.video_sample_versions v
                                   where v.sample_id = s.id and v.video_url = s.video_url)) then
        raise exception 'current videos were not recorded as versions';
    end if;
end $$;
