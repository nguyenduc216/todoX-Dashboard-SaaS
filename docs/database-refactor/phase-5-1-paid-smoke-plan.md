# Phase 5.1 Controlled Paid Smoke Plan

Paid provider smoke: NOT EXECUTED - awaiting explicit user approval.

- Provider/model: KIE `kling-2.6/motion-control`
- Estimated provider cost: confirm from KIE account/docs before execution.
- Estimated TodoX points: compute through `IAiBillingService.Estimate` after config verification.
- Input: one small product image, one character image, one approved short MP4 dance source URL, 720p mode.
- Expected outputs: provider task id, render artifact video URL, usage log, billing completion, provider attempt, operation log timeline.
- Expected events: `provider_account_claimed`, `credential_resolved`, `billing_reserved`, `provider_submitted`, `provider_completed`, `usage_finalized`, `billing_completed`, `lease_released`, `job_completed`.
- Approval checkpoint: user must explicitly approve the paid call and the expected point deduction before execution.
- Cleanup: keep immutable audit rows; cancel/release active lease if provider task fails; refund only through `IAiBillingService.RefundAsync` if policy allows.
