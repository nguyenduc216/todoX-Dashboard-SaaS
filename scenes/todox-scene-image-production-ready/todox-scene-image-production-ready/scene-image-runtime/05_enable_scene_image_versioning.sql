\set ON_ERROR_STOP on

-- Enable only immutable scene-image history after code deployment and all prior verification passes.
-- Video versioning remains disabled.
BEGIN;

DO $$
DECLARE
    default_count integer;
    system_wallet_count integer;
BEGIN
    IF to_regclass('video_render.scene_image_versions') IS NULL THEN
        RAISE EXCEPTION 'Missing video_render.scene_image_versions. Run the scene-media-versioning package first.';
    END IF;

    IF to_regclass('billing.ai_image_billing_records') IS NULL THEN
        RAISE EXCEPTION 'Missing billing.ai_image_billing_records. Run the YEScale v2-fixed package first.';
    END IF;

    SELECT count(*) INTO system_wallet_count
      FROM billing.token_wallets
     WHERE wallet_scope='system'
       AND wallet_code='TODOX_AI_IMAGE_SYSTEM'
       AND status='active';

    IF system_wallet_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one active TODOX_AI_IMAGE_SYSTEM wallet, found %.', system_wallet_count;
    END IF;

    SELECT count(*) INTO default_count
      FROM public.todox_ai_provider_capability
     WHERE capability_code='scene_image_generation'
       AND provider_code='yescale_task_image'
       AND model_name='nano-banana-2'
       AND enabled=true
       AND is_default=true;

    IF default_count <> 1 THEN
        RAISE EXCEPTION 'Expected one enabled default YEScale nano-banana-2 scene_image_generation row, found %.', default_count;
    END IF;
END $$;

INSERT INTO system.app_settings
    (id,setting_key,setting_group,setting_type,setting_value,description,is_active,created_at)
SELECT gen_random_uuid(),'features.scene_render_versioning','render','boolean','true',
       'Enable immutable scene image history and selection.',true,now()
WHERE NOT EXISTS (
    SELECT 1 FROM system.app_settings WHERE setting_key='features.scene_render_versioning'
);

UPDATE system.app_settings
   SET setting_value='true', is_active=true
 WHERE setting_key='features.scene_render_versioning';

UPDATE system.app_settings
   SET setting_value='false'
 WHERE setting_key IN ('features.scene_video_versioning','features.final_video_versioning');

COMMIT;

