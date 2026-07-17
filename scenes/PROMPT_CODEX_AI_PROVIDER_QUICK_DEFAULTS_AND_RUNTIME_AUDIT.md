# PROMPT CODEX — CÀI ĐẶT NHANH PROVIDER/MODEL MẶC ĐỊNH VÀ RÀ SOÁT RUNTIME

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Commit đã khảo sát: `fca4fdb7b6477f33854dc9284f6f2a50209d4855`. Nếu working tree mới hơn, phải kiểm tra lại toàn bộ trước khi sửa.

## 1. Mục tiêu

Bổ sung vào trang **AI Providers** một khu vực **Cài đặt nhanh Provider mặc định** cho năm chức năng:

1. Render ảnh Avatar.
2. Render ảnh Character.
3. Render ảnh Scene.
4. Avatar Builder.
5. Render Video.

Mỗi chức năng có:

- Dropdown Provider.
- Dropdown Model phụ thuộc Provider đã chọn.
- Hiển thị capability thực tế, trạng thái provider/model, đơn giá điểm nếu có.
- Nút lưu riêng hoặc một nút `Lưu cài đặt mặc định` có transaction/validation an toàn.
- Sau khi lưu, mọi runtime tương ứng phải resolve đúng provider/model đó.

Đây chỉ là giao diện trực quan trên cấu trúc AI Provider/Capability hiện có. Không tạo một hệ cấu hình song song.

## 2. Quy tắc bắt buộc

1. Đọc và tuân thủ toàn bộ `AGENTS.md` đúng scope.
2. Kiểm tra `git status`, commit mới nhất, bảo toàn thay đổi ngoài phạm vi.
3. Không hard-code secret, provider ID, capability ID hoặc model vào code.
4. Không tạo migration và không tự chạy SQL/database.
5. Nếu schema hiện tại thật sự không biểu diễn được yêu cầu, dừng phần schema, báo rõ và chỉ tạo SQL idempotent riêng để chủ hệ thống duyệt/chạy sau.
6. Không deploy production. Phải test, build Release và publish artifact riêng.
7. Không sửa tiếng Việt bằng script thay thế hàng loạt; mọi file phải UTF-8 và không phát sinh mojibake.

## 3. Kiến trúc hiện tại phải tái sử dụng

Đã khảo sát thấy:

- `AiProviderCapability` có `CapabilityCode`, `ModelName`, `IsDefault`, `Enabled`, `AllowUserSelect`, `UnitCostPoints`.
- `IAiProviderService` đã có:
  - `GetCapabilitiesAsync(...)`
  - `GetSelectableProvidersAsync(...)`
  - `GetDefaultProviderAsync(...)`
  - `SetDefaultCapabilityAsync(...)`
  - `ResolveProviderForCapabilityAsync(...)`
  - `LogUsageAsync(...)`
- `SetDefaultCapabilityAsync` đang hướng tới quy tắc một default cho mỗi `capability_code`.
- `AiProviderUsageLog` đã có `ProviderId`, `ProviderCapabilityId`, `ProviderCode`, `CapabilityCode`, `FeatureCode`, `ModelName`, `RequestId`, `JobId`, cost, point và metadata.

Vì vậy:

- Không tạo bảng `quick_settings` mới nếu không cần.
- Một lựa chọn Model phải trỏ tới đúng **provider capability row** hiện có.
- Lưu mặc định bằng cơ chế `SetDefaultCapabilityAsync` hiện có.
- Runtime tiếp tục gọi `ResolveProviderForCapabilityAsync`.
- UI quick settings chỉ là một projection/editor dễ dùng của dữ liệu capability hiện tại.

## 4. Rà soát và chốt mapping chức năng → capability

Không được đoán mapping chỉ từ tên. Tìm tất cả call site trước rồi lập bảng mapping thực tế.

Các capability trong catalog hiện có gồm:

- `avatar_generation`
- `chibi_avatar_generation`
- `character_generation`
- `image_generation`
- `scene_image_generation`
- `text_to_video`
- `image_to_video`
- các capability ảnh khác.

Mapping dự kiến cần xác minh:

| Chức năng UI | Capability dự kiến | Yêu cầu kiểm tra |
|---|---|---|
| Render ảnh Avatar | `avatar_generation` | Avatar update/Admin Avatar/public avatar đang gọi capability nào |
| Render ảnh Character | `character_generation` | AiCharacterService và màn tạo/render Character |
| Render ảnh Scene | `scene_image_generation` | Cả auto batch sau tách scene và render lại từng scene |
| Avatar Builder | `chibi_avatar_generation` hoặc capability thực tế riêng | Không dùng chung `avatar_generation` nếu cần cấu hình độc lập |
| Render Video | `image_to_video` cho scene từ ảnh | Xác minh handler/provider video thật; nếu có text-to-video thì báo riêng |

Nếu Avatar và Avatar Builder hiện dùng cùng một capability thì không thể có hai default độc lập chỉ bằng `IsDefault`. Khi đó phải:

1. Ưu tiên dùng capability riêng đã có như `chibi_avatar_generation` nếu đúng nghiệp vụ.
2. Cập nhật call site Avatar Builder resolve capability riêng đó.
3. Không tạo setting giả trong UI nhưng cả hai vẫn đổi chung một default.
4. Viết test chứng minh đổi Avatar Builder không làm đổi Avatar và ngược lại.

Đối với Video, yêu cầu hiện tại là video scene được tạo từ selected image, do đó ưu tiên `image_to_video`. Không gộp `text_to_video` nếu runtime khác nhau. Nếu sản phẩm cần cả hai sau này, kiến trúc UI phải dễ mở rộng nhưng vòng này chỉ cấu hình đường video thực tế đang dùng.

Tạo một catalog code dùng chung, ví dụ `AiFeatureProviderCatalog`, chứa:

- Feature key ổn định.
- Tên tiếng Việt.
- Capability code.
- Loại media.
- Mô tả ngắn.
- Thứ tự hiển thị.

Không rải string capability ở nhiều component.

## 5. Giao diện Cài đặt nhanh trên AI Providers

Đặt một card/panel dễ thấy ở đầu trang hoặc ngay trên phần chỉnh chi tiết provider:

```text
CÀI ĐẶT NHANH PROVIDER MẶC ĐỊNH

Chức năng             Provider              Model                 Trạng thái
Ảnh Avatar            [YEScale ▼]           [nano-banana-2 ▼]     Đang dùng
Ảnh Character         [YEScale ▼]           [nano-banana-2 ▼]     Đang dùng
Ảnh Scene             [YEScale ▼]           [nano-banana-2 ▼]     Đang dùng
Avatar Builder        [YEScale ▼]           [nano-banana-2 ▼]     Đang dùng
Video từ ảnh          [YEScale ▼]           [grok-video ▼]        Đang dùng

                                          [Lưu cài đặt mặc định]
```

### 5.1. Provider dropdown

- Chỉ liệt kê provider `Enabled=true`.
- Provider phải có ít nhất một capability enabled khớp chức năng.
- Hiển thị `ProviderName`, có thể kèm `ProviderCode` nhỏ phía dưới.
- Không hiển thị provider không hỗ trợ capability đó.

### 5.2. Model dropdown phụ thuộc Provider

- Khi đổi Provider, tải/lọc các capability rows của provider đó với đúng `capability_code` và `Enabled=true`.
- Mỗi option hiển thị `ModelName`, `DisplayName`, điểm/đơn vị nếu có.
- Không lấy model từ một danh sách hard-code.
- Nếu provider chỉ có một model hợp lệ, tự chọn model đó nhưng vẫn hiển thị rõ.
- Nếu không có model, báo `Provider chưa có model được bật cho chức năng này` và không cho lưu.

Kiểm tra schema/index hiện tại có cho phép nhiều row cùng provider + capability nhưng khác model hay không. Nếu có, dùng trực tiếp. Nếu không, không giả lập danh sách model; báo giới hạn schema và chuẩn bị đề xuất SQL riêng, không chạy.

### 5.3. Trạng thái hiện tại

Khi load trang:

- Gọi `GetDefaultProviderAsync(capabilityCode)` cho từng chức năng.
- Chọn đúng Provider và capability/model đang `IsDefault=true`.
- Hiển thị cảnh báo nếu default đang disabled, provider disabled, model trống hoặc không có default.
- Hiển thị fallback priority hiện tại chỉ để chẩn đoán, không trình bày nó như default đã lưu.

### 5.4. Lưu

- Giá trị lưu chính là `ProviderCapabilityId` của model đã chọn.
- Dùng `SetDefaultCapabilityAsync` hiện có.
- Không update `IsDefault` rời rạc ở UI.
- Repository phải đảm bảo một default duy nhất trên mỗi capability trong transaction.
- Chỉ admin có quyền thay đổi.
- Audit người thay đổi, thời gian, capability, old provider/model và new provider/model.
- Nếu lưu nhiều dòng cùng lúc và một dòng lỗi, chọn transaction toàn bộ hoặc trả kết quả chi tiết; không để UI báo thành công chung khi một phần thất bại.
- Sau lưu phải reload từ DB và hiển thị đúng giá trị đã persist, không chỉ giữ state local.

## 6. Rà soát runtime — cài đặt phải thật sự được sử dụng

Tìm và lập bảng tất cả entry point của năm chức năng. Với mỗi entry point, ghi:

- Component/API/handler gọi vào.
- Feature code.
- Capability code.
- Resolver được gọi.
- Provider/model nhận được.
- Usage log được ghi ở đâu.
- Có đường hard-code/bypass hay không.

### 6.1. Quy tắc runtime bắt buộc

Mọi đường render phải theo flow:

```text
Feature
→ capability code chuẩn
→ ResolveProviderForCapabilityAsync
→ ProviderOptionDto/ProviderCapabilityId
→ router/factory đúng provider
→ adapter gọi API
→ usage/billing/version log với provider/model thực tế
```

Không được:

- Hard-code provider/model trong page hoặc handler.
- Gọi thẳng provider service mà bỏ qua resolver.
- Resolve default nhưng sau đó adapter lại dùng model từ appsettings khác.
- Im lặng fallback sang Google/OpenRouter/YEScale khi default lỗi.
- Ghi provider/model theo giá trị dự kiến thay vì kết quả thực tế.

### 6.2. Điểm cần kiểm tra đặc biệt từ code hiện tại

`SceneImageRenderService` hiện có nhánh `RenderSceneImageWithVertexAsync` gọi trực tiếp creative render cho batch/auto, trong khi nhánh routed dùng `scene_image_generation`.

Phải kiểm tra cả:

- Auto render ảnh sau khi tách scene.
- Batch render ảnh còn thiếu.
- Render lại một scene.
- Retry quota.

Nếu mục tiêu là cài đặt nhanh `Ảnh Scene` điều khiển toàn bộ chức năng, cả batch và manual phải resolve cùng default `scene_image_generation`. Không được để batch vẫn luôn Vertex còn render lại mới dùng setting.

Retry của cùng logical request phải giữ nguyên provider/model snapshot ban đầu, không resolve lại sang provider khác giữa các attempt và không charge hai lần.

### 6.3. Avatar và Avatar Builder

Kiểm tra:

- `AdminAvatarManager.razor`
- avatar update/public avatar nếu có.
- `AvatarBuilder.razor`
- `AvatarTemplateService`.

Mỗi chức năng phải dùng capability riêng đã chốt. Không để Avatar Builder chọn provider từ UI nhưng service vẫn dùng provider/model hard-code trong config.

### 6.4. Character

Kiểm tra `AiCharacterService`, `AiImageProviderFactory`, UI tạo/render character và mọi background handler. Tất cả phải resolve `character_generation` và truyền đúng `ProviderCapabilityId` vào router.

### 6.5. Video

Kiểm tra handler video thật. Nếu hiện chưa có provider adapter/runtime thật:

- Vẫn cho phép hiển thị cấu hình default `image_to_video` nếu capability data tồn tại.
- Không tuyên bố runtime đã dùng được khi handler vẫn mock/disabled.
- Không bật mock production.
- Báo blocker rõ trong báo cáo.

Nếu đã có runtime, render scene video phải dùng selected image + motion prompt và resolve provider/model default `image_to_video`.

## 7. Logging bắt buộc — biết chính xác provider/model nào đã chạy

### 7.1. Structured application log

Tại các mốc Resolve, Start, Success, Failure phải ghi structured fields, không ghép chuỗi khó query:

- `FeatureCode`
- `CapabilityCode`
- `ProviderId`
- `ProviderCapabilityId`
- `ProviderCode`
- `ModelName`
- `RequestId`
- `LogicalRequestId`
- `JobId`
- `CustomerId`/tenant scope phù hợp
- entity liên quan: AvatarId, CharacterId, ProjectId, SceneId...
- `ProviderTaskId` khi có
- `Status`
- duration
- estimated/actual provider cost
- charged/refunded points
- error code/message đã sanitize

Không log API key, Authorization header, raw secret hoặc signed URL nhạy cảm.

### 7.2. Usage log trong database

Mọi API call thực tế phải ghi `todox_ai_provider_usage_log` qua đường thống nhất và có tối thiểu:

- `provider_id`
- `provider_capability_id`
- `provider_code`
- `capability_code`
- `feature_code`
- `model_name`
- `request_id`
- `job_id` nếu có
- status
- cost/points
- metadata liên kết entity.

Giá trị phải lấy từ provider/model **đã thực sự thực thi**. Trường hợp adapter trả model canonical khác alias cấu hình, lưu cả configured model và executed/provider-returned model trong metadata; `ModelName` chính phải theo quy ước được chốt và nhất quán.

Không ghi hai usage rows cho cùng một logical API call/retry. Cần idempotency theo logical request hiện có.

### 7.3. Job/version/event log

Khi hệ thống đã lưu image/video version hoặc render job, snapshot thêm/giữ đúng:

- provider code
- provider capability id
- model
- provider task id
- request/logical request id
- cost/points/status.

Không để usage log nói YEScale nhưng version/job lại ghi OpenRouter hoặc null.

### 7.4. Giao diện kiểm tra log

Nếu trang AI Providers đã có usage/history section, bổ sung cột/filter:

- Feature.
- Provider.
- Model.
- Capability.
- Status.
- Request/Job.
- Thời gian.
- Cost/Points.

Nếu chưa có, tạo panel đọc gần đây ở trang AI Providers hoặc liên kết sang trang log hiện có; không tạo hệ log thứ hai.

## 8. Cache và hiệu lực sau khi lưu

Kiểm tra resolver/repository có cache default hay không.

- Sau khi admin lưu, request render mới phải dùng setting mới ngay hoặc sau khi cache invalidation rõ ràng.
- Job đã enqueue phải dùng provider/model snapshot tại thời điểm enqueue hoặc quy tắc hiện có được tài liệu hóa; không đổi giữa retry.
- Không restart IIS chỉ để setting DB có hiệu lực, trừ khi kiến trúc hiện tại thực sự bắt buộc và phải được sửa nếu có thể.

## 9. Validation và an toàn

Không cho lưu nếu:

- Provider disabled.
- Capability/model disabled.
- Capability code không đúng chức năng.
- ModelName trống khi adapter yêu cầu model.
- Provider adapter/factory không hỗ trợ provider code đó.
- Video chọn model image hoặc ngược lại.

Hiển thị lỗi tiếng Việt rõ ràng. Không hiển thị secret/config JSON nhạy cảm trong quick settings.

## 10. Test bắt buộc

### Service/repository

1. Load đúng default cho từng capability.
2. Provider dropdown chỉ có provider enabled và hỗ trợ capability.
3. Model dropdown chỉ có model enabled của provider + capability đã chọn.
4. Set default bảo đảm duy nhất một `IsDefault=true` cho capability.
5. Avatar và Avatar Builder có default độc lập.
6. Lưu xong reload trả đúng provider/model từ DB.
7. Provider/model disabled không được lưu.
8. Cache được invalidate sau khi lưu.

### Runtime routing

9. Avatar resolve đúng capability/default.
10. Character resolve đúng capability/default.
11. Scene batch và scene rerender cùng theo default Scene.
12. Avatar Builder theo default riêng.
13. Video theo `image_to_video` nếu runtime thật đã có.
14. Không có silent fallback/hard-code bypass.
15. Retry giữ nguyên provider/model và không double-charge.

### Logging

16. Success log có provider/capability/model/request/job.
17. Failure log vẫn có provider/capability/model đã chọn.
18. Usage DB row có đầy đủ provider/model thực thi.
19. Usage log, render job và media version nhất quán.
20. Không ghi trùng usage khi retry/idempotent request.
21. Không log secret.

Ưu tiên fake repository/adapter/HttpMessageHandler; unit/integration test không gọi API trả phí thật.

## 11. Browser QA

Trên trang AI Providers:

1. Thấy đủ năm dòng cài đặt nhanh.
2. Đổi Provider làm Model dropdown cập nhật đúng.
3. Không thấy model của provider khác.
4. Lưu và refresh trình duyệt vẫn giữ đúng lựa chọn.
5. Disabled provider/model không thể chọn.
6. Giao diện responsive và không phá phần quản lý provider chi tiết hiện có.

Sau đó chạy mỗi chức năng một lần bằng fake/staging adapter hoặc môi trường được phép và đối chiếu:

```text
Quick setting
= resolver result
= application log
= usage log
= job/version metadata
```

Không gọi API production trả phí chỉ để test nếu chưa được cho phép.

## 12. Database

Ưu tiên tuyệt đối không thay schema vì cấu trúc hiện tại đã có `IsDefault` theo capability.

Nếu chỉ thiếu capability/model rows thì không migration; chuẩn bị SQL data idempotent riêng gồm preflight/verify/rollback để chủ hệ thống tự chạy. Không tự thực thi.

Nếu phát hiện unique constraint không cho nhiều model trên cùng provider/capability, báo rõ trước khi đề xuất schema. Không tự tạo migration.

## 13. Build và publish

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\ai-provider-quick-defaults
```

Không deploy production.

## 14. Báo cáo cuối

Báo cáo theo cấu trúc:

1. `AGENTS.md` đã đọc.
2. Bảng mapping cuối cùng: chức năng → feature code → capability → entry points.
3. Kiến trúc quick settings và xác nhận không tạo cơ chế song song.
4. Từng đường runtime đã sửa để dùng resolver.
5. Những hard-code/bypass đã phát hiện, đặc biệt scene batch Vertex.
6. Cách bảo đảm provider/model trong application log, usage log, job và version nhất quán.
7. Trạng thái video: adapter thật hay còn blocker/mock.
8. Có cần SQL data bổ sung hay không; tuyệt đối không tự chạy.
9. File đã sửa.
10. Test/build/publish và đường dẫn artifact.
11. Xác nhận không migration, không database execution, không deploy.

Không báo hoàn thành nếu chỉ có giao diện dropdown nhưng runtime vẫn bypass setting, hoặc nếu log không chứng minh được provider/model thực tế đã chạy.
