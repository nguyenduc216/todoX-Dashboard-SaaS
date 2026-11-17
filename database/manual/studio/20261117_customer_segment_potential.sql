-- Adds the customer type "Tiềm năng" (tiem_nang) to studio.customer_profiles.
-- studio.customer_profiles is a TodoX table (20261116_customer_segments_staff_video_plans.sql), not Foundation V2.
alter table studio.customer_profiles drop constraint if exists ck_customer_profiles_segment;
alter table studio.customer_profiles add constraint ck_customer_profiles_segment
    check (segment in ('tiem_nang', 'bam_duoi', 'thong_thuong', 'vip'));
