do $$
begin
    if (select count(*) from auth.permissions where module like 'home\_%' and action = 'view') < 7 then
        raise exception 'the 7 home page permissions must exist';
    end if;
    if exists (
        select 1 from auth.permissions p
         where p.module like 'home\_%' and p.action = 'view'
           and not exists (
               select 1 from auth.role_permissions rp join auth.roles r on r.id = rp.role_id
                where r.code = 'admin' and rp.permission_id = p.id)) then
        raise exception 'admin must see every home page block';
    end if;
    if not exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module = 'home_my_video_plans' and p.action = 'view') then
        raise exception 'staff must see their own video plans on the home page';
    end if;
    if exists (
        select 1 from auth.role_permissions rp
          join auth.roles r on r.id = rp.role_id
          join auth.permissions p on p.id = rp.permission_id
         where r.code = 'staff' and p.module in ('home_points_sold', 'home_accounts', 'home_roadmap')) then
        raise exception 'staff must not see points, accounts or the roadmap by default';
    end if;
end $$;
