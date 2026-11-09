do $$
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'landing' and table_name = 'video_samples'
          and column_name = 'sample_group' and is_nullable = 'YES'
    ) then
        raise exception 'landing.video_samples.sample_group is missing';
    end if;
    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_video_samples_sample_group' and conrelid = 'landing.video_samples'::regclass
    ) then
        raise exception 'ck_video_samples_sample_group is missing';
    end if;
    -- Existing samples keep their group: nothing was moved.
    if exists (select 1 from landing.video_samples where sample_group is not null) then
        raise exception 'existing samples must not be assigned to a built-in group';
    end if;
end $$;
