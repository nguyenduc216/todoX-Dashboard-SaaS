-- Manual placeholder for TodoX Dashboard Vbee callback authentication.
-- Replace the placeholder value only during manual execution. Do not commit real secrets.

INSERT INTO public.todox_config (config_key, config_value)
VALUES ('rvideo.vbee.callback_secret', '<set-vbee-callback-secret-manually>')
ON CONFLICT (config_key)
DO UPDATE SET config_value = EXCLUDED.config_value;
