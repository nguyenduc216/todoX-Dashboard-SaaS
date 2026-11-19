do $$
declare
    v_tenant uuid;
    v_id uuid := gen_random_uuid();
begin
    if to_regclass('studio.staff_tasks') is null then raise exception 'studio.staff_tasks is missing'; end if;

    if (select count(*) from auth.permissions where module = 'staff_tasks') <> 4 then
        raise exception 'the 4 staff_tasks permissions must exist';
    end if;
    if not exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'admin' and p.module = 'staff_tasks' and p.action = 'delete') then
        raise exception 'admin must be able to delete staff tasks';
    end if;
    if exists (select 1 from auth.roles where code = 'staff') and not exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module = 'staff_tasks' and p.action = 'update') then
        raise exception 'staff must be able to update staff tasks';
    end if;
    if exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module = 'staff_tasks' and p.action = 'delete') then
        raise exception 'staff must not delete staff tasks';
    end if;

    select tenant_id into v_tenant from auth.app_users limit 1;
    if v_tenant is not null then
        -- A task without a customer is accepted, then removed again right away.
        insert into studio.staff_tasks (id, tenant_id, title) values (v_id, v_tenant, 'test');
        delete from studio.staff_tasks where id = v_id;
        -- Unknown statuses and blank titles are refused.
        begin
            insert into studio.staff_tasks (id, tenant_id, title, status) values (v_id, v_tenant, 'test', 'bogus');
            raise exception 'staff task status check is missing';
        exception when check_violation then null;
        end;
        begin
            insert into studio.staff_tasks (id, tenant_id, title) values (v_id, v_tenant, '   ');
            raise exception 'staff task title check is missing';
        exception when check_violation then null;
        end;
    end if;
end $$;
