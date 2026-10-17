# Landing Intro Video V1 — verification record

- Branch: `feature/landing-intro-video`; base: `b418669dc8a5adb448715b2d13626b528a015bfc`.
- Scope: Landing hero phone media, Dashboard Landing Video Intro settings, PostgreSQL migration, SharedMedia subfolders.
- Security: write services enforce the existing Landing Industry update permission through the circuit's `AuthStateService`; anonymous/view-only/customer sessions cannot call write methods. No public write endpoint is registered.
- Data integrity: uploads use unique names, temporary cleanup and do not delete existing assets. SQL upsert is atomic. Uploaded files not saved in configuration remain unreferenced; operator may clean them after verifying no references.
- Migration: `database/manual/todox_landing_intro_video_settings.sql`, additive PostgreSQL DDL with `on conflict do nothing`. **Not run against any database**; operator must review and execute before enabling the feature.
- Available checks: 20 feature xUnit cases passed; both Release builds passed; `node --check` and `git diff --check` passed. Dashboard build reported 56 warnings (including existing ImageSharp security advisories); Landing build reported none.
- Unverified gates: disposable PostgreSQL migration/read-write test, HTTP integration test, FFmpeg conversion and file-write failure tests, genuine browser playback/viewport/screenshots, end-to-end role authorization and shared IIS media mapping. Staging artifacts are not production-tested.
- Deployment: configure identical `SharedMedia:StorageRoot` and `/media` mapping on both apps, ensure app pool read/write permissions, install FFmpeg when transcoding is enabled, run migration after review, test staging via browser, then obtain operator approval before deploying production.