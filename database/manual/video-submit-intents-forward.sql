-- Operator only: apply before deploying a worker using SceneVideoSubmitCoordinator.
-- Contract: TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs.
BEGIN;
CREATE TABLE IF NOT EXISTS render.scene_video_submit_intents (
    tenant_id uuid NOT NULL,
    scene_id bigint NOT NULL,
    logical_request_id text NOT NULL CHECK (length(btrim(logical_request_id)) > 0),
    render_job_id uuid NOT NULL,
    owner_token uuid NOT NULL,
    submit_state text NOT NULL DEFAULT 'SUBMIT_INTENT_PERSISTED'
        CHECK (submit_state IN ('SUBMIT_INTENT_PERSISTED', 'SUBMIT_IN_FLIGHT',
            'CONFIRMED_PROVIDER_TASK', 'CONFIRMED_NO_TASK', 'AMBIGUOUS_SUBMIT', 'MANUAL_REVIEW_REQUIRED')),
    provider_task_id text,
    provider_video_id_base text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    reconciled_at timestamptz,
    PRIMARY KEY (tenant_id, scene_id, logical_request_id),
    UNIQUE (owner_token),
    FOREIGN KEY (render_job_id) REFERENCES render.render_jobs(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS scene_video_submit_intents_scene_idx
    ON render.scene_video_submit_intents (tenant_id, scene_id, submit_state);
COMMIT;