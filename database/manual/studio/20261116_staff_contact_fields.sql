-- TodoX: số điện thoại và email liên hệ của nhân viên lưu trong hồ sơ nhân viên (2026-11-16).
-- Người dùng: chỉ tên đăng nhập phải khác nhau; số điện thoại / email của nhân viên được trùng với tài khoản khác
-- (ví dụ chính người đó có tài khoản khách hàng). auth.app_users giữ phone/email duy nhất (Foundation V2, không sửa),
-- nên khi số/email đã thuộc tài khoản khác, tài khoản đăng nhập để trống trường đó và hồ sơ nhân viên giữ giá trị.
-- Additive and idempotent: two nullable columns on studio.staff_members (created by this dashboard, not Foundation V2).

alter table studio.staff_members add column if not exists phone text;
alter table studio.staff_members add column if not exists contact_email text;
