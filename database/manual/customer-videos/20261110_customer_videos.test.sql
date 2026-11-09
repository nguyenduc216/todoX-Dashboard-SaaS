do $$
begin
    if to_regclass('studio.video_product_prices') is null then
        raise exception 'studio.video_product_prices is missing';
    end if;
    if to_regclass('studio.customer_videos') is null then
        raise exception 'studio.customer_videos is missing';
    end if;
    if (select count(*) from studio.video_product_prices where product_code in ('truyen_thong', 'tvc')) <> 2 then
        raise exception 'both video kinds must be priced';
    end if;
    if not exists (
        select 1 from pg_indexes
        where schemaname = 'studio' and indexname = 'ix_customer_videos_customer_created'
    ) then
        raise exception 'ix_customer_videos_customer_created is missing';
    end if;
    -- Only valid kinds and statuses are accepted.
    begin
        insert into studio.customer_videos (id, customer_id, product_code, title, charge_status)
        values (gen_random_uuid(), gen_random_uuid(), 'tvc', 'test', 'bogus');
        raise exception 'charge_status check is missing';
    exception when check_violation then null;
    end;
end $$;
