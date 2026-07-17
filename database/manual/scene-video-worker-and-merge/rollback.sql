\set ON_ERROR_STOP on

-- No destructive rollback is provided for scene-media-versioning tables here.
-- Roll back provider seed separately and redeploy older application build if needed.

SELECT 'MANUAL_ROLLBACK_REQUIRED' section,
       'Revert app build and unset YEScale video provider rows if you need to back out.' AS note;
