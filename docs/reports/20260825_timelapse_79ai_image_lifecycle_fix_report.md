# Báo cáo sửa lỗi Timelapse 79AI image fallback và lifecycle logging

Ngày: 25/08/2026  
Branch: `integration/rdance-on-construction-video-core`

## Phạm vi

Chỉ xử lý hai yêu cầu của Timelapse image:

1. Ngăn lỗi PostgreSQL `22P02 invalid input syntax for type json` khi lưu fallback.
2. Ghi đầy đủ lifecycle event cho các lần gọi 79AI image.

Không thay đổi luồng YEScale, không thay đổi model chain, không tạo hoặc chạy migration, không thay đổi schema/database.

## Nguyên nhân và cách sửa

Nhánh fallback trước đây có thể đưa request/response text chưa chắc là JSON hợp lệ vào repository, trong khi SQL persistence cast hai giá trị này sang `jsonb`. Việc chỉ flush `Utf8JsonWriter` trước đó không xử lý được trường hợp provider trả về raw/malformed response.

Đã bổ sung:

- Parse/validate độc lập request JSON và response JSON trước khi gọi `SaveImageFallbackAsync`.
- Request malformed được chuẩn hóa thành `{}` sau khi loại worker claim.
- Response malformed được bọc thành JSON hợp lệ với `providerResponseParseFailed`, `rawResponse` và `image_model_attempts`, giữ lại chẩn đoán nhưng không đưa raw text vào cột `jsonb`.
- Nếu persistence throw hoặc không cập nhật active attempt, ghi event `TIMELAPSE_IMAGE_FALLBACK_PERSIST_FAILED`, release claim và không ghi nhận fallback thành công.
- `TIMELAPSE_IMAGE_MODEL_FALLBACK` chỉ được ghi sau khi persistence thành công.

## Lifecycle events 79AI image

Runtime hiện ghi các mốc:

- `TIMELAPSE_IMAGE_MODEL_SELECTED`
- `TIMELAPSE_IMAGE_PROVIDER_RESOLVE_BEGIN`
- `TIMELAPSE_IMAGE_PROVIDER_RESOLVED`
- `TIMELAPSE_IMAGE_SUBMIT_BEGIN`
- `TIMELAPSE_IMAGE_SUBMIT_RESPONSE`
- `TIMELAPSE_IMAGE_SUBMIT_FAILED`
- `TIMELAPSE_IMAGE_POLL_RESPONSE`
- `TIMELAPSE_IMAGE_COMPLETED`
- `TIMELAPSE_IMAGE_FALLBACK_JSON_INVALID`
- `TIMELAPSE_IMAGE_FALLBACK_PERSIST_FAILED`
- `TIMELAPSE_IMAGE_MODEL_FALLBACK`

Submit event chỉ ghi metadata cần thiết như stage, attempt, provider/model, endpoint, ratio, mode, resolution, reference flag và prompt length. Không ghi access token, authorization header, base64 image hoặc token credential.

## Test regression

Đã thêm test cho:

- malformed worker-claim request trả về JSON object hợp lệ.
- malformed provider response được bọc thành JSON hợp lệ và giữ raw response trong field JSON đã serialize.
- các lifecycle event tồn tại và đúng thứ tự quanh submit/persistence.

## Validation

Đã chạy:

```text
dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TimelapsePhase2CTests"
Passed: 63, Failed: 0, Skipped: 0

dotnet test TodoX.Dashboard.sln -c Release --no-restore
Passed: 725, Failed: 0, Skipped: 0

dotnet build TodoX.Dashboard.sln -c Release --no-restore
Build succeeded, 0 warnings, 0 errors

dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore
Failed because of pre-existing whitespace diagnostics across unrelated files.
No formatting files were changed.

git diff --check
Passed; Git only reported existing LF/CRLF normalization warnings.
```

## Publish

Command:

```text
dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard
```

Output directory:

```text
D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard
```

Publish result and final commit SHA are recorded after the publish/commit steps complete.

## Changed files

- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`
- `docs/reports/20260825_timelapse_79ai_image_lifecycle_fix_report.md`

## Database and deployment note

No database update or migration is required. The fix guarantees valid JSON reaches the existing JSONB persistence path. This task performs build/publish and Git push only; it does not restart production services.

## Commit

Required commit message:

```text
fix(timelapse): persist fallback safely and log 79ai image lifecycle
```

