# TDC-RDN-FASHION-CUSTOM-PRICING-ROOT-CAUSE-FIX-20260915-002

## Phạm vi

Task chỉ sửa đường tính point estimate của RDance `FASHION_VIDEO`. Không thay đổi UI layout/CSS, provider payload, motion upload, render worker, billing charge/refund, wallet, database schema/migration hay dữ liệu production.

Base SHA trước khi sửa: `a9b81c0106413e3ed072c42f80ea9cf9a4afd54a` (`fix(rdance): restore usable detail after successful job load`).

## 1. Call graph trước khi sửa

Đường chạy thực tế là:

```text
RDanceJobDetail.ReloadAsync
  -> TryRefreshEstimateAsync(job)
  -> RefreshEstimateAsync()
  -> EstimatePointPricingAsync(route)
  -> CustomerPricing.EstimateAsync(job, durationSeconds, quality, imageCount)
  -> ResolveServiceAsync(job)
  -> IServiceSellPriceResolver.EstimateAsync(...)
  -> ResolveVideoScenePriceAsync(serviceId, quality, durationSeconds)
  -> Point estimate / _pointEstimate / _displayPoints
  -> Expected Point
```

Trong `RefreshEstimateAsync`, estimate lỗi sẽ đi vào `TryRefreshEstimateAsync`. Wrapper hậu tải tại `RDanceJobDetail.razor` bắt exception, đặt `_estimate`, `_pointEstimate` và `_displayPoints` về `null`, đồng thời gọi `MarkPostLoadWarning`. Vì vậy job vẫn hiển thị được nhưng Expected Point rơi vào trạng thái “Chưa cấu hình”.

## 2. Nguyên nhân chính xác

Pricing cũ dùng `catalog.service_sell_prices` và resolver legacy với các điều kiện:

* asset type `video_scene`;
* quality tương ứng;
* `duration_seconds == durationSeconds` chính xác.

Với video thực tế khoảng 28 giây, resolver tìm `video_scene` có `duration_seconds = 28`. Trong khi custom pricing UI lưu override ở `billing.service_point_rate_override` với:

* resource type `video`;
* quality `standard` hoặc `premium`;
* unit `per_second`;
* rate riêng của service.

Do khác resource contract và không có bản ghi `video_scene` theo đúng duration 28, resolver legacy trả về missing. `DanceSellCustomerPricing` khi đó ném `DANCE_SELL_CUSTOMER_PRICE_NOT_CONFIGURED`. Đây là lỗi lookup ở backend, không phải lỗi render UI và không phải do duration bị mất.

Exception này là lỗi tạo yellow warning hiện tại: `TryRefreshEstimateAsync()` bắt exception từ `RefreshEstimateAsync()` và ghi log `RDance estimate enrichment failed after primary job load...`; sau đó xóa point estimate như mô tả ở trên.

## 3. Identity, quality, duration và precedence

* Service identity được đọc từ persisted `job.RequestJson` bằng `serviceId`/`service_id` và `serviceCode`/`service_code`.
* Service code của flow là `FASHION_VIDEO`; `serviceId` được lấy từ JSON request thực tế và resolve lại bằng `ICoreServiceCatalogService.GetByIdAsync`. Không hard-code ID ví dụ trong prompt.
* Service phải enabled và có `ServiceType == RDance`; nếu request có cả ID và code, ID mismatch bị từ chối bằng `DANCE_SELL_SERVICE_ID_MISMATCH`.
* Resource đúng theo custom pricing contract là `video` (`PointPricingResourceTypes.Video`), không phải `video_scene`.
* Quality giữ nguyên contract hiện tại: `_job.Mode == "premium"` maps tới `premium`; các mode còn lại maps tới `standard`.
* Duration là actual persisted duration. Với case được điều tra là khoảng 28 giây, `ResolveMotionDurationSeconds` đọc `durationSeconds` từ job request trước, sau đó mới dùng operation request; không thêm route/default/synthetic duration.
* Không có duration tier mới và không làm tròn sang tier khác. Rate `per_second` được nhân trực tiếp bởi actual duration trong `PointPricingCalculator`: `videoPoints = videoSeconds * videoRate.Rate`.
* Precedence hiện có của `PointPricingService.ResolveRateAsync` được giữ nguyên: active service override trước, sau đó global active `billing.point_rate_config`. Vì vậy custom `FASHION_VIDEO` rate thắng system/default rate.

Ví dụ đã được test:

* premium `3` x `28` giây = `84` points;
* standard `1.6` x `28` giây = `44.8` points.

## 4. Thay đổi thực hiện

`DanceSellCustomerPricing.EstimateAsync` nay:

1. Giữ legacy fallback cho job không có service identity.
2. Với service RDance đã resolve, gọi `_pointPricing.ResolveRateAsync(service.Id, "video", qualityTier)`.
3. Chỉ resolve image khi `imageCount > 0`; voice RDance vẫn là zero/not-used.
4. Dùng actual duration cùng `PointPricingCalculator`, không thay đổi công thức point.

Các file thay đổi:

* `Services/DanceSell/DanceSellCustomerPricing.cs`: thay service-resolved lookup từ legacy scene price sang point-rate override; giữ service identity validation và fallback.
* `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`: thêm regression tests cho premium/standard, duration 28, service identity, legacy lookup isolation và missing configuration.
* `docs/TDC-RDN-FASHION-CUSTOM-PRICING-ROOT-CAUSE-FIX-20260915-002.md`: báo cáo này.

## 5. Regression tests

Đã thêm/duy trì các kiểm tra rằng:

* `FASHION_VIDEO` + premium custom rate chọn đúng service override;
* `FASHION_VIDEO` + standard custom rate chọn đúng service override;
* duration thực tế 28 giây được nhân theo `per_second`;
* service ID/resource/quality truyền đúng;
* legacy `IServiceSellPriceResolver` không bị gọi với service-resolved FASHION pricing;
* thiếu point rate thật sự vẫn ném `POINT_RATE_NOT_CONFIGURED`;
* flow reload/estimate và chỉ action tạo video vẫn giữ nguyên.

Focused command:

```text
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter RDanceCustomerStatusAndPointsRegressionTests
```

Kết quả: `35 passed, 0 failed, 0 skipped`.

## 6. Validation

* Format:
  `dotnet format TodoX.Web.csproj --verify-no-changes --no-restore --include Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
  - đạt, exit code 0.
* Build:
  `dotnet build TodoX.Web.csproj -c Release --no-restore`
  - đạt, `0 Error(s)`, `45 Warning(s)` từ Razor generated code nullable hiện hữu.
* Publish:
  `dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`
  - đạt, `0 Error(s)`; output directory là `artifacts/publish/todox-dashboard`.
* Diff whitespace:
  `git diff --check`
  - đạt.

Không chạy production smoke test và không ghi production database theo yêu cầu task.

## 7. Database, production và protected areas

* Database/schema/migration: **NO**. Không tạo, sửa hoặc chạy migration; không sửa pricing record.
* Production changes: **NONE**. Chưa deploy, chưa restart service, chưa chạy production smoke test.
* YEScale/provider MCP: không liên quan; không thêm/sửa YEScale provider hoặc model.
* Không thay đổi 79AI/KIE contract/payload, motion upload/TikTok/reference generation/approval, render worker, billing transaction, wallet/charging/refund, RVideo, Timelapse, RDance UI layout/CSS hay behavior isolation của commit `a9b81c0`.

## 8. Git và push

Commit message yêu cầu: `fix(rdance): restore fashion custom pricing estimate`.

Final SHA, trạng thái push và remote HEAD sẽ được cập nhật sau khi commit/push hoàn tất. Chỉ ba file task được phép stage; không stage `bin/`, `obj/`, publish artifacts, credentials hoặc file ngoài phạm vi.

## 9. Checklist cuối

- [x] Xác định exact call graph và exception.
- [x] Xác định `FASHION_VIDEO`, service ID từ request JSON, resource `video`, quality và duration thực tế.
- [x] Sửa root cause ở service pricing, không patch UI.
- [x] Có regression test cho custom pricing và missing configuration.
- [x] Focused tests đạt.
- [x] Format và build đạt.
- [x] Publish đạt.
- [x] Không có database/production/provider/billing change.
- [ ] Commit đúng message và verify SHA.
- [ ] Push đúng `origin/feature/rdn-onepage-ui-revamp` và verify remote HEAD.
