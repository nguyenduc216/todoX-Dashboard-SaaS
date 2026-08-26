# RDance Product Removal Character Reference Report

## Scope

Updated RDance / RDance Fashion on branch `integration/rdance-on-construction-video-core` so removing an optional product image immediately transitions the job to a valid Person Only reference when the character image exists.

## Implementation

- Replaced the product preview-only close icon with a visible text action: `Gỡ ảnh`.
- Added `RemoveProductAndUseCharacterReferenceAsync` to the repository.
- Product fields are cleared atomically with the prepared reference fields.
- The current character media/object key/URL becomes the prepared reference.
- The prepared reference is set to `approved` when the character image is valid; otherwise the job returns to `not_created`.
- Existing reference history is retained, but old versions are deselected.
- Product upload still invalidates the character-only reference and returns to the normal Person + Product generation flow.
- Motion providers, models, and render workflow were not changed.

## Database

- Migration required: **NO**
- No schema or database migration was created or executed.
- The change uses existing nullable product and prepared-reference columns.

## Changed Files

- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRepository.cs`
- `TodoX.Web.Tests/DanceSellPhase2ValidationTests.cs`
- `TodoX.Web.Tests/DanceSellRepositoryTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`

## Validation

- `dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore`: PASS, 0 errors; existing project warnings remain.
- `dotnet test ..\TodoX.Dashboard.sln -c Release --no-restore`: PASS, 771 passed, 0 failed, 0 skipped.
- `dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore --include ...`: PASS.
- `git diff --check`: PASS.
- Mojibake/replacement-character scan on changed RDance files: PASS.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`: PASS.

Publish output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Acceptance Criteria

- Visible text `Gỡ ảnh` action: PASS.
- Remove product clears product state and uses the current character as the video reference: PASS.
- Character reference is automatically `Approved` when valid: PASS.
- Person + Product behavior remains unchanged: PASS by regression coverage.

## Git

- Commit message: `fix(rdance): use character reference after removing product`
- Commit and push are performed after final validation.
