-- RVideo scene-video model order (user, 2026-11-10): Veo 3.1 Omni -> Veo 3.1 Fast -> Veo 3.1 Lite
-- -> Gemini Omni Flash 1.2 -> Grok. The new order is the code default (RVideoVideoModelPolicy.Models);
-- services that saved their own order (catalog.services.default_options.job_defaults.videoModelOrder)
-- drop it so every service and every new job follows the new default.
-- Data only, idempotent; other job_defaults keys are kept.

UPDATE catalog.services
   SET default_options = jsonb_set(
           default_options,
           '{job_defaults}',
           (default_options->'job_defaults') - 'videoModelOrder' - 'VideoModelOrder',
           false),
       updated_at = now()
 WHERE jsonb_typeof(default_options->'job_defaults') = 'object'
   AND ((default_options->'job_defaults') ? 'videoModelOrder'
        OR (default_options->'job_defaults') ? 'VideoModelOrder');
