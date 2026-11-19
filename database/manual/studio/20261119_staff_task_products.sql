-- TodoX: "Sản phẩm" của công việc nhân viên (2026-11-19): các video trong thư viện video đã tải lên
-- (Video khách hàng, Kho mẫu video) mà công việc đó làm ra.
--
-- Additive and idempotent: one new table in schema studio. No existing Foundation V2 table or column is altered.

create table if not exists studio.staff_task_products (
    id         uuid primary key,
    task_id    uuid not null references studio.staff_tasks (id) on delete cascade,
    video_url  text not null,
    title      text,
    source     text,
    poster_url text,
    sort_order integer not null default 0,
    created_at timestamptz not null default now(),
    created_by uuid,
    constraint ux_staff_task_products_url unique (task_id, video_url),
    constraint ck_staff_task_products_url check (video_url like '/media/%')
);

create index if not exists ix_staff_task_products_task on studio.staff_task_products (task_id, sort_order);
