# Character Customer UI and Upload Flow Report

## Scope

Implemented the customer-facing Character UI cleanup and direct image upload flow.
Timelapse, RDance, RVideo, billing, provider routing, provider credentials, and
database schema were not changed.

## Changes

- Removed customer-visible seed, provider selector, provider/model badge, provider
  labels, model details, and technical render metadata.
- Removed the reference-image URL input.
- Added a separate reference-image upload section with preview, replacement, and
  removal actions. The browser file control is visually hidden; customers click
  the cloud-upload icon to open the picker.
- Uploading from the new Character page first creates a draft Character
  automatically, then persists the selected image against that Character.
- Kept reference/source image storage separate from the master image fields.
- Reused the existing media storage service and configured
  `MediaStorage:MaxImageBytes` limit.
- Added JPEG, PNG, and WEBP MIME plus file-signature validation for both reference
  and master uploads.
- The saved reference collection remains the render input through
  `ReferenceImageUrls`; uploading the reference does not overwrite the master
  image.
- Added ownership-scoped reference upload/removal operations and immediate
  Character reload for preview updates.
- Added regression tests for customer UI visibility, upload persistence,
  supported formats, invalid MIME/signature, size limits, icon-only picker
  behavior, and master/reference separation.

## Database

No migration was created or executed. Existing Character and reference tables and
columns are reused.

## Validation

Passed:

```text
dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiCharacterCustomerUiTests
8 passed, 0 failed

dotnet build TodoX.Dashboard.sln -c Release --no-restore
0 errors

dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web/Services/AiCharacters/AiCharacterService.cs TodoX.Web/Services/AiCharacters/AiCharacterRepository.cs
passed

git diff --check
passed
```

The full Web test suite completed with 653 passed and 3 pre-existing failures
outside this Character task:

- RVideo routing expectation
- RDance source assertion about `InfiniteTimeSpan`
- Render video jobs tab-count expectation

Because the full suite is not green for unrelated existing failures, deployment
was not performed and the result is:

**READY TO DEPLOY: NO**
