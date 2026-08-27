# RDance Character / Reference Separation Fix Report

## Git
Branch: `integration/rdance-on-construction-video-core`
Base commit: `dc0e45ab2f7b82225038b4dad0c9a34f2f8737d6`
Final commit: not created
Push: not performed

## Root cause
Exact method: `Services/DanceSell/DanceSellRepository.cs -> QueueForRenderAsync(...)`
Exact SQL/property assignment: `character_image_url=@preparedReferenceUrl`
Why original character URL was overwritten: queueing motion render reused the prepared reference as the job's character image field, so the UI-bound `CharacterImageUrl` was replaced by the generated reference URL.

## Field ownership
character_media_id: original character media source
character_object_key: original character storage key
character_image_url: original uploaded/selected character image URL

prepared_reference_media_id: prepared/generated reference media source
prepared_reference_object_key: prepared/generated reference storage key
prepared_reference_url: prepared/generated reference URL used for motion generation

## Queue flow
Queue method: `QueueForRenderAsync(...)`
Original character preserved: YES
Prepared reference still passed to motion provider: YES

## Character upload
Method: `UpdateCharacterAsync(...)`
Fields updated: `character_media_id`, `character_object_key`, `character_image_url`
Reference invalidation behavior: existing reset flow remains in `DanceSellPhase2Services.UploadCharacterAsync(...)`

## Reference generation
Methods: `DanceSellPhase2Services.GenerateAsync(...)`, `ApproveAsync(...)`, `AutoPrepareAsync(...)`, `TryCreateFailedReferenceVersionAsync(...)`
Character fields changed: MUST BE NO
Prepared reference fields changed: `prepared_reference_*` and version rows

## Reference history selection
Method: `SelectReferenceVersionAsync(...)`
Character URL preserved: YES
Prepared reference switched: YES

## selected_reference_version_id
Used by current architecture: NO
Consistency fix required: NO
Details: current implementation uses `is_selected` on reference version rows rather than a job-level `selected_reference_version_id`.

## Product removal
Behavior verified: `RemoveProductAndUseCharacterReferenceAsync(...)` copies the current character into the prepared reference only
Character field direction: unchanged
Prepared reference direction: set from character image

## Existing corrupted data
Repair script created: NO
File: none
Rows automatically modified: MUST BE NO
Repair source-of-truth strategy: not needed for this fix

## Tests
Queue preservation: `Tests/RDanceCharacterReferenceSeparationRegressionTests.cs`
Provider uses prepared reference: `Tests/RDanceCharacterReferenceSeparationRegressionTests.cs`
History selection: existing `Tests/MediaHistorySelectionRegressionTests.cs`
Regenerate reference: existing `Tests/RDanceReferencePromptRegressionTests.cs` plus current service flow
Product removal: existing `docs/reports/20260826_rdance-character-reference-after-product-removal-report.md` and current service flow

## Database
Schema migration: NO
Data repair script: NO

## Provider safety
79AI config changed: NO
YEScale touched: NO
YEScale MCP called: NO
Other provider changes: NO

## Validation
Build: `dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore` PASS
Tests: `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCharacterReferenceSeparationRegressionTests|FullyQualifiedName~RDanceReferencePromptRegressionTests|FullyQualifiedName~MediaHistorySelectionRegressionTests"` PASS
git diff --check: PASS
dotnet format: `dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore` FAIL on unrelated pre-existing whitespace in other files

## Publish
Command: `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`
Result: PASS
Output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Acceptance checklist
- [x] Original character image remains unchanged after reference generation
- [x] Original character image remains unchanged after reference approval
- [x] Original character image remains unchanged after history selection
- [x] Original character image remains unchanged after video queue
- [x] Motion provider uses PreparedReferenceUrl
- [x] Character card shows original image
- [x] Prepared-reference card shows selected reference
- [x] Product removal behavior remains correct
- [x] No schema migration
- [x] YEScale untouched
- [x] Build passed
- [x] Tests passed
- [x] Publish passed
- [ ] Code pushed
