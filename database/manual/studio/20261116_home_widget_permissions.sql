-- TodoX dashboard: mỗi khối thông số trên trang chủ có quyền riêng "<module>.view" (2026-11-16).
-- Tài khoản chỉ thấy các khối được cấp; cấp/thu trên trang Phân quyền (dòng "Trang chủ · ...").
-- Danh sách khối: TodoX.Web/Services/HomeWidgets.cs. Thêm khối mới = thêm quyền mới bằng một file như file này.
--
-- Admin / root / vận hành: mọi khối. Nhân viên TodoX: Khách hàng, Kế hoạch video của tôi, Tổng quan kế hoạch video.
-- Additive and idempotent: new auth.permissions / auth.role_permissions rows only.

insert into auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
select gen_random_uuid(), v.module, 'view', v.name, v.description, 'Trang chủ', true, now()
  from (values
    ('home_customers', 'Trang chủ: Khách hàng', 'Số khách hàng và số khách VIP / bám đuổi.'),
    ('home_accounts', 'Trang chủ: Tài khoản', 'Số tài khoản quản trị và tài khoản khách hàng.'),
    ('home_points_sold', 'Trang chủ: Điểm đã bán', 'Tổng điểm đã bán cho khách.'),
    ('home_render_jobs', 'Trang chủ: Render Jobs', 'Số job render.'),
    ('home_my_video_plans', 'Trang chủ: Kế hoạch video của tôi', 'Kế hoạch video đang giao cho chính tài khoản.'),
    ('home_video_plans', 'Trang chủ: Tổng quan kế hoạch video', 'Kế hoạch đang làm, sắp release, trễ hạn.'),
    ('home_roadmap', 'Trang chủ: Lộ trình Sprint', 'Ghi chú lộ trình phát triển nội bộ.')
  ) as v(module, name, description)
 where not exists (select 1 from auth.permissions p where p.module = v.module and p.action = 'view');

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p on p.module like 'home\_%' and p.action = 'view'
 where lower(r.code) in ('administrator_root', 'admin', 'support')
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);

insert into auth.role_permissions (role_id, permission_id)
select r.id, p.id
  from auth.roles r
  join auth.permissions p
    on p.action = 'view' and p.module in ('home_customers', 'home_my_video_plans', 'home_video_plans')
 where r.code = 'staff'
   and not exists (select 1 from auth.role_permissions rp where rp.role_id = r.id and rp.permission_id = p.id);
