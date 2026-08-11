-- TodoX Landing - Multi video per industry
-- PostgreSQL / todo_saas
-- Additive migration: keeps landing.industry_solutions and all existing data unchanged.
-- Existing representative video is copied to the child table and remains the primary video.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS landing;

CREATE TABLE IF NOT EXISTS landing.industry_solution_videos
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    industry_solution_id uuid NOT NULL
        REFERENCES landing.industry_solutions(id) ON DELETE CASCADE,

    title varchar(200) NOT NULL,
    short_description varchar(500) NULL,
    description text NULL,

    thumbnail_url text NULL,
    video_url text NOT NULL,
    aspect_ratio varchar(10) NOT NULL DEFAULT '9:16',

    format_note text NULL,
    goal_note text NULL,
    capability_note text NULL,

    display_order integer NOT NULL DEFAULT 0,
    is_primary boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,

    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL,
    deleted_at timestamptz NULL,
    deleted_by uuid NULL,

    CONSTRAINT ck_industry_solution_videos_title_not_blank CHECK (length(btrim(title)) > 0),
    CONSTRAINT ck_industry_solution_videos_video_not_blank CHECK (length(btrim(video_url)) > 0),
    CONSTRAINT ck_industry_solution_videos_aspect_ratio CHECK (aspect_ratio IN ('9:16', '16:9')),
    CONSTRAINT ck_industry_solution_videos_display_order CHECK (display_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_industry_solution_videos_industry_order
ON landing.industry_solution_videos
(industry_solution_id, is_active, deleted_at, display_order, created_at);

CREATE INDEX IF NOT EXISTS ix_industry_solution_videos_public
ON landing.industry_solution_videos
(industry_solution_id, display_order, created_at)
WHERE is_active = true AND deleted_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_industry_solution_videos_one_primary
ON landing.industry_solution_videos(industry_solution_id)
WHERE is_primary = true AND deleted_at IS NULL;

CREATE OR REPLACE FUNCTION landing.set_industry_solution_video_updated_at()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_industry_solution_videos_updated_at
ON landing.industry_solution_videos;

CREATE TRIGGER trg_industry_solution_videos_updated_at
BEFORE UPDATE ON landing.industry_solution_videos
FOR EACH ROW
EXECUTE FUNCTION landing.set_industry_solution_video_updated_at();

INSERT INTO landing.industry_solution_videos
(
    industry_solution_id, title, short_description, description,
    thumbnail_url, video_url, aspect_ratio, format_note, goal_note,
    capability_note, display_order, is_primary, is_active,
    created_at, created_by, updated_at, updated_by
)
SELECT
    i.id, i.title, i.short_description, i.description,
    i.thumbnail_url, i.video_url, i.aspect_ratio, i.format_note,
    i.goal_note, i.capability_note, 0, true, true,
    i.created_at, i.created_by, i.updated_at, i.updated_by
FROM landing.industry_solutions i
WHERE i.video_url IS NOT NULL
  AND btrim(i.video_url) <> ''
  AND i.deleted_at IS NULL
  AND NOT EXISTS
  (
      SELECT 1 FROM landing.industry_solution_videos v
      WHERE v.industry_solution_id = i.id
        AND v.deleted_at IS NULL
  );

WITH ranked AS
(
    SELECT
        v.id,
        v.industry_solution_id,
        row_number() OVER
        (
            PARTITION BY v.industry_solution_id
            ORDER BY v.display_order, v.created_at, v.id
        ) AS rn
    FROM landing.industry_solution_videos v
    WHERE v.deleted_at IS NULL
      AND v.is_active = true
      AND NOT EXISTS
      (
          SELECT 1
          FROM landing.industry_solution_videos p
          WHERE p.industry_solution_id = v.industry_solution_id
            AND p.is_primary = true
            AND p.deleted_at IS NULL
      )
)
UPDATE landing.industry_solution_videos v
SET is_primary = true,
    updated_at = now()
FROM ranked r
WHERE v.id = r.id
  AND r.rn = 1;

COMMIT;

SELECT
    i.title AS industry,
    count(v.id) FILTER (WHERE v.deleted_at IS NULL) AS video_count,
    max(v.title) FILTER (WHERE v.is_primary = true AND v.deleted_at IS NULL) AS primary_video
FROM landing.industry_solutions i
LEFT JOIN landing.industry_solution_videos v
    ON v.industry_solution_id = i.id
WHERE i.deleted_at IS NULL
GROUP BY i.id, i.title, i.display_order
ORDER BY i.display_order, i.title;
