BEGIN;

CREATE TABLE IF NOT EXISTS todox_skill_actions (
    id bigserial PRIMARY KEY,
    action_id varchar(80) NOT NULL UNIQUE,
    job_id varchar(160) NULL,
    scene_index integer NULL,
    action_type varchar(80) NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'pending',
    idempotency_key varchar(160) NULL,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_json jsonb NULL,
    error_json jsonb NULL,
    requested_by varchar(160) NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_todox_skill_actions_idempotency
    ON todox_skill_actions(idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_todox_skill_actions_job_created
    ON todox_skill_actions(job_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_todox_skill_actions_status_created
    ON todox_skill_actions(status, created_at);

CREATE TABLE IF NOT EXISTS todox_skill_audit_log (
    id bigserial PRIMARY KEY,
    request_id varchar(100) NULL,
    action_id varchar(80) NULL,
    job_id varchar(160) NULL,
    scene_index integer NULL,
    operation varchar(80) NOT NULL,
    actor varchar(160) NULL,
    remote_ip inet NULL,
    http_status integer NULL,
    before_json jsonb NULL,
    after_json jsonb NULL,
    detail_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_todox_skill_audit_job_created
    ON todox_skill_audit_log(job_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_todox_skill_audit_action
    ON todox_skill_audit_log(action_id);

-- Helpful indexes for diagnostics/retry. These are added only when the
-- corresponding legacy/foundation columns exist, because TodoX has schema drift.
DO $$
BEGIN
    IF to_regclass('public.todox_scene_render_tasks') IS NOT NULL THEN
        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema='public' AND table_name='todox_scene_render_tasks' AND column_name='job_id'
        ) AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema='public' AND table_name='todox_scene_render_tasks' AND column_name='scene_index'
        ) THEN
            EXECUTE 'CREATE INDEX IF NOT EXISTS ix_todox_scene_render_tasks_job_scene ON todox_scene_render_tasks(job_id, scene_index)';
        END IF;

        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema='public' AND table_name='todox_scene_render_tasks' AND column_name='status'
        ) THEN
            EXECUTE 'CREATE INDEX IF NOT EXISTS ix_todox_scene_render_tasks_status ON todox_scene_render_tasks(status)';
        END IF;
    END IF;
END $$;

-- Record migration in the existing TodoX foundation migration ledger.
DO $$
BEGIN
    IF to_regclass('public.todox_foundation_migrations') IS NOT NULL THEN
        INSERT INTO todox_foundation_migrations(migration_key, migration_name, notes)
        VALUES (
            'skill_ops_20260807_001',
            'TodoX Skill/Ops diagnostic and retry foundation',
            jsonb_build_object(
                'component', 'TodoX.SkillEndpoint',
                'purpose', 'diagnostic-reconcile-retry-resume-repair',
                'safe', true
            )
        )
        ON CONFLICT (migration_key) DO NOTHING;
    END IF;
END $$;

COMMIT;
