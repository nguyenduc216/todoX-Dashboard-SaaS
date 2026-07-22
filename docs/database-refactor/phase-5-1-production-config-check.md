# Phase 5.1 Production Configuration Check

Values were not printed or recorded.

| Config item | Status |
| --- | --- |
| Database connection | present |
| KIE_API_KEY | not verified |
| YEScale credential reference | not verified |
| OpenRouter credential reference | not verified |
| Vertex credential reference/service-account location | present but production-blocking until account resolver migration |
| MinIO endpoint/bucket/public base URL | not verified |
| Callback public base URL | not verified |
| Render worker settings | present |
| Poll interval | present |
| Lease duration | not verified |
| Heartbeat interval | not verified |
| Reconciliation worker settings | present |
| Billing feature flags | not verified |
| Point deduction feature flag | not verified |

Readiness impact: NO-GO until all active provider credentials are account-scoped and config references are verified without exposing values.
