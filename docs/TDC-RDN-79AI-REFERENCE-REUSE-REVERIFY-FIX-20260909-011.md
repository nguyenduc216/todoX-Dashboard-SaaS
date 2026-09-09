# TDC-RDN-79AI-REFERENCE-REUSE-REVERIFY-FIX-20260909-011

## 1. Scope

Fix only RDance 79AI provider reference-image reuse across render attempts.

Protected areas not changed:

- RDance UI and create flow
- database schema and migrations
- API contracts and 79AI endpoints
- multipart upload implementation
- Motion Control payload contract
- KIE, other providers, render worker
- billing, points, wallet, storage architecture
- retry architecture outside provider asset reuse

## 2. Baseline

- Branch: `feature/rdn-onepage-ui-revamp`
- Baseline commit: `094914a`
- Regression job: `450355fe-2d0b-443a-8f5a-3b271c8fe0f8`
- Historical regression source: `af83613`

## 3. Root Cause

`af83613` changed reference asset lookup from render-job scope to DanceSell-job scope. A verified provider asset from an earlier render attempt could therefore be reused by a new attempt without live provider verification.

## 4. Call Graph Before Fix

`HandleAsync`
-> `Submit79AiAsync`
-> `GetLatestAssetAsync(danceJob.Id, ...)`
-> `IsVerifiedProviderAsset`
-> reuse stored provider URL
-> `SubmitMotionControlAsync`

When no stored asset was found or it was locally unverified:

`ResolveMotionFileAsync`
-> `UploadMediaAsync`
-> `VerifyProviderImageAsync`
-> `ListImagesAsync`
-> persist asset
-> `SubmitMotionControlAsync`

## 5. Exact Code Changed

Files:

- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`

`Submit79AiAsync` now:

1. Looks up an asset for the current `renderJobId`.
2. Reuses a verified current-attempt asset without another upload.
3. Falls back to a DanceSell-job asset only when no current-attempt asset exists.
4. Live-verifies the previous-attempt asset through the existing `VerifyProviderImageAsync` and `ListImagesAsync` path.
5. Requires both stored provider `idBase` and provider URL to match the live verification.
6. Falls through to the existing `UploadMediaAsync` branch when verification fails or identity mismatches.

No new upload implementation or provider endpoint was added.

## 6. Behavior Before and After

Before:

```text
New attempt
-> find latest verified asset for DanceSell job
-> reuse stored provider URL
-> Motion Control
```

After:

```text
New attempt
-> find verified asset for current render job
-> reuse if valid

No current-attempt asset
-> find prior-attempt asset
-> live verify with provider list
-> identity match
   -> reuse
identity mismatch / not found / verification failure
   -> upload binary with existing UploadMediaAsync
   -> verify new upload
-> Motion Control
```

## 7. Current-Attempt Reuse

Current-attempt assets remain reusable when the existing local verification metadata is valid. This preserves idempotent behavior and avoids duplicate uploads within the same render attempt.

## 8. Previous-Attempt Reuse

Previous-attempt assets are never trusted solely from stored metadata. They must pass live provider verification before reuse.

## 9. Live Verification

The existing `VerifyProviderImageAsync` helper calls the configured 79AI image-list endpoint and requires a matching provider item with normalized `SUCCESS` status. The fix additionally requires the returned `idBase` and URL to match the stored asset identity.

## 10. Fresh Upload Fallback

If live verification fails, the existing branch executes:

`ResolveMotionFileAsync`
-> `UploadMediaAsync`
-> `VerifyProviderImageAsync`
-> persist verified provider asset
-> Motion Control

## 11. Idempotency

Motion upload reuse, render-attempt creation, provider task handling, billing, and retry orchestration were not refactored. Only reference-image asset reuse selection and re-verification changed.

## 12. Tests

Added regression coverage for:

- current render-job asset lookup
- previous-attempt fallback lookup
- live re-verification before reuse
- identity validation
- fresh upload fallback ordering

Targeted regression test:

```text
Passed:
  DanceSellPreviousAttemptReferenceMustBeLiveReverifiedBeforeReuse
  DanceSell79AiMotionSubmitUsesRouteFieldsAndProviderMode
```

The broader selected test run had one pre-existing failure in `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`; its prompt expectation does not match the current provider implementation and is unrelated to this change.

## 13. Build

Command:

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
```

Result: PASS, 0 errors. Existing generated Razor nullable warnings remain.

## 14. Publish

Command:

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Result: PASS. Published successfully.

Output directory:

`artifacts/publish/todox-dashboard`

## 15. Diff Check

Command:

```text
git diff --check
```

Result: PASS.

## 16. Runtime Verification

No live 79AI task or production retry was executed.

`RUNTIME VERIFICATION: NOT VERIFIED`

## 17. Commit and Push

Implementation commit SHA: PENDING

Commit message:

`TDC-RDN-79AI-REFERENCE-REUSE-REVERIFY-FIX-20260909-011`

Remote:

`origin/feature/rdn-onepage-ui-revamp`

Push result: PENDING

## 18. Working Tree

Unrelated pre-existing changes in other video-render files were not modified or staged.

Final status: PENDING
