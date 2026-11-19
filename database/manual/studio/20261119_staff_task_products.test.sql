do $$
declare
    v_tenant uuid;
    v_task uuid := gen_random_uuid();
begin
    if to_regclass('studio.staff_task_products') is null then raise exception 'studio.staff_task_products is missing'; end if;

    select tenant_id into v_tenant from auth.app_users limit 1;
    if v_tenant is not null then
        insert into studio.staff_tasks (id, tenant_id, title) values (v_task, v_tenant, 'test');
        insert into studio.staff_task_products (id, task_id, video_url, title)
        values (gen_random_uuid(), v_task, '/media/customer-videos/videos/x.mp4', 'x');
        -- The same video twice on one task is refused.
        begin
            insert into studio.staff_task_products (id, task_id, video_url) values (gen_random_uuid(), v_task, '/media/customer-videos/videos/x.mp4');
            raise exception 'duplicate product check is missing';
        exception when unique_violation then null;
        end;
        -- Only files on this server (/media/...) are accepted.
        begin
            insert into studio.staff_task_products (id, task_id, video_url) values (gen_random_uuid(), v_task, 'https://evil.example/x.mp4');
            raise exception 'product url check is missing';
        exception when check_violation then null;
        end;
        -- Deleting the task removes its products.
        delete from studio.staff_tasks where id = v_task;
        if exists (select 1 from studio.staff_task_products where task_id = v_task) then
            raise exception 'products must be deleted with their task';
        end if;
    end if;
end $$;
