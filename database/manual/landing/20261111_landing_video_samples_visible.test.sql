do $$
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'landing' and table_name = 'video_samples'
          and column_name = 'is_visible' and is_nullable = 'NO' and data_type = 'boolean'
    ) then
        raise exception 'landing.video_samples.is_visible is missing';
    end if;
    -- Existing samples keep showing: nothing was hidden by the migration.
    if exists (select 1 from landing.video_samples where is_visible = false) then
        raise exception 'existing samples must stay visible';
    end if;
end $$;
