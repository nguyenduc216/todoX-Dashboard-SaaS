# Phase 5.1 Production Configuration Check

Values were not printed or recorded.

| Config item | Status |
| --- | --- |
| Database connection | present |
| KIE provider account credential reference | resolved through provider account resolver; secret value not printed |
| YEScale provider account credential reference | resolved through provider account resolver; YEScale MCP unavailable for live metadata |
| OpenRouter provider account credential reference | resolved through provider account resolver; global `OpenRouter:ApiKey` fallback removed |
| Vertex provider account service-account JSON reference | resolved through provider account resolver; `Vertex:ServiceAccountKeyPath` removed |
| MinIO endpoint/bucket/public base URL | not verified |
| Callback public base URL | not verified |
| Render worker settings | present |
| Poll interval | present |
| Lease duration | not verified |
| Heartbeat interval | not verified |
| Reconciliation worker settings | present |
| Billing feature flags | not verified |
| Point deduction feature flag | not verified |

Readiness impact: provider credential access is now account-scoped in source. External service live smoke and remaining deployment config verification are still required before production deployment.
