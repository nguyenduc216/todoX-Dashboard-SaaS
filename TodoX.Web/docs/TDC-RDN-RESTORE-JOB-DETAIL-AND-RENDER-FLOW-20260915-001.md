# TDC-RDN-RESTORE-JOB-DETAIL-AND-RENDER-FLOW-20260915-001

## 1. Current code path before fix

Trước fix, `RDanceJobDetail.razor` chạy `ReloadAsync` theo luồng:

`DanceSell.GetAsync(JobId, currentUser)` -> `RenderJobs.GetAsync` nếu có `RenderJobId` -> `DanceRepo.ListReferenceVersionsAsync` -> `RefreshEstimateAsync` -> `ContinueAutoFinishAsync` -> render UI.

## 2. Exact post-load failure mechanism

Sau khi primary job load đã thành công, các bước enrichment nằm chung trong một `try/catch`. Nếu `RenderJobs.GetAsync`, `ListReferenceVersionsAsync`, `RefreshEstimateAsync` hoặc `ContinueAutoFinishAsync` throw exception, catch này set `_loadError = DetailRefreshErrorMessage`.

Markup đầu trang ưu tiên `_loadError`, nên UI hiển thị lỗi fatal toàn trang: `Không thể tải đầy đủ thông tin video. Vui lòng thử tải lại.`

## 3. Root cause

Trang trộn lỗi mandatory primary job load với lỗi optional/post-load enrichment. `_job` đã có dữ liệu hợp lệ nhưng `_loadError` vẫn khiến UI ẩn job-detail.

## 4. Why DanceSell.GetAsync PASS but page was unusable

`DanceSell.GetAsync` PASS và `_job` được gán thành công. Sau đó một bước enrichment không bắt buộc có thể fail và set `_loadError`, làm nhánh render chuyển sang alert fatal thay vì hiển thị `_job`.

## 5. Exact files changed

- `TodoX.Web/Components/Pages/RDanceJobDetail.razor`
- `TodoX.Web/Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `TodoX.Web/docs/TDC-RDN-RESTORE-JOB-DETAIL-AND-RENDER-FLOW-20260915-001.md`

## 6. Exact methods changed

- `ReloadAsync`
- thêm `TryLoadRenderJobAsync`
- thêm `TryLoadReferenceVersionsAsync`
- thêm `TryRefreshEstimateAsync`
- thêm `TryContinueAutoFinishAsync`
- thêm `MarkPostLoadWarning`
- cập nhật regression tests trong `RDanceCustomerStatusAndPointsRegressionTests`

## 7. Mandatory vs optional load dependencies

Mandatory cho page load:

- auth/current user
- `DanceSell.GetAsync(JobId, currentUser)`
- job identity/security/ownership

Optional cho page load:

- historical render job display từ `RenderJobs.GetAsync`
- reference-version history từ `ListReferenceVersionsAsync`
- estimate/pricing preview từ `RefreshEstimateAsync`
- auto-finish continuation

Mandatory tại render validation:

- motion media/url/source stage ready
- persisted/resolved duration > 0
- approved prepared reference URL
- backend `QueueRenderAsync` validation, pricing/points checks và provider prerequisites

## 8. Before flow

Primary load thành công nhưng bất kỳ post-load exception nào cũng set `_loadError`, khiến trang chuyển sang fatal/general error.

## 9. After flow

Primary load thành công thì `_job` luôn được giữ và hiển thị. Mỗi enrichment optional có `try/catch` riêng, log warning riêng và chỉ set `_postLoadWarning`.

## 10. Render behavior

Không bypass render validation. `QueueRenderFromUserActionAsync` vẫn gọi `DanceSell.GetAsync`, kiểm tra approved reference, rồi gọi `DanceSell.QueueRenderAsync`.

## 11. Upload does NOT auto-render

Không thay đổi upload flow. Upload/stage video vẫn không gọi `QueueRenderAsync`.

## 12. `Tạo video` remains explicit trigger

`Tạo video` vẫn là trigger khách hàng cho render. Tests tiếp tục khóa việc `QueueRenderAsync` chỉ nằm trong `QueueRenderFromUserActionAsync`.

## 13. Duration behavior

Không đổi. Render vẫn yêu cầu duration persisted/resolved > 0 qua `HasValidMotionDuration` và backend validation.

## 14. FASHION_VIDEO pricing behavior

Không đổi. Estimate hiển thị vẫn dùng `IDanceSellCustomerPricing`, persisted service identity và route/provider mode hiện có. Pricing/points bắt buộc vẫn do backend render queue path xử lý.

## 15. Tests

- `dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter RDanceCustomerStatusAndPointsRegressionTests`: PASS, 31 passed.
- `dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore`: FAIL, 507 passed / 17 failed. Failures nằm ở RVideo, Timelapse, TodoXVideo prompt parser và RDance reference prompt static assertions hiện hữu ngoài phạm vi thay đổi này.

## 16. Build

`dotnet build TodoX.Web.csproj -c Release --no-restore`: PASS, 45 warnings từ Razor generated `AiProviders_razor.g.cs`.

## 17. Publish

`dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: PASS.

Verified: `artifacts/publish/todox-dashboard/TodoX.Web.dll` exists.

## 18. git diff --check

PASS.

## 19. Database/schema changes

NO.

## 20. Provider changes

NO.

## 21. Billing/points changes

NO.

## 22. Base SHA

`bfa8b94b13a797e0065b660112f988ec9b22257d`

## 23. Final SHA

Recorded after commit by `git log -1 --oneline`; exact SHA is reported in the final task response.

## 24. Push result

Pending at report creation time.

## 25. Remote HEAD

Pending at report creation time.

## 26. Final checklist

- Primary load and post-load enrichment separated: YES
- `_job` retained after post-load failure: YES
- null `RenderJobId` before first render remains valid: YES
- null `ResultVideoUrl` before completion remains valid: YES
- upload does not auto-render: YES
- `Tạo video` remains explicit render trigger: YES
- database migration: NO
- provider contract/payload changes: NO
- billing/points contract changes: NO
