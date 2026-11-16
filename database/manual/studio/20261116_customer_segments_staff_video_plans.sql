-- TodoX: loại khách hàng, nhân viên TodoX và kế hoạch làm video cho khách (2026-11-16).
--
-- 1. studio.customer_profiles   : loại khách (bám đuổi / thông thường / VIP), một dòng cho mỗi khách.
-- 2. studio.staff_members       : hồ sơ nhân viên TodoX gắn với tài khoản đăng nhập auth.app_users,
--                                 vai trò mới auth.roles 'staff' (Nhân viên TodoX).
-- 3. studio.video_plans         : kế hoạch làm video cho khách (nội dung, kịch bản, người làm, ngày release).
--    studio.video_plan_versions : các phiên bản video của một kế hoạch.
--    studio.video_plan_notes    : ghi chú khi làm việc cùng khách.
-- Quyền mới: video_plans.* và staff_members.* (cấp cho admin/root/support); vai trò staff được
-- video_plans.view/create/update và customers.view.
--
-- Additive and idempotent: only new tables in schema studio and new rows in auth.roles / auth.permissions /
-- auth.role_permissions. No existing Foundation V2 table or column is altered.

create schema if not exists studio;

-- ---------------------------------------------------------------- 1. Loại khách hàng
create table if not exists studio.customer_profiles (
    customer_id uuid primary key references crm.customers (id) on delete cascade,
    segment     text not null default 'thong_thuong',
    updated_at  timestamptz not null default now(),
    updated_by  uuid,
    constraint ck_customer_profiles_segment check (segment in ('bam_duoi', 'thong_thuong', 'vip'))
);

-- ---------------------------------------------------------------- 2. Nhân viên TodoX
create table if not exists studio.staff_members (
    user_id       uuid primary key references auth.app_users (id) on delete cascade,
    employee_code text,
    position      text,
    department    text,
    joined_on     date,
    note          text,
    created_at    timestamptz not null default now(),
    created_by    uuid,
    updated_at    timestamptz not null default now(),
    updated_by    uuid
);

create unique index if not exists ux_staff_members_employee_code
    on studio.staff_members (lower(employee_code))
    where employee_code is not null;

-- Role 'staff', copied from the existing 'support' row so every column the role table requires
-- (tenant, role type, sort order...) keeps the shape the dashboard already reads.
do $$
declare
    r jsonb;
begin
    if exists (select 1 from auth.roles where code = 'staff') then
        return;
    end if;
    select to_jsonb(x) into r from auth.roles x where x.code = 'support' limit 1;
    if r is null then
        raise exception 'auth.roles has no support role to copy the staff role from';
    end if;
    r := r || jsonb_build_object('id', gen_random_uuid(), 'code', 'staff', 'name', 'Nhân viên TodoX');
    if r ? 'is_system' then r := r || jsonb_build_object('is_system', false); end if;
    if r ? 'description' then
        r := r || jsonb_build_object('description', 'Nhân viên TodoX: làm video cho khách theo kế hoạch');
    end if;
    if r ? 'sort_order' and jsonb_typeof(r -> 'sort_order') = 'number' then
        r := r || jsonb_build_object('sort_order', (r ->> 'sort_order')::int + 1);
    end if;
    if r ? 'created_at' then r := r || jsonb_build_object('created_at', now()); end if;
    if r ? 'updated_at' then r := r || jsonb_build_object('updated_at', now()); end if;
    insert into auth.roles select * from jsonb_populate_record(null::auth.roles, r);
end $$;

-- ---------------------------------------------------------------- 3. Kế hoạch làm video
create table if not exists studio.video_plans (
    id               uuid primary key,
    customer_id      uuid not null references crm.customers (id) on delete cascade,
    title            text not null,
    content          text,
    script           text,
    assignee_user_id uuid references auth.app_users (id) on delete set null,
    release_date     date,
    status           text not null default 'planning',
    created_at       timestamptz not null default now(),
    created_by       uuid,
    updated_at       timestamptz not null default now(),
    updated_by       uuid,
    constraint ck_video_plans_status
        check (status in ('planning', 'scripting', 'producing', 'reviewing', 'released', 'cancelled'))
);

create index if not exists ix_video_plans_customer on studio.video_plans (customer_id, release_date);
create index if not exists ix_video_plans_assignee on studio.video_plans (assignee_user_id, release_date);
create index if not exists ix_video_plans_release on studio.video_plans (release_date);

create table if not exists studio.video_plan_versions (
    id          uuid primary key,
    plan_id     uuid not null references studio.video_plans (id) on delete cascade,
    version_no  integer not null,
    video_url   text,
    change_note text,
    status      text not null default 'draft',
    created_at  timestamptz not null default now(),
    created_by  uuid,
    constraint ux_video_plan_versions_no unique (plan_id, version_no),
    constraint ck_video_plan_versions_status check (status in ('draft', 'sent', 'approved', 'rejected')),
    constraint ck_video_plan_versions_no check (version_no > 0)
);

create table if not exists studio.video_plan_notes (
    id         uuid primary key,
    plan_id    uuid not null references studio.video_plans (id) on delete cascade,
    note       text not null,
    created_at timestamptz not null default now(),
    created_by uuid
);

create index if not exists ix_video_plan_notes_plan on studio.video_plan_notes (plan_id, created_at desc);

-- ---------------------------------------------------------------- Quyền
insert into auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
select gen_random_uuid(), v.module, v.action, v.name, v.description, 'Studio', true, now()
  from (values
    ('video_plans', 'view', 'Xem kế hoạch video', 'Xem kế hoạch làm video cho khách.'),
    ('video_plans', 'create', 'Tạo kế hoạch video', 'Tạo kế hoạch, thêm phiên bản và ghi chú.'),
    ('video_plans', 'update', 'Sửa kế hoạch video', 'Sửa kế hoạch, phiên bản và trạng thái.'),
    ('video_plans', 'delete', 'Xóa kế hoạch video', 'Xóa kế hoạch video.'),
    ('staff_members', 'view', 'Xem nhân viên TodoX', 'Xem danh sách nhân viên TodoX.'),
    ('staff_members', 'create', 'Thêm nhân viên TodoX', 'Tạo tài khoản dashboard cho nhân viên.'),
    ('staff_members', 'update', 'Sửa nhân viên TodoX', 'Sửa hồ sơ, khóa/mở tài khoản nhân viên.'),
    ('staff_members', 'delete', 'Xóa nhân viên TodoX', 'Xóa tài khoản nhân viên.')
  ) as v(module, action, name, description)
 where not exists (select 1 from auth.permissions p where p.module = v.module and p.action = v.action);

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p on p.module in ('video_plans', 'staff_members')
 where lower(r.code) in ('administrator_root', 'admin', 'support')
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p
    on (p.module = 'video_plans' and p.action in ('view', 'create', 'update'))
    or (p.module = 'customers' and p.action = 'view')
 where r.code = 'staff'
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);
