do $$
declare
    v_customer uuid;
begin
    if not exists (select 1 from pg_constraint where conname = 'ck_customer_profiles_segment'
                    and conrelid = 'studio.customer_profiles'::regclass
                    and pg_get_constraintdef(oid) like '%tiem_nang%') then
        raise exception 'segment check must accept tiem_nang';
    end if;

    select id into v_customer from crm.customers
     where id not in (select customer_id from studio.customer_profiles) limit 1;
    if v_customer is not null then
        -- tiem_nang is accepted; the row is removed again right away.
        insert into studio.customer_profiles (customer_id, segment) values (v_customer, 'tiem_nang');
        delete from studio.customer_profiles where customer_id = v_customer;
        -- Unknown types are still refused.
        begin
            insert into studio.customer_profiles (customer_id, segment) values (v_customer, 'bogus');
            raise exception 'segment check is missing';
        exception when check_violation then null;
        end;
    end if;
end $$;
