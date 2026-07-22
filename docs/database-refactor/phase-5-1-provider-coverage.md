# Phase 5.1 Provider Coverage

No active provider workflow is reported as `complete` for production readiness. Several paths compile and have source-level coverage, but Phase 5.1 requires proof that every active submission path is account-lease-first, credential-resolver-first, idempotent, and DB-race-tested.

Main blockers:

- `YEScaleTaskClient` still reads global `YEScaleOptions.AccessKey`.
- `KieClient` can still use `KieOptions.ApiKey`/`KIE_API_KEY` directly.
- `OpenRouterImageService` still falls back to `OpenRouter:ApiKey`.
- `VertexClient` still uses Vertex configuration/service-account token flow directly.
- DB integration suites for wallet locking, provider leases, and callback/poll races are skipped.

Final provider coverage status: `blocked`.
