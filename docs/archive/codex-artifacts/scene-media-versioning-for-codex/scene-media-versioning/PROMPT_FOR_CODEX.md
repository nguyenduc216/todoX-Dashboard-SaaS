# Prompt gửi Codex — Versioning ảnh, video scene và video hoàn chỉnh

Bạn đang làm việc trong repository `nguyenduc216/todoX-Dashboard-SaaS`.

## Bắt buộc trước khi làm

1. Đọc toàn bộ `AGENTS.md` ở root và các thư mục liên quan; tuân thủ tuyệt đối.
2. Kiểm tra `git status`; không ghi đè/revert thay đổi của người dùng.
3. Truy vết toàn bộ luồng `/render-job`: tách scene, lưu project/scene, enqueue ảnh, render/rerender ảnh, image-to-video, merge final video, media storage và billing.
4. Đọc toàn bộ SQL trong `database/scene-media-versioning` (hoặc gói SQL đính kèm), đối chiếu với schema/code thật rồi sửa SQL nếu cần.
5. Không tạo EF migration. Chỉ tạo/cập nhật SQL patch độc lập, idempotent.
6. Không tự chạy SQL trên bất kỳ database nào. Không deploy production. Giao SQL lại để người dùng tự chạy.
7. Không hard-code credential, đường dẫn máy, tenant/customer/user/model/provider.
8. Giữ UTF-8, không làm lỗi tiếng Việt.

## Mục tiêu

Sau khi phân tách scene, tự động persist project/job và scene trước khi enqueue. Mỗi lần render ảnh tạo immutable image version; mỗi lần render image-to-video tạo immutable scene video version tham chiếu chính xác image version nguồn; mỗi lần ghép tạo final video version chứa snapshot có thứ tự của các scene video version. Không ghi đè hoặc xóa lịch sử.

Mô hình:

```text
Project
├─ Scene
│  ├─ Image Versions
│  └─ Scene Video Versions (mỗi bản tham chiếu source_image_version_id)
└─ Final Video Versions
   └─ Final Items (mỗi item tham chiếu scene_video_version_id)
```

## Quy tắc version/selection

- Mỗi render do người dùng yêu cầu tạo version mới; retry kỹ thuật cùng logical request không tạo version mới.
- Version number tuần tự theo scene/project, chống race bằng transaction/locking và unique constraints.
- Prompt/config/reference phải snapshot bất biến tại lúc submit.
- Version thành công mới nhất tự selected; failed không thay selected cũ.
- Chọn bản cũ chỉ đổi database pointer, không gọi provider, không copy file, không trừ điểm.
- Phân biệt latest, latest successful và selected.
- Chọn image version khác không tự thay scene video; đánh dấu video hiện tại `outdated` nếu `selected_video.source_image_version_id != selected_image_version_id`.
- Final video outdated nếu các item không còn trùng selected scene video hiện tại.
- Render video scene luôn snapshot selected image version lúc enqueue.
- Merge final video luôn snapshot danh sách selected scene video version vào item table lúc enqueue và worker chỉ dùng snapshot đó.

## Dữ liệu bắt buộc

### Image version

Lưu project/scene/tenant/customer/user, version/logical request/job, provider/model/task, image/video/negative prompt snapshots, scene/reference/config snapshots, media/storage path, dimensions/MIME, billing USD/points/status/error/timestamps và selection.

### Scene video version

Lưu toàn bộ lineage trên và bắt buộc `source_image_version_id` cho render image-to-video mới; lưu video prompt/image prompt snapshots, media/poster/storage, duration/fps/resolution, billing và selection. Legacy backfill được phép source image null nếu dữ liệu cũ không có ảnh.

### Final video version

Lưu composition/transition/audio/subtitle snapshots, output/poster/storage, duration/fps/resolution/status/selection. Bảng item phải lưu final version, scene, exact scene video version, order, trim, transition, volume/config.

## Folder/storage key

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

- Dùng IDs, không dùng tên scene.
- Không tạo `latest.*`; database selection là nguồn chính.
- Storage key không chứa domain; mọi ghi/đọc qua media/storage abstraction.
- Ghi temp, validate rồi atomic move; chỉ completed sau khi file và media record thành công.
- Không xóa media/version cũ khi rerender, selection hoặc rollback.
- Manifest không chứa secret/PII; prompt chính nằm database.

## Tự động lưu sau tách scene

1. Resolve tenant/customer/user hợp lệ.
2. Transaction lưu project và toàn bộ scene.
3. Tạo queued image version và job idempotent cho từng scene cần render.
4. Chỉ enqueue sau commit persistence.
5. Job chứa scene/version/logical request/trusted payer context phía server.
6. Worker không tự tạo scene/version ngoài ý muốn.

## UI

- Mỗi scene hiển thị selected image và selected scene video.
- Có lịch sử ảnh và lịch sử video lazy-load, phân trang.
- Hiển thị thumbnail/preview, version, selected/latest/status, prompts, model/provider/task, chi phí/điểm/lỗi/timestamps.
- Cho chọn lại bản cũ, copy prompt, hoặc dùng prompt cũ để render version mới.
- Chỉ card scene đang chạy có flash/skeleton; không overlay processing toàn trang.
- Final video có lịch sử composition; xem được exact scene video versions đã ghép và chọn lại final cũ.
- Hiển thị cảnh báo outdated rõ ràng.

## Billing/job safety

- Giữ luồng resolve payer → reserve → submit → lưu task ID → complete/refund/reconcile.
- Image/video version liên kết logical billing request.
- Retry idempotent không trừ hai lần.
- Chọn/xem/copy bản cũ không billing.
- Lưu provider task ID ngay sau submit, trước polling.
- Nếu provider thành công nhưng persistence lỗi, retry persistence, không gọi provider lại.

## SQL

Đọc và rà soát các file:

```text
00_preflight_scene_media_versioning.sql
01_add_scene_media_versioning.sql
02_backfill_existing_scene_media.sql
03_seed_scene_media_versioning_settings.sql
verify_scene_media_versioning.sql
rollback_scene_media_versioning.sql
```

Yêu cầu:

- Đối chiếu type/FK/tên bảng với database code hiện tại.
- SQL additive, transaction, fail-fast, idempotent, không DROP/TRUNCATE/xóa lịch sử.
- Backfill ảnh/video/final hiện có thành version 1, không billing/provider call và không bịa prompt.
- Settings/feature flags mặc định false.
- Rollback chỉ tắt flag, không xóa version/media.
- Không chạy SQL; báo thứ tự để người dùng chạy sau build/test.

## Service/API đề xuất

Tạo service tách biệt cho image versions, scene video versions và final composition versions. Các thao tác create queued, submitted, complete, fail, history, select và get selected phải kiểm tra tenant/customer ownership, dùng cancellation token, transaction và idempotency.

## Test bắt buộc

1. Split lưu project/scene trước enqueue và retry không trùng.
2. Render ảnh lần 1/2 tạo hai version; bản cũ còn; success mới selected; failure không đổi selected.
3. Prompt snapshot bất biến; concurrent render không trùng version number.
4. Chọn image cũ không provider/billing và đánh dấu scene video outdated đúng.
5. Scene video tham chiếu exact image version; retry worker không tạo version mới/trừ hai lần.
6. Chọn scene video cũ không billing và final outdated đúng.
7. Final merge snapshot exact ordered scene video versions; thay selection khi merge đang chạy không đổi input job.
8. Merge lại tạo final version mới; chọn final cũ không merge/billing.
9. Cross-scene/cross-tenant selection bị chặn.
10. Backfill idempotent; không tạo completed giả khi thiếu file.
11. Storage keys khác nhau theo version; file cũ vẫn truy cập được.
12. History pagination và UTF-8 tiếng Việt.

## Build/publish

Sau khi code: kiểm tra diff/encoding; chạy `dotnet restore`, `dotnet build -c Release`, toàn bộ test và `dotnet publish -c Release` theo `AGENTS.md`. Không deploy. Báo command, exit code, test pass/fail/skip, warnings và publish folder.

## Báo cáo cuối

Liệt kê kiến trúc, file code/SQL, thứ tự SQL, backfill, folder storage, job/billing/version/selection/outdated logic, test/build/publish, smoke-test checklist và kết luận `READY FOR SCENE MEDIA VERSIONING SQL REVIEW` hoặc `NOT READY` kèm blocker. Nhắc rõ SQL CHƯA ĐƯỢC CHẠY.

