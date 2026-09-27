# RVideo Audio-Only Recovery Endpoint

Date: 2026-09-26

## Operation

`POST /api/rvideo/projects/{projectId}/recover-audio` reevaluates missing or failed external scene audio for one accessible RVideo project. It uses the existing audio auto-chain, Vbee render handler, scene media finalizer, and project finalization flow. Completed selected scene videos are preserved; the operation does not invoke image or video generation.

The application currently authenticates dashboard users through `AuthStateService` in the interactive Blazor circuit, not through an ASP.NET authentication cookie. A standalone browser `fetch` or `curl` request therefore does not inherit `AuthStateService.CurrentUser` and cannot invoke this endpoint as the signed-in dashboard user. The supported dashboard operation is the project-level `Khôi phục Voice` action, which calls `IRVideoAudioRecoveryService` directly from the authenticated circuit.

The HTTP endpoint remains available for a future HTTP authentication integration and has no request body. Do not work around its current authentication boundary by accepting browser-supplied user, tenant, or account identifiers.

The response reports total and eligible scenes, enqueue requests, skips, failures, and safe per-scene outcomes. It returns after enqueue evaluation and does not wait for Vbee completion.
