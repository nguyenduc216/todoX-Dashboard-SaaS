# RVideo Audio-Only Recovery Endpoint

Date: 2026-09-26

## Operation

`POST /api/rvideo/projects/{projectId}/recover-audio` reevaluates missing or failed external scene audio for one accessible RVideo project. It uses the existing audio auto-chain, Vbee render handler, scene media finalizer, and project finalization flow. Completed selected scene videos are preserved; the operation does not invoke image or video generation.

The request requires the application's existing authenticated session cookie and has no request body. Example for project 112:

```bash
curl -X POST \
  --cookie "<authenticated-session-cookie>" \
  https://<dashboard-host>/api/rvideo/projects/112/recover-audio
```

The response reports total and eligible scenes, enqueue requests, skips, failures, and safe per-scene outcomes. It returns after enqueue evaluation and does not wait for Vbee completion.
