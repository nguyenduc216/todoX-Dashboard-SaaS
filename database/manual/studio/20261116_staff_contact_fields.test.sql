do $$
begin
    if not exists (select 1 from information_schema.columns
                    where table_schema = 'studio' and table_name = 'staff_members' and column_name = 'phone') then
        raise exception 'studio.staff_members.phone is missing';
    end if;
    if not exists (select 1 from information_schema.columns
                    where table_schema = 'studio' and table_name = 'staff_members' and column_name = 'contact_email') then
        raise exception 'studio.staff_members.contact_email is missing';
    end if;
    -- The profile phone is not unique: two staff profiles may share it.
    if exists (select 1 from pg_indexes
                where schemaname = 'studio' and tablename = 'staff_members' and indexdef ilike '%(phone)%') then
        raise exception 'studio.staff_members.phone must not be unique';
    end if;
end $$;
