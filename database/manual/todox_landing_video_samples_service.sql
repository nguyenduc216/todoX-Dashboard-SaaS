-- TodoX Landing: link video samples to the service list (catalog.services).
-- Manual script: run once on the TodoX SaaS database, after todox_landing_video_samples.sql.
-- Not executed automatically. Safe to re-run (idempotent).
-- service_id null means "Dịch vụ khác". Samples created before this script start as "Dịch vụ khác"
-- until they are edited. category_id is kept so no existing data is removed.

alter table landing.video_samples
    add column if not exists service_id uuid references catalog.services (id) on delete set null;

alter table landing.video_samples
    alter column category_id drop not null;

create index if not exists ix_video_samples_service on landing.video_samples (service_id, created_at desc);

-- Verification (read-only):
select column_name, data_type, is_nullable
from information_schema.columns
where table_schema = 'landing' and table_name = 'video_samples' and column_name in ('service_id', 'category_id');
