do $$
begin
    if to_regclass('studio.customer_profiles') is null then raise exception 'studio.customer_profiles is missing'; end if;
    if to_regclass('studio.staff_members') is null then raise exception 'studio.staff_members is missing'; end if;
    if to_regclass('studio.video_plans') is null then raise exception 'studio.video_plans is missing'; end if;
    if to_regclass('studio.video_plan_versions') is null then raise exception 'studio.video_plan_versions is missing'; end if;
    if to_regclass('studio.video_plan_notes') is null then raise exception 'studio.video_plan_notes is missing'; end if;

    if (select count(*) from auth.roles where code = 'staff') <> 1 then
        raise exception 'exactly one staff role must exist';
    end if;
    if (select count(*) from auth.permissions where module in ('video_plans', 'staff_members')) <> 8 then
        raise exception 'the 8 video_plans / staff_members permissions must exist';
    end if;
    if not exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'admin' and p.module = 'staff_members' and p.action = 'create') then
        raise exception 'admin must be able to create staff accounts';
    end if;
    if not exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module = 'video_plans' and p.action = 'update') then
        raise exception 'staff must be able to update video plans';
    end if;
    if exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module in ('staff_members', 'admin_users')) then
        raise exception 'staff must not manage staff or admin accounts';
    end if;

    -- Only the three customer types are accepted.
    begin
        insert into studio.customer_profiles (customer_id, segment)
        select id, 'bogus' from crm.customers limit 1;
        if found then raise exception 'segment check is missing'; end if;
    exception when check_violation then null;
    end;
    -- Only the known plan statuses are accepted.
    begin
        insert into studio.video_plans (id, customer_id, title, status)
        select gen_random_uuid(), id, 'test', 'bogus' from crm.customers limit 1;
        if found then raise exception 'video plan status check is missing'; end if;
    exception when check_violation then null;
    end;
end $$;
