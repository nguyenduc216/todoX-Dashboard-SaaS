do $$
declare
    v_task uuid;
    v_id uuid := gen_random_uuid();
    n int;
begin
    if not exists (select 1 from information_schema.columns
                    where table_schema = 'studio' and table_name = 'staff_tasks' and column_name = 'assigner_user_id') then
        raise exception 'staff_tasks.assigner_user_id is missing';
    end if;
    if not exists (select 1 from information_schema.columns
                    where table_schema = 'studio' and table_name = 'staff_tasks' and column_name = 'due_at') then
        raise exception 'staff_tasks.due_at is missing';
    end if;
    if to_regclass('studio.staff_task_comments') is null then raise exception 'studio.staff_task_comments is missing'; end if;

    select count(*) into n from studio.staff_tasks t
     where t.assigner_user_id is null and exists (select 1 from auth.app_users u where u.id = t.created_by);
    if n <> 0 then raise exception '% tasks have no assigner although their creator exists', n; end if;

    select count(*) into n from studio.staff_tasks where status in ('new', 'pending', 'in_progress') and completed_at is not null;
    if n <> 0 then raise exception '% unfinished tasks still keep a completion time', n; end if;

    if exists (
        select 1 from auth.role_permissions rp
          join auth.permissions v on v.id = rp.permission_id and v.module = 'staff_tasks' and v.action = 'view'
         where not exists (select 1 from auth.role_permissions x
                             join auth.permissions c on c.id = x.permission_id and c.module = 'staff_tasks' and c.action = 'comment'
                            where x.role_id = rp.role_id)) then
        raise exception 'every role that views staff tasks must be able to comment';
    end if;

    select id into v_task from studio.staff_tasks limit 1;
    if v_task is not null then
        insert into studio.staff_task_comments (id, task_id, body) values (v_id, v_task, 'test');
        delete from studio.staff_task_comments where id = v_id;
        begin
            insert into studio.staff_task_comments (id, task_id, body) values (v_id, v_task, '   ');
            raise exception 'comment body check is missing';
        exception when check_violation then null;
        end;
        begin
            insert into studio.staff_task_comments (id, task_id, kind, body) values (v_id, v_task, 'bogus', 'x');
            raise exception 'comment kind check is missing';
        exception when check_violation then null;
        end;
    end if;
end $$;
