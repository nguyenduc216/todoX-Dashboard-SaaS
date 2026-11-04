# RVideo 79AI Fallback Final Hardening Report

Date: 2026-09-12
Branch: `feature/rdn-onepage-ui-revamp`

## Root Cause

The submit reconciliation path treated nearly every `Ai79TaskSubmitException`
that carried an HTTP status as a definitive provider rejection. An ambiguous
79AI response such as HTTP 503/provider-unavailable could therefore trigger
fallback even when the provider may have accepted the task. That risks duplicate
video submissions. In addition, `request_id` and `requestId` were accepted as
pollable task identities even though the polling contract requires an actual
video/task identity.

Submit diagnostics are persisted by the existing centralized
`SceneVideoJobWorker` event path. The event uses sanitized response and request
metadata, so no schema or migration change is required.

## Files Changed

- `TodoX.Web/Services/AiProviders/Ai79TaskClient.cs`
  - Removed `request_id` and `requestId` from accepted async task ID aliases.
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
  - Keeps only valid task identity fields (`id_base`, `task_id`, `taskId`,
    `videoId`, `video_id`).
  - Treats ambiguous 5xx, 429, timeout, and provider-unavailable outcomes as
    pending reconciliation; explicit rejection evidence remains eligible for
    fallback.
  - Preserves sanitized submit diagnostics and existing retry behavior.
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
  - Added regression coverage for ambiguous responses, explicit rejection,
    invalid `request_id`, and valid task IDs overriding error fields.
- `TodoX.Web.Tests/RVideoSceneVideoRecoveryAndDiagnosticsTests.cs`
  - Updated the stale source assertion to cover the current centralized
    diagnostics persistence path.
- `reports/rvideo-79ai-fallback-final-hardening-report.md`
  - This report.

Unrelated pre-existing RDance changes were left untouched:
`DanceSellRepository.cs`, `DanceSellRepositoryTests.cs`,
`RDanceCustomerStatusAndPointsRegressionTests.cs`, and
`docs/TDC-RDN-JOB-LOAD-DEEP-DIAGNOSTIC-20260912-001.md`.

## Event JSON Shape

The existing `render.render_job_events.data_json` event now contains this
shape for a submit failure (values below are illustrative):

```json
{
  "exceptionType": "Ai79TaskSubmitException",
  "provider": "79ai",
  "model": "veo_omni",
  "httpStatusCode": 503,
  "providerErrorCode": "provider_unavailable",
  "sanitizedResponseJson": "...",
  "sanitizedRequestMetadataJson": "...",
  "attemptCount": 3,
  "maxAttempts": 3
}
```

Access tokens, provider credentials, Authorization headers, and other secret
fields are removed before persistence and are not logged.

## Validation

Passed:

- `dotnet format ..\\TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore ...`
- `dotnet test Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideoVideoHotfixTests"`
  - 79 passed, 0 failed.
- `dotnet test ..\\TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests"`
  - 13 passed, 0 failed.
- `dotnet build ..\\TodoX.Dashboard.sln -c Release --no-restore`
  - 0 errors, 46 warnings (existing generated Razor/obsolete warnings).
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\\artifacts\\publish\\todox-dashboard`
  - Succeeded.

The broader RVideo/RenderVideo filter was also run. It exposed four existing
unrelated source/CSS/static billing assertion failures:
`RVideoRuntimeSqlTests` (2), `RVideoAutosaveWorkflowTests` (1), and
`StaticImageBillingPolicyRegressionTests` (1). The targeted hardening and
diagnostics suites pass. No real 79AI provider calls were made.

## Artifact and Commit

Publish output:
`D:\\todoX\\Dashboard-web\\TodoXPortal\\todoX-Dashboard-SaaS\\artifacts\\publish\\todox-dashboard`

No deployment or production database update was performed. The implementation
is present in commit `30e8e9a59ae8afe399a14b8cbf988721f8e34b95` on the current
branch. The commit also contains pre-existing RDance changes from the prior
working session; they were not modified by this task.
