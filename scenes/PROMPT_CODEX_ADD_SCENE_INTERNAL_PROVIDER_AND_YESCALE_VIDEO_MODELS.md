# PROMPT CODEX — BỔ SUNG IMAGEAICREATIVERENDER CHO SCENE VÀ YESCALE VIDEO

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

## 1. Mục tiêu

Sửa và hoàn thiện phần **Cài đặt nhanh Provider mặc định** để:

1. Dòng **Ảnh Scene** chọn được `ImageAICreativeRender` và model nội bộ tương ứng.
2. Dòng **Video từ ảnh** chọn được provider YEScale.
3. YEScale có đủ ba model video đã thống nhất:
   - `grok-video` — mặc định/chi phí thấp.
   - `grok-video-1.5` — chất lượng/fallback.
   - `omni-flash` — multi-mode.
4. Hiển thị đúng điểm TodoX dự kiến cho từng model.
5. Runtime video thật dùng đúng provider/model đã chọn, không chỉ thêm dữ liệu dropdown.
6. Log, usage, billing và version metadata ghi đúng provider/model/cost thực tế.

## 2. Quy tắc bắt buộc

1. Đọc toàn bộ `AGENTS.md` đúng scope trước khi làm.
2. Kiểm tra `git status`, commit mới nhất và bảo toàn thay đổi ngoài phạm vi.
3. Không hard-code secret/API key.
4. Không tạo hoặc tự chạy migration.
5. Không tự chạy SQL trên database. Chỉ tạo SQL data idempotent riêng nếu cần để chủ hệ thống chạy.
6. Không deploy production; phải test, build Release và publish artifact riêng.
7. Không gọi API video trả phí thật trong unit/integration test nếu chưa được cho phép.
8. Không báo hoàn thành nếu dropdown chọn được nhưng runtime video vẫn mock/disabled hoặc bỏ qua setting.

## 3. Đọc tài liệu YEScale bằng MCP trước khi code

YEScale public docs không được search engine index đầy đủ. Phải dùng YEScale MCP đã cấu hình:

1. `yescale_list_models`
2. `yescale_get_model_doc`

Truy vấn riêng ba model:

- `grok-video`
- `grok-video-1.5`
- `omni-flash`

Trong báo cáo phải ghi:

- Thời điểm truy vấn UTC.
- `cached=true/false` nếu MCP trả về.
- Endpoint submit/poll.
- Modalities input/output.
- Tên tham số chính xác.
- Duration/aspect ratio/resolution/mode được hỗ trợ.
- Giá hiện tại và đơn vị giá.
- Timeout/quota/RPM nếu tài liệu có.
- Output schema và terminal statuses.

Không dựa hoàn toàn vào số liệu cũ nếu MCP trả giá mới. Không tự suy đoán tham số không có trong tài liệu.

## 4. Thông tin YEScale đã xác minh trước đây — chỉ làm baseline

Baseline cũ cần đối chiếu lại với MCP:

| Model | Vai trò | Endpoint | Input → Output | Giá cũ |
|---|---|---|---|---|
| `grok-video` | Video mặc định/cheap | `/task/submit` + `/task/{task_id}` | text,image → video | 0,14–0,30 USD/request |
| `grok-video-1.5` | Backup/quality | `/task/submit` + `/task/{task_id}` | text,image → video | 0,20–0,40 USD/request |
| `omni-flash` | Multi-mode | `/task/submit` + `/task/{task_id}` | text,image,video → video | 0,37–0,47 USD/request |

Async task baseline:

```text
POST https://api.yescale.io/task/submit
GET  https://api.yescale.io/task/{task_id}
terminal status: SUCCESS / FAILURE
```

Phải lấy schema payload/result chính xác từ MCP trước khi triển khai adapter.

## 5. Công thức quy đổi điểm TodoX

Quy định hiện tại:

```text
YEScale: 8.000 VND / 1 USD
TodoX:   1 điểm = 10.000 VND
```

Công thức:

```text
Điểm TodoX = Chi phí YEScale USD × 8.000 / 10.000
             = Chi phí YEScale USD × 0,8
```

Theo giá baseline cũ:

| Model | USD/request | Điểm TodoX dự kiến |
|---|---:|---:|
| `grok-video` | 0,14–0,30 | 0,112–0,240 điểm |
| `grok-video-1.5` | 0,20–0,40 | 0,160–0,320 điểm |
| `omni-flash` | 0,37–0,47 | 0,296–0,376 điểm |

Nếu MCP trả giá mới, cập nhật cả SQL/config/test/docs theo giá mới và trình bày phép tính.

### 5.1. Không dùng sai một mức giá cố định

Vì giá có khoảng và có thể phụ thuộc duration/resolution/mode:

- `unit_cost_points` không được giả vờ là chi phí cuối nếu request có biến số.
- Lưu tariff matrix chính xác trong `config_json` hiện có hoặc cấu trúc pricing hiện có.
- UI quick settings hiển thị khoảng điểm, ví dụ `0,112–0,240 điểm/video`.
- Trước render: tính estimated USD/points theo tham số request.
- Sau provider hoàn tất: lấy actual cost/usage nếu YEScale trả về và reconcile.
- Nếu provider không trả actual cost: tính theo tariff snapshot + request parameters và đánh dấu `cost_source=tariff_estimate`.
- Log cả `estimated_usd`, `actual_usd`, `estimated_points`, `charged_points`, `cost_source`.
- Không double-charge khi poll/retry cùng logical request.

Nếu cấu trúc hiện tại chỉ có một `UnitCostPoints`, dùng giá tối thiểu/base cho hiển thị phụ và tariff JSON cho tính toán thực; ghi rõ quy ước. Không âm thầm dùng minimum làm final charge.

## 6. Sửa Ảnh Scene để chọn được ImageAICreativeRender

Hiện quick settings lọc provider theo `scene_image_generation`, nhưng database/provider capability có vẻ chưa có row enabled của `ImageAICreativeRender` cho capability này.

Phải kiểm tra:

- Provider code thật: `image_ai_creative_render`.
- Adapter/factory mapping hiện có về `todox_image`.
- Capability rows hiện có của provider.
- Unique constraints/index của bảng capability.
- Runtime batch và rerender scene.

Nếu thiếu row data, tạo SQL idempotent riêng để upsert:

```text
provider:        ImageAICreativeRender / image_ai_creative_render
capability:      scene_image_generation
model_name:      model nội bộ chính xác đang được runtime dùng, không đoán
display_name:    ImageAICreativeRender Scene Image Generation
enabled:         true
unit_type:       image
unit_cost_points: theo cấu hình hiện hành đã được xác minh
allow_user_select: theo policy admin/system hiện tại
is_default:      không tự đổi nếu admin chưa bấm lưu quick settings
```

Ảnh chụp hiện tại cho thấy model nội bộ ở Avatar/Character là `internal_default` và 3 điểm/image. Phải kiểm tra Scene có cùng engine/tariff hay không trước khi dùng lại. Nếu đúng, seed `internal_default`, `3 điểm/image`; nếu không, dùng dữ liệu thực tế.

SQL phải có:

- Preflight schema/constraint.
- Transaction.
- Idempotent upsert an toàn.
- Không phá default hiện tại.
- Verify query.
- Rollback riêng.
- Không chạy SQL.

Sau khi data tồn tại, dropdown Ảnh Scene phải liệt kê cả:

- ImageAICreativeRender.
- YEScale Task Image.
- Các provider enabled khác thực sự hỗ trợ `scene_image_generation`.

### 6.1. Runtime Scene

Sửa mọi đường Scene cùng dùng resolver `scene_image_generation`:

- Auto render sau tách scene.
- Batch render ảnh còn thiếu.
- Render lại một scene.
- Retry quota.

Không để `RenderSceneImageWithVertexAsync` bypass default trong khi UI đã chọn YEScale hoặc provider khác. Nếu chọn ImageAICreativeRender thì resolver chọn capability row đó rồi router/factory gọi engine nội bộ; không gọi bằng hard-code từ handler.

Retry cùng logical request giữ nguyên provider/model snapshot và không charge lại.

## 7. Bổ sung YEScale Video từ ảnh

### 7.1. Provider architecture

Kiểm tra provider YEScale hiện tại:

- Nếu provider hiện có `yescale_task_image` chỉ là tên cũ nhưng client/task API dùng chung và kiến trúc cho phép nhiều modality, cân nhắc đổi display name thành `YEScale Task` mà vẫn giữ provider code tương thích.
- Nếu adapter/factory bị ràng buộc image-only, tạo provider/adapter video riêng như `yescale_task_video` thay vì nhét video vào image factory.
- Không đổi provider code đã chạy production một cách làm hỏng log/foreign key/config cũ.

Quyết định phải dựa trên code hiện tại và được giải thích trong báo cáo.

### 7.2. Capability/model rows

Video scene từ selected image dùng capability:

```text
image_to_video
```

Tạo SQL data idempotent để provider YEScale có ba model enabled cho `image_to_video`:

1. `grok-video`
2. `grok-video-1.5`
3. `omni-flash`

Mỗi model phải là một lựa chọn capability/model hợp lệ theo schema hiện có, gồm:

- provider id/code.
- capability code `image_to_video`.
- model name.
- display name/vai trò.
- endpoint path.
- enabled.
- unit type phù hợp.
- cost points base/range.
- config JSON chứa parameter schema, pricing/tariff matrix, supported ratio/duration/resolution/mode.
- `is_default=false` trừ model đang được admin chọn/lưu bằng quick settings.

Nếu unique constraint hiện tại không cho nhiều model cùng provider + capability:

- Không tạo migration.
- Báo blocker chính xác.
- Chuẩn bị SQL schema proposal riêng có preflight/rollback để chủ hệ thống duyệt.
- Không giả lập ba model bằng hard-code UI.

### 7.3. Quick settings

Dòng `Video từ ảnh` phải:

- Provider dropdown thấy YEScale khi có capability enabled.
- Model dropdown thấy đúng ba model YEScale.
- Hiển thị điểm/range đã quy đổi.
- `grok-video` có badge `Mặc định/Chi phí thấp`.
- `grok-video-1.5` có badge `Chất lượng/Fallback`.
- `omni-flash` có badge `Multi-mode`.
- Lưu `ProviderCapabilityId`, không lưu chuỗi model rời rạc.
- Refresh trang vẫn load đúng default từ DB.

## 8. Xây dựng runtime YEScale video thật

Không chỉ seed dropdown. Hoàn thiện boundary/adapter xử lý image-to-video:

```text
Selected scene image version
+ current motion_prompt
+ aspect_ratio
+ resolution
+ duration
→ ResolveProviderForCapabilityAsync("image_to_video")
→ YEScale submit
→ lưu provider task id
→ background poll
→ validate SUCCESS có video URL
→ tải video tạm về media storage TodoX
→ tạo scene video version mới
→ chọn version mới nhất mặc định
→ usage/billing/log reconciliation
```

### 8.1. Request

- Dùng selected image version đang được chọn, không lấy URL ảnh cũ.
- Dùng TodoX MediaId/ObjectKey/URL bền vững; không truyền URL provider đã hết hạn.
- Payload theo đúng model doc MCP.
- Validate aspect ratio/duration/resolution/mode theo model.
- Không hard-code `grok-video`; dùng model từ resolved capability.
- Logical request idempotent.

### 8.2. Poll

- Poll là background job; UI không bị khóa.
- Status queued/running/success/failure/timeout/cancelled.
- Không coi HTTP 200 hoặc `progress=100%` là thành công nếu status failure hoặc không có output video.
- Failure phải giữ nguyên error code/message/provider response sanitized.
- Có timeout/cancellation/backoff.
- Poll không tạo thêm usage charge.

### 8.3. Output

- YEScale output URL có thể tạm thời: phải download và lưu vào media storage TodoX ngay.
- Lưu object key theo cấu trúc versioning scene video hiện có.
- Scene video version snapshot:
  - source image version id.
  - motion/image prompt.
  - ratio/resolution/duration.
  - provider/provider capability/model/task id.
  - request/logical request id.
  - estimated/actual USD và points.
  - status/error/timestamps.

Không dùng mock video trong production.

## 9. Logging và billing

Mỗi video call phải ghi nhất quán:

- application structured log.
- `todox_ai_provider_usage_log`.
- render job/job event.
- scene video version metadata.
- wallet/point transaction.

Fields tối thiểu:

- feature code `render_job_scene_video`.
- capability `image_to_video`.
- provider id/code.
- provider capability id.
- configured model và executed model.
- request/logical request/job/project/scene ids.
- provider task id.
- status/duration/error.
- estimated/actual USD.
- estimated/charged/refunded points.
- cost source.

Không log API key, Authorization header hoặc signed URL nhạy cảm.

Admin vẫn đi qua luồng wallet/usage hiện tại theo policy hệ thống; không bypass log chỉ vì là admin.

## 10. Test bắt buộc

### Data/quick settings

1. ImageAICreativeRender xuất hiện ở Scene khi capability enabled.
2. Chọn/lưu ImageAICreativeRender Scene rồi refresh vẫn đúng.
3. YEScale xuất hiện ở Video từ ảnh.
4. Ba model video xuất hiện đúng provider/capability.
5. Model provider khác không bị trộn.
6. Điểm/range hiển thị đúng công thức.
7. Disabled provider/model không chọn được.

### Routing

8. Scene batch và rerender dùng default đã chọn.
9. Chọn ImageAICreativeRender thì runtime trả provider/model nội bộ đúng.
10. Chọn YEScale Scene thì không gọi Vertex trực tiếp.
11. Video resolve đúng selected YEScale model.
12. Retry giữ nguyên provider/model.

### YEScale video adapter

13. Submit payload từng model đúng doc.
14. Poll SUCCESS có output được lưu media.
15. FAILURE không bị báo success.
16. SUCCESS nhưng thiếu output bị coi là failure rõ ràng.
17. Timeout/cancel được xử lý.
18. URL tạm được download và version dùng URL TodoX.
19. Video version lineage tới selected image version.

### Billing/log

20. Điểm estimate đúng tariff/request parameters.
21. Actual cost được reconcile đúng.
22. Không double-charge poll/retry.
23. Failure/refund theo policy hiện tại.
24. Usage log/job/version/wallet cùng provider/model/cost.
25. Không log secret.

Dùng fake repository, fake handler/HttpMessageHandler và fixtures payload YEScale; không gọi API trả phí thật.

## 11. Browser QA

1. Mở AI Providers → Cài đặt nhanh.
2. Dòng Ảnh Scene chọn được ImageAICreativeRender và YEScale.
3. Dòng Video từ ảnh chọn được YEScale.
4. Model dropdown hiển thị ba model video cùng điểm/range.
5. Lưu, refresh, mở lại vẫn đúng.
6. Dùng fake/staging adapter chạy một scene video và đối chiếu UI status/log/version.

Không test production trả phí nếu chưa được cho phép.

## 12. File SQL bàn giao

Nếu cần data update, tạo bộ file riêng, ví dụ:

```text
database/manual/ai-provider-video/
01_preflight_ai_provider_video.sql
02_seed_image_ai_creative_render_scene.sql
03_seed_yescale_image_to_video_models.sql
04_verify_ai_provider_video.sql
rollback_ai_provider_video.sql
README.md
```

Tất cả idempotent, có transaction, verify và rollback. Không tự chạy.

## 13. Build và publish

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\yescale-video-provider
```

Không deploy production.

## 14. Báo cáo cuối

1. `AGENTS.md` đã đọc.
2. Kết quả MCP cho ba model, timestamp và giá.
3. Bảng công thức USD → điểm.
4. Vì sao ImageAICreativeRender trước đây không xuất hiện ở Scene và cách sửa.
5. Kiến trúc provider YEScale video đã chọn.
6. Capability/model rows đã chuẩn bị.
7. Runtime video thật đã hoàn thiện đến đâu; không che giấu blocker.
8. Cách submit/poll/store/version.
9. Billing/log/idempotency.
10. SQL tạo ra nhưng xác nhận chưa chạy.
11. File code đã sửa.
12. Test/build/publish và artifact.
13. Xác nhận không migration, không database execution, không deploy.

Không báo hoàn thành nếu ImageAICreativeRender chưa chọn được cho Scene, YEScale chưa chọn được cho Video, ba model/điểm chưa hiển thị đúng, hoặc runtime video vẫn không dùng setting đã lưu.
