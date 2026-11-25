-- TodoX: công việc nhân viên — người giao việc, hạn hoàn thành, lịch sử phản hồi (2026-11-25).
--
-- studio.staff_tasks gets:
--   assigner_user_id: người giao việc (mặc định người tạo).
--   due_at: thời gian dự kiến hoàn thành (hạn). "Trễ hạn" now reads due_at; completed_at is only the real
--           completion time. Unfinished tasks that used completed_at as the planned time move it to due_at.
-- studio.staff_task_comments: lịch sử phản hồi — comments people write, plus automatic entries for
--   status changes, so progress can be followed.
-- New permission staff_tasks.comment (viết phản hồi), granted to every role that can view tasks.
--
-- Additive and idempotent: studio tables are TodoX's own; no Foundation V2 table or column is altered.

alter table studio.staff_tasks add column if not exists assigner_user_id uuid references auth.app_users (id) on delete set null;
alter table studio.staff_tasks add column if not exists due_at timestamptz;

update studio.staff_tasks t
   set assigner_user_id = t.created_by
 where t.assigner_user_id is null
   and exists (select 1 from auth.app_users u where u.id = t.created_by);

update studio.staff_tasks
   set due_at = completed_at, completed_at = null
 where due_at is null and completed_at is not null and status in ('new', 'pending', 'in_progress');

create index if not exists ix_staff_tasks_assigner on studio.staff_tasks (assigner_user_id);

create table if not exists studio.staff_task_comments (
    id         uuid primary key,
    task_id    uuid not null references studio.staff_tasks (id) on delete cascade,
    kind       text not null default 'comment',
    body       text not null,
    old_status text,
    new_status text,
    created_at timestamptz not null default now(),
    created_by uuid references auth.app_users (id) on delete set null,
    constraint ck_staff_task_comments_kind check (kind in ('comment', 'status', 'created')),
    constraint ck_staff_task_comments_body check (length(btrim(body)) > 0 and length(body) <= 4000)
);

create index if not exists ix_staff_task_comments_task on studio.staff_task_comments (task_id, created_at);

insert into auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
select gen_random_uuid(), 'staff_tasks', 'comment', 'Phản hồi công việc nhân viên',
       'Viết phản hồi (comment) vào lịch sử của công việc.', 'Studio', true, now()
 where not exists (select 1 from auth.permissions p where p.module = 'staff_tasks' and p.action = 'comment');

insert into auth.role_permissions (role_id, permission_id)
select distinct rp.role_id, c.id
  from auth.role_permissions rp
  join auth.permissions v on v.id = rp.permission_id and v.module = 'staff_tasks' and v.action = 'view'
  join auth.permissions c on c.module = 'staff_tasks' and c.action = 'comment'
 where not exists (select 1 from auth.role_permissions x where x.role_id = rp.role_id and x.permission_id = c.id);
