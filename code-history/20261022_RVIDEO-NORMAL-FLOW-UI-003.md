# RVideo normal-flow investigation and remove redundant UI labels

- Date: 2026-10-22. Branch: main. Baseline: f9a5dc321fc9fc5d105f013541997085345be7f1.
- Remove only the two visible selected/latest status lines from RenderVideoJobs. Keep preview, selected video, status/read-model methods, retry/rerender guards and disabled reasons.
- Job investigated: 2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432. No authorized production data connector or matching execution logs were available. Root cause and failed stage remain unverified; no provider failure is asserted.
- No committed changes exist after the supplied baseline. Pending Scene 05 reconciliation guard edits are preserved separately and excluded from this release.
- Source review: AssertJobMaySubmitAsync checks only this job's persisted identities and unresolved intents; its own in-flight token is permitted. A fresh version without provider IDs does not alone satisfy these conditions. This does not establish that production executed this path successfully.
- Exactly one read-only SQL script collects root/child/project jobs, events, scenes, versions, intents, leases and provider identities: database/manual/rvideo-job-2f3664eb-readonly.sql.
- No speculative worker/ownership/Submit Guard change. No tests, production SQL, provider calls, trial renders or production deployment.
- Release build/diff check and publish/hash evidence are reported after execution. Commit SHA: see the commit containing this entry.
