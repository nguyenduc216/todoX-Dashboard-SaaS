# TDC-RDN-DURATION-FINAL-FIX-20260916-008

## Phạm vi

Task chỉ xử lý việc giữ, reload và hiển thị duration của video RDance. Không thay đổi provider request, render pipeline, auto-render, billing contract, database schema hoặc production deployment.

## Base

- Branch: `main`
- Base SHA: `31c8dcdeb359c39c44021bd338cbe29970fff433`
- `origin/main` tại thời điểm bắt đầu: `31c8dcdeb359c39c44021bd338cbe29970fff433`

## Root cause

Duration được xác định đúng ở bước upload:

`DanceSellPhase2Service.UploadMotionAsync` hoặc `StageTikTokAsync`
-> `DanceSellMotionDuration.TryGetBillableSeconds`
-> `DanceSellRepository.UpdateMotionUploadAsync` / `UpdateMotionTikTokAsync`
-> `dance_sell_jobs.request_json.durationSeconds`.

`QueueRenderAsync` cũng đưa duration vào request JSON của operation. Tuy nhiên khi provider submit bắt đầu:

- `DanceSellRepository.UpdateSubmittedAsync` dùng `request_json=CAST(@requestJson AS jsonb)`, thay toàn bộ JSON job.
- `DanceSellOperationRepository.BeginMotionSubmitAttemptAsync` cũng dùng request JSON submit mới để thay snapshot operation.

Request JSON submit không có `durationSeconds`, nên duration bị mất ở submit transition. Sau reload, RDance Detail không còn giá trị dương để hiển thị, bật warning hoặc truyền vào pricing.

## Last correct / first broken

- Last correct duration stage: sau `UploadMotionAsync` hoặc `StageTikTokAsync`, tại `dance_sell_jobs.request_json.durationSeconds`; và sau `QueueRenderAsync` trong operation request JSON.
- First broken duration stage: submit transition, tại `UpdateSubmittedAsync` và `BeginMotionSubmitAttemptAsync`.

## Thay đổi

Đổi hai phép ghi JSON từ replace sang merge:

```sql
COALESCE(request_json, '{}'::jsonb) || CAST(@requestJson AS jsonb)
```

và tương ứng với operation:

```sql
COALESCE(o.request_json, '{}'::jsonb) || CAST(@requestJson AS jsonb)
```

Các field submit mới vẫn ghi đè field cùng tên; các field persistence trước đó như `durationSeconds` được giữ lại. `submitAttempt` vẫn được cập nhật bằng `jsonb_set`.

## Behavior sau sửa

- Upload MP4/TikTok vẫn xác định duration bằng helper hiện tại và persist server-side.
- Queue không auto-render; `Tạo video` vẫn là entry point render tường minh.
- Submit/render không làm mất `durationSeconds`.
- `GetAsync` sau reload trả lại duration trong `RequestJson`.
- `RDanceJobDetail` đọc duration dương từ job trước, operation sau; warning chỉ còn hiện khi duration thực sự không xác định.
- `FormatDurationLabel` tiếp tục hiển thị `hh:mm:ss`, ví dụ `28` -> `00:00:28`.
- `DanceSellCustomerPricing.EstimateAsync` tiếp tục nhận duration đã resolve; không sửa pricing configuration. Với fixture rate `1.6` và duration `28`, kết quả kỳ vọng là `44.8`.

## Files changed

- `TodoX.Web/Services/DanceSell/DanceSellRepository.cs`
  - Merge `request_json` tại `UpdateSubmittedAsync`.
- `TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs`
  - Merge operation `request_json` tại `BeginMotionSubmitAttemptAsync`.
- `TodoX.Web.Tests/DanceSellRepositoryTests.cs`
  - Regression assertions bảo đảm submit không replace duration của job/operation.

## Validation

- Focused RDance duration tests:
  - Command: `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DanceSellMotionDurationTests|FullyQualifiedName~DanceSellRepositoryTests|FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"`
  - Result: **PASS**, 18 passed, 0 failed.
- `dotnet format TodoX.Web.csproj --verify-no-changes --no-restore --include Services/DanceSell/DanceSellRepository.cs Services/DanceSell/DanceSellAiOperations.cs`
  - Result: **PASS**.
- `dotnet build TodoX.Web.csproj -c Release --no-restore`
  - Result: **PASS**, 0 errors. Existing nullable warnings are emitted from generated Razor code.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`
  - Result: **PASS**.
- `git diff --check`
  - Result: **PASS**.

Phase1B full suite was also run, but contains pre-existing failures outside this task in voice/timelapse/RVideo and legacy source-expectation tests. No files in those protected areas were changed.

## Protected areas

- Provider behavior: **UNCHANGED**
- 79AI/Kling/VEO routing and payload: **UNCHANGED**
- Render pipeline: **UNCHANGED**
- Auto-render: **DISABLED / UNCHANGED**
- `Tạo video`: **EXPLICIT / UNCHANGED**
- Billing, wallet and point contracts: **UNCHANGED**
- Database schema and migrations: **NO CHANGE**
- Production deployment/restart: **NO DIRECT CHANGE**
