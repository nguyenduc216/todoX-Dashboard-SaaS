# Prompt Codex — Hoàn thiện triệt để Scene Media Versioning

Bạn đang làm việc trong repository:

`nguyenduc216/todoX-Dashboard-SaaS`

Commit cần tiếp tục xử lý:

`886761d52ed90bed187d1358f014a6a93e794479`

## Mục tiêu

Hoàn thiện end-to-end versioning cho:

1. Ảnh tĩnh từng scene.
2. Video từng scene được tạo từ một image version cụ thể.
3. Video hoàn chỉnh được ghép từ các scene video version cụ thể.
4. Lịch sử ảnh, prompt ảnh, prompt video, video scene và final video.
5. Chọn lại version cũ làm mặc định mà không gọi provider hoặc trừ điểm.
6. Storage vật lý bất biến theo version.
7. Billing, provider task ID, authorization, audit, idempotency và reconciliation đầy đủ.

Không chỉ tạo thêm lớp hoặc DTO. Phải nối hoàn chỉnh từ UI → service → database → job worker → storage → billing → history → selection → video merge.

## Quy định bắt buộc

1. Đọc toàn bộ `AGENTS.md` ở root và các thư mục liên quan trước khi sửa.
2. Đọc lại `PROMPT_FOR_CODEX.md`, SQL trong `database/scene-media-versioning` và toàn bộ code của commit hiện tại.
3. Kiểm tra `git status`; không ghi đè hoặc revert thay đổi có sẵn của người dùng.
4. Không tạo EF migration.
5. Chỉ cập nhật SQL patch độc lập, additive, idempotent.
6. Không tự chạy SQL trên database.
7. Không deploy production.
8. Không hard-code tenant/customer/user/model/provider/storage root/URL/credential.
9. Giữ UTF-8 và kiểm tra toàn bộ chuỗi tiếng Việt đã sửa.
10. Trước khi code, báo hiện trạng và kế hoạch theo từng blocker dưới đây.

## Blocker 1 — Hoàn thiện UI lịch sử và selection

`RenderVideoJobs.razor` hiện chưa có giao diện versioning. Phải bổ sung:

### Trên từng scene

- Ảnh selected hiện tại.
- Video scene selected hiện tại.
- Nút `Lịch sử ảnh`.
- Nút `Lịch sử video`.
- Badge:
  - Đang chọn.
  - Mới nhất.
  - Thành công.
  - Đang xử lý.
  - Thất bại.
  - Video đã cũ so với ảnh đang chọn.
- Chỉ scene đang render có flash/skeleton; không processing overlay toàn trang.

### Lịch sử ảnh

Lazy-load theo scene, phân trang 20 dòng/lần. Mỗi version hiển thị:

- Thumbnail/xem ảnh lớn.
- Version number.
- Image prompt draft/original.
- Compiled/final prompt thực sự gửi provider.
- Video prompt snapshot.
- Negative prompt.
- Character/reference snapshot.
- Provider/model/task ID.
- Điểm và USD dự kiến/thực tế.
- Status/error/timestamps.
- Nút sao chép prompt.
- Nút chọn làm mặc định.
- Nút dùng prompt này để render lại; thao tác này phải tạo version mới.

### Lịch sử video scene

Hiển thị preview/poster, source image version, prompt, model/provider/task, duration, billing, status/error và nút chọn version cũ.

### Lịch sử final video

Hiển thị từng final version, danh sách exact scene video versions đã ghép, trạng thái outdated, preview, thời gian và nút chọn lại.

Không tải toàn bộ lịch sử của mọi scene lúc mở trang. Không reload toàn project khi chỉ đổi selection nếu không cần.

## Blocker 2 — Chọn image version cũ phải khôi phục prompt snapshot

Mở rộng DTO/query để trả đầy đủ prompt và metadata.

Khi chọn image version cũ, trong một transaction:

1. Xác minh version completed, có file/media hợp lệ, thuộc đúng scene/project/tenant/customer.
2. Bỏ selected version cũ.
3. Chọn version yêu cầu.
4. Cập nhật scene:
   - `selected_image_version_id`.
   - `static_image_url/path`.
   - `image_prompt` theo snapshot.
   - `video_prompt` theo snapshot.
   - negative/config/reference draft nếu schema hiện tại hỗ trợ.
5. Không gọi provider.
6. Không tạo billing.
7. Ghi audit/project event chứa old version ID, new version ID, selected_by và timestamp.
8. Tính/hiển thị selected scene video có outdated hay không.

Chọn video scene và final video cũ cũng phải có audit/event tương tự.

## Blocker 3 — Lưu exact compiled prompt

Hiện version lưu `scene.ImagePrompt` nhưng provider nhận `SceneImagePromptBuilder.Build(...)`.

Sửa dữ liệu version để lưu riêng:

- `image_prompt_draft_snapshot` hoặc trường tương đương.
- `compiled_image_prompt_snapshot`: prompt chính xác gửi provider.
- `video_prompt_snapshot`.
- `negative_prompt_snapshot`.
- Scene/script/character/reference/config snapshot.

Không gọi prompt compiler hai lần có thể cho kết quả khác nhau. Compile một lần trước khi tạo/enqueue version, snapshot kết quả đó và worker dùng đúng snapshot đã lưu để gọi provider.

Render video scene phải dùng `video_prompt_snapshot` và exact selected image version được snapshot lúc enqueue.

## Blocker 4 — Lưu file thật vào storage folder theo version

Storage key mong muốn:

```text
render-projects/{tenant_id}/{project_id}/
  scenes/{scene_id}/
    images/{image_version_id}/output/scene-image.{ext}
    images/{image_version_id}/thumbnails/thumb-320.webp
    videos/{video_version_id}/output/scene-video.mp4
    videos/{video_version_id}/previews/poster.webp
  final-videos/{final_video_version_id}/output/final-video.mp4
  final-videos/{final_video_version_id}/manifests/composition.json
```

Yêu cầu:

1. Không chỉ tạo chuỗi storage key rồi ghi đè bằng object key khác.
2. File thực tế phải được save/copy qua media/storage abstraction vào đúng version key.
3. Không hard-code local disk; hỗ trợ local/GCS/S3-compatible qua abstraction/config.
4. Ghi file tạm, validate, sau đó atomic move/finalize.
5. Chỉ complete version khi file và media record thành công.
6. Lưu `result_media_id`, canonical `storage_key`, public/signed URL.
7. Tạo thumbnail/poster khi phù hợp.
8. Không tạo `latest.*`.
9. Không xóa/ghi đè file version cũ.
10. Nếu provider thành công nhưng persistence lỗi, retry persistence từ kết quả đã có; không gọi provider lại.

Nếu media service hiện chưa hỗ trợ explicit storage key, mở rộng interface bằng thay đổi nhỏ, tương thích ngược và có test.

## Blocker 5 — Provider task ID và billing

Không truyền `ProviderTaskId: null` khi provider có task ID.

Mở rộng outcome/router để trả và lưu:

- Provider task ID.
- Requested/actual model.
- Provider capability ID.
- Billing logical request/record ID.
- Estimated USD.
- Actual USD nếu provider xác nhận.
- Cost source.
- Charged/refunded points.
- Provider usage snapshot.

Mọi render mới phải theo luồng:

```text
resolve payer → reserve → submit provider → persist task ID
→ poll/result → persist media/version → complete/refund/reconcile
```

Version phải liên kết với đúng billing logical request. Retry không được trừ hai lần. Chọn/xem/copy version cũ không tạo billing.

Nếu YEScale không trả actual cost, lưu `actual_usd = null`, đánh dấu cost source/incomplete rõ ràng; không giả actual bằng estimated.

## Blocker 6 — Tạo queued version trước khi enqueue

Hiện image version được tạo trong worker. Phải đổi thành:

```text
transaction persist project + scenes + queued versions + render jobs
→ commit
→ worker claim/process
```

Sau khi phân tách scene:

1. Lưu project và toàn bộ scene.
2. Compile exact prompt.
3. Tạo queued image version cho mỗi scene cần render.
4. Tạo/enqueue job tham chiếu exact version ID và logical request ID.
5. Commit persistence trước khi worker xử lý.
6. UI có thể thấy queued versions ngay sau split.

Job payload phải chứa project ID, scene ID, version ID và trusted payer context phía server.

Worker:

- Chỉ claim/process version đã tồn tại.
- Không tạo version mới trong retry.
- Tương thích với job cũ khi feature flag tắt.
- Retry enqueue/bấm hai lần không tạo trùng nhờ logical request/idempotency.

Nếu kiến trúc queue hiện tại không hỗ trợ transaction chung, dùng outbox hoặc trạng thái pending enqueue an toàn; không để job tồn tại mà scene/version chưa persist.

## Blocker 7 — Final merge phải dùng exact version snapshot

Đây là blocker nghiêm trọng.

`CreateQueuedFinalVideoVersionAsync` đã tạo `final_video_version_items`, nhưng merge hiện vẫn dùng `project.Scenes[].SceneVideoPath`.

Phải sửa cả `VideoRenderMergeHandler` và mock/handler liên quan:

1. Sau khi tạo/reuse final version, đọc `final_video_version_items` theo `item_order`.
2. Join exact `scene_video_versions`.
3. Xác minh từng version completed, đúng project/scene/tenant và file tồn tại.
4. Tạo concat/input chỉ từ `scene_video_versions.source_file_path/storage_key` đã snapshot.
5. Không đọc selected/current scene path lại trong lúc merge.
6. Nếu người dùng đổi selection khi merge đang chạy, input job hiện tại không thay đổi.
7. `composition.json` phải ghi exact item IDs/order/config.
8. Nếu item thiếu/invalid, fail final version rõ ràng; không âm thầm dùng path hiện tại.

Áp dụng nguyên tắc tương tự cho scene video: worker dùng exact `source_image_version_id`, không đọc selected image lại sau enqueue.

## Blocker 8 — Authorization và ownership

Mọi method create/list/select/complete/fail phải xác minh:

- Tenant.
- Project.
- Scene thuộc project.
- Customer ownership nếu customer session.
- User/permission phù hợp nếu admin.
- Version thuộc đúng scene/project/customer.

Không chỉ lọc theo tenant và scene ID.

Không tin customer/user/version ID do UI gửi lên nếu chưa đối chiếu session/server context. Background worker dùng trusted server-side context.

Thêm test cross-customer và cross-tenant bị từ chối.

## Blocker 9 — Audit và trạng thái outdated

Ghi append-only event/audit khi:

- Tạo version.
- Submit provider.
- Complete/fail/reconcile.
- Tự chọn latest successful.
- Người dùng chọn version cũ.
- Tạo/complete final composition.

Outdated:

```text
scene video outdated khi
selected_video.source_image_version_id != selected_image_version_id

final video outdated khi
bất kỳ final item.scene_video_version_id != scene.selected_video_version_id
```

Outdated không đổi status completed và không xóa file; chỉ là cảnh báo nguồn hiện tại đã thay đổi.

## Blocker 10 — SQL

Rà soát và cập nhật bộ SQL trong `database/scene-media-versioning`:

- `00_preflight_scene_media_versioning.sql`
- `01_add_scene_media_versioning.sql`
- `02_backfill_existing_scene_media.sql`
- `03_seed_scene_media_versioning_settings.sql`
- `verify_scene_media_versioning.sql`
- `rollback_scene_media_versioning.sql`

Yêu cầu:

1. Đối chiếu tên bảng/type/FK với code và database foundation hiện tại.
2. Bổ sung cột exact prompt/provider/billing/media nếu code cần.
3. Additive, transaction, fail-fast, idempotent.
4. Không DROP/TRUNCATE/xóa lịch sử.
5. Backfill ảnh/video/final hiện tại thành version 1, không billing/provider call, không bịa prompt.
6. Backfill idempotent và không tạo completed version nếu file/URL không tồn tại trong dữ liệu.
7. Verify pointer, unique selected, lineage, settings, prompts, storage keys và orphan records.
8. Feature flags mặc định false.
9. Rollback chỉ tắt flags, không xóa version/media.
10. Không chạy SQL; giao lại cho người dùng tự chạy.

Loại bỏ các bản ZIP/artifact trùng lặp khỏi source repository nếu chúng không phải source chính, nhưng không xóa file người dùng khi chưa xác định. Chỉ giữ một bộ canonical dưới `database/scene-media-versioning` và tài liệu cần thiết.

## Blocker 11 — Test đầy đủ

Hiện test mới chỉ kiểm tra chuỗi storage key. Phải bổ sung unit/integration tests cho:

1. Split persist project/scene/version/job trước worker.
2. Retry enqueue không trùng version/job.
3. Render image lần 1/2 tạo version 1/2.
4. Version cũ còn và file cũ truy cập được.
5. Success mới auto-selected; failed không thay selected.
6. Exact compiled prompt snapshot được dùng bởi provider.
7. Chọn ảnh cũ khôi phục prompt snapshot, không provider/billing.
8. Concurrent render không trùng version number.
9. Worker retry không tạo version/trừ điểm lần hai.
10. Provider task ID và billing fields được persist.
11. File thực tế được lưu ở hai version storage key khác nhau.
12. Scene video dùng exact source image version.
13. Chọn ảnh khác làm video outdated.
14. Final merge dùng exact final items, không dùng scene current path.
15. Đổi selection khi merge đang chạy không đổi inputs.
16. Merge lại tạo final version mới; chọn final cũ không merge/billing.
17. Cross-scene/project/customer/tenant selection bị chặn.
18. Backfill idempotent.
19. History pagination/UI state.
20. Audit events.
21. UTF-8 tiếng Việt.

Nếu có PostgreSQL test environment, bắt buộc test transaction/concurrency. Nếu chưa có, tạo test project/fixture phù hợp hoặc báo blocker; không tuyên bố hoàn thành chỉ bằng mock tests.

## Build, test và publish

Sau khi sửa:

1. Kiểm tra `git diff` và file ngoài phạm vi.
2. Kiểm tra UTF-8.
3. Chạy `dotnet restore`.
4. Chạy `dotnet build -c Release`.
5. Chạy toàn bộ tests.
6. Chạy `dotnet publish -c Release` theo `AGENTS.md`.
7. Không deploy.
8. Không chạy SQL.
9. Báo chính xác command, exit code, tests pass/fail/skip, warning và publish folder.

Không được nói hoàn thành nếu chưa có output command thực tế.

## Smoke-test checklist phải chuẩn bị

1. Tách project 3 scene.
2. Xác nhận project/scene/queued versions/jobs đã persist trước worker.
3. Render ảnh và kiểm tra exact prompts/task/billing/storage.
4. Render lại scene, kiểm tra version/file cũ còn.
5. Chọn image version cũ, kiểm tra prompt được khôi phục và không billing.
6. Render scene video từ exact selected image.
7. Đổi image selection, kiểm tra video outdated.
8. Chọn/render scene video versions.
9. Merge final và kiểm tra exact item lineage/composition manifest.
10. Đổi scene video trong khi merge để xác minh input snapshot không đổi.
11. Chọn final version cũ, không merge/billing.
12. Restart app, kiểm tra lịch sử/selection còn nguyên.

## Tiêu chí hoàn thành

Chỉ kết luận `READY FOR SCENE MEDIA VERSIONING SQL REVIEW` khi:

- UI history/selection hoàn chỉnh.
- Exact prompt/task/billing/media được lưu.
- File thật nằm đúng immutable version storage key.
- Queued versions persist trước worker.
- Scene video và final merge dùng exact lineage snapshots.
- Authorization/audit/outdated đầy đủ.
- SQL canonical đã cập nhật nhưng chưa chạy.
- Unit/integration tests đạt.
- Build và publish đạt.
- Không lỗi tiếng Việt.

Nếu còn thiếu bất kỳ mục nào, kết luận `NOT READY` và liệt kê blocker. Báo cáo cuối phải nhắc rõ: `SQL CHƯA ĐƯỢC CHẠY VÀ PRODUCTION CHƯA ĐƯỢC DEPLOY`.
