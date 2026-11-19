-- TodoX: quản lý công việc (task) của nhân viên (2026-11-19).
--
-- studio.staff_tasks: tiêu đề, ghi chú, người thực hiện, ngày giờ thực hiện, ngày giờ hoàn thành,
-- trạng thái (mới / chờ thực hiện / đang thực hiện / đã hoàn thành / hủy), khách hàng (không bắt buộc).
-- Quyền mới staff_tasks.view/create/update/delete: cấp đủ cho administrator_root/admin/support;
-- vai trò staff được view/create/update.
--
-- Additive and idempotent: one new table in schema studio and new rows in auth.permissions /
-- auth.role_permissions. No existing Foundation V2 table or column is altered.

create schema if not exists studio;

create table if not exists studio.staff_tasks (
    id               uuid primary key,
    tenant_id        uuid not null,
    title            text not null,
    note             text,
    customer_id      uuid references crm.customers (id) on delete set null,
    assignee_user_id uuid references auth.app_users (id) on delete set null,
    start_at         timestamptz,
    completed_at     timestamptz,
    status           text not null default 'new',
    created_at       timestamptz not null default now(),
    created_by       uuid,
    updated_at       timestamptz not null default now(),
    updated_by       uuid,
    constraint ck_staff_tasks_status check (status in ('new', 'pending', 'in_progress', 'done', 'cancelled')),
    constraint ck_staff_tasks_title check (length(btrim(title)) > 0)
);

create index if not exists ix_staff_tasks_tenant_status on studio.staff_tasks (tenant_id, status, start_at);
create index if not exists ix_staff_tasks_assignee on studio.staff_tasks (assignee_user_id, start_at);
create index if not exists ix_staff_tasks_customer on studio.staff_tasks (customer_id) where customer_id is not null;

insert into auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
select gen_random_uuid(), v.module, v.action, v.name, v.description, 'Studio', true, now()
  from (values
    ('staff_tasks', 'view', 'Xem công việc nhân viên', 'Xem danh sách công việc và thống kê theo trạng thái.'),
    ('staff_tasks', 'create', 'Tạo công việc nhân viên', 'Tạo công việc, giao cho nhân viên.'),
    ('staff_tasks', 'update', 'Sửa công việc nhân viên', 'Sửa công việc và đổi trạng thái.'),
    ('staff_tasks', 'delete', 'Xóa công việc nhân viên', 'Xóa công việc.')
  ) as v(module, action, name, description)
 where not exists (select 1 from auth.permissions p where p.module = v.module and p.action = v.action);

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p on p.module = 'staff_tasks'
 where lower(r.code) in ('administrator_root', 'admin', 'support')
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p on p.module = 'staff_tasks' and p.action in ('view', 'create', 'update')
 where r.code = 'staff'
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);
