do $$
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'landing' and table_name = 'video_samples' and column_name = 'sort_order' and data_type = 'integer'
    ) then
        raise exception 'landing.video_samples.sort_order is missing';
    end if;
    if exists (select 1 from landing.video_samples where sort_order is null) then
        raise exception 'every existing sample must have a position';
    end if;
    -- Today's order is kept: newest first.
    if exists (
        select 1 from landing.video_samples a join landing.video_samples b on a.created_at > b.created_at
        where a.sort_order > b.sort_order
    ) then
        raise exception 'existing samples must keep the newest-first order';
    end if;
end $$;
