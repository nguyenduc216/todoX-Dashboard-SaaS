-- Optional non-secret Phase 2 configuration seed.
-- This script stores only normal configuration metadata. Secrets remain in environment/user-secrets/secret store.

BEGIN;

UPDATE public.todox_ai_provider_capability
   SET config_json = COALESCE(config_json, '{}'::jsonb)
       || jsonb_build_object(
            'danceSell', jsonb_build_object(
                'defaultMode', '720p',
                'allowedModes', jsonb_build_array('720p'),
                'defaultCharacterOrientation', 'image',
                'allowedCharacterOrientations', jsonb_build_array('image'),
                'phase', 'phase2_no_billing'
            )
          ),
       updated_at = now()
 WHERE provider_code = 'kie'
   AND capability_code = 'motion_control_video'
   AND model_name = 'kling-2.6/motion-control';

COMMIT;
