# TDC-RDN-79AI-KLING-MOTION-SUBMIT-ROOT-CAUSE-FIX-20260915-003

Ngày kiểm tra: 2026-09-15

## 1. Phạm vi và baseline

- Repository: `nguyenduc216/todoX-Dashboard-SaaS`
- Branch: `feature/rdn-onepage-ui-revamp`
- Base SHA: `16fa33eba1c3998710c3ea0a92a6de3a0260e454`
- Remote SHA trước khi kiểm tra: `16fa33eba1c3998710c3ea0a92a6de3a0260e454`
- Working tree trước khi kiểm tra: sạch.

Phạm vi chỉ là chẩn đoán đường submit 79AI Kling Motion của RDance. Không gọi provider thật, không sửa database, không sửa cấu hình production, không sửa billing, points, duration, UI upload, TikTok, RVideo, Timelapse, image generation, voice/audio hay render pipeline.

## 2. Runtime evidence đã đối chiếu

Job RDance `cb0be134-f835-4f02-8f66-3db9bc00bb9d`, render job `84572167-dc6b-4fa5-95f7-dabd590ebc38`, operation `23f20dd3-184c-438e-a6a7-b28155a15802` đã:

1. Enqueue và được worker claim.
2. Upload ảnh tham chiếu lên 79AI thành công.
3. Verify ảnh qua danh sách provider thành công; URL danh sách trả về có hậu tố `?full=1`.
4. Upload video motion lên 79AI thành công.
5. Verify video qua danh sách provider thành công.
6. Gọi submit Motion Control, sau đó nhận lỗi provider `Tải media lên thất bại, vui lòng kiểm tra file và thử lại.`

`provider_task_id` vẫn `NULL`, nên lỗi nằm sau media upload/verify nhưng trước khi provider tạo task.

## 3. Call graph thực tế

```text
RDanceJobDetail.razor
  ConfirmAndQueueAsync()
    QueueRenderFromUserActionAsync()
      IDanceSellPhase2Service.QueueRenderAsync()
        POST /api/dance-sell/jobs/{id}/render
        tạo render.render_jobs (job_type=dance_sell)
          DanceSellRenderHandler.HandleAsync()
            Submit79AiAsync()
              UploadMediaAsync() ảnh
              ListImagesAsync() verify ảnh
              UploadMediaAsync() video
              ListVideosAsync() verify video
              BeginMotionSubmitAttemptAsync()
              IAi79TaskClient.SubmitMotionControlAsync()
                POST 79AI Motion endpoint
                ReadSubmitResultAsync()
              UpdateSubmittedAsync()/MarkSubmittedAsync() khi có task id
```

Lỗi runtime dừng tại `Ai79TaskClient.SubmitMotionControlAsync()` / `ReadSubmitResultAsync()`. Vì provider không trả task id, `UpdateSubmittedAsync()` và `MarkSubmittedAsync()` không được gọi.

## 4. HTTP request hiện tại

`DanceSellRenderHandler.Resolve79AiRuntimeAsync()` lấy route cho `kling_video_motion_3`. Nếu không có override trong route config, endpoint submit là:

```text
POST /ai/jobs/video/kling_video_motion_3
Content-Type: application/x-www-form-urlencoded
Authorization: Bearer <access token>
```

`Ai79TaskClient.SubmitMotionControlAsync()` gửi các field:

```text
domain
project_id
model = kling_video_motion_3
prompt
image_url
video_url
subType = motion
background_source = input_video
mode = standard
ratio = default
images[0][url]
```

`access_token` không nằm trong form body; token chỉ có ở Bearer header. Đây là contract v2 đã được ghi nhận trong `docs/dance-sell-79ai-v2-auth-contract-fix-report.md`, và cùng cơ chế Bearer đang được upload ảnh/video dùng thành công.

## 5. So sánh với contract đã có trong repository

| Field / thuộc tính | RDance hiện tại | Contract v2 trong repository | Kết luận |
| --- | --- | --- | --- |
| Endpoint | `/ai/jobs/video/kling_video_motion_3` | `/ai/jobs/video/kling_video_motion_3` | Khớp |
| Method | `POST` | `POST` | Khớp |
| Encoding | form-urlencoded | form-urlencoded | Khớp |
| Auth | Bearer token | Bearer token | Khớp |
| `domain`, `project_id` | Có | Có | Khớp |
| `model` | Có | Có | Khớp |
| `image_url`, `images[0][url]` | Có | Có | Khớp |
| `video_url` | Có | Có | Khớp |
| `subType`, `background_source`, `mode`, `ratio` | Có | Có | Khớp |
| `access_token` trong body | Không có | Không được gửi | Khớp |

RVideo dùng endpoint/contract khác (`/create-video`), nên không phải bằng chứng để thay đổi payload riêng của Kling Motion.

## 6. Điều tra `?full=1`

Không có hàm nào strip query string `?full=1`.

- `VerifyProviderImageAsync()` lưu URL trả về trực tiếp từ upload trong `uploadUrl` và lưu URL từ danh sách verify riêng trong `verificationMatchedUrl`.
- `Submit79AiAsync()` cố ý dùng `referenceUpload.Url` làm `referenceUrlUsed`.
- Commit `a827d51` (`fix(rdance): preserve provider upload urls`) đã thay đổi có chủ đích từ URL danh sách verify về URL canonical do upload trả về.

Vì vậy URL không có `?full=1` trong event submit là hành vi đã được thiết kế và được lưu vết rõ ràng, không phải mất query string do serialization. Không có bằng chứng trong repository rằng Kling Motion bắt buộc phải dùng URL verify có `?full=1`; đổi URL này sẽ là suy đoán.

## 7. Provider error và giới hạn của bằng chứng

`ReadSubmitResultAsync()` giữ HTTP status, provider error code và sanitized response JSON trong `Ai79TaskSubmitException`. Runtime evidence cung cấp chỉ có message đã map:

```text
79AI video submit failed:
Tải media lên thất bại, vui lòng kiểm tra file và thử lại.
```

Chưa có HTTP status, provider response code, response JSON đã sanitize, hoặc request trace độc lập của 79AI cho operation đích. Vì vậy chưa thể xác định provider đang từ chối:

- URL ảnh,
- URL video,
- project/domain asset ownership,
- asset availability tại thời điểm provider fetch,
- hoặc một quy tắc media/model nội bộ của 79AI.

## 8. `KIE_TASK_FAILED`

`KIE_TASK_FAILED` được phát từ `DanceSellCompletionService.FailAsync()`, là service dùng chung từ trước khi có đường 79AI. Submit/poll 79AI dùng event riêng như `AI79_MOTION_SUBMIT_STARTED`, `AI79_MOTION_SUBMIT_FAILED`, `AI79_MOTION_TASK_SUBMITTED`, và `AI79_MOTION_TASK_POLLING`.

Kết luận: đây là legacy event naming ở lớp completion chung, không phải bằng chứng RDance đã đi nhầm vào KIE orchestration.

## 9. Root-cause gate

### Root cause

Chưa chứng minh được root cause chính xác từ code và runtime evidence hiện có.

### Failing method

`Ai79TaskClient.SubmitMotionControlAsync()` gọi `ReadSubmitResultAsync()`, nhận provider error trước khi có provider task id.

### Failing request

Request hiện tại khớp contract Motion v2 đã lưu trong repository, như mục 4 và 5.

### Expected request

Không có request shape khác được chứng minh bởi source hiện có. Contract v2 hiện tại chính là request expected trong repository.

### Vì sao upload thành công nhưng submit thất bại

Upload/verify chỉ xác nhận asset đã được tạo và có mặt trong media list. Submit yêu cầu provider thực hiện validation/fetch media trong context của model Motion. Provider đã từ chối ở bước này, nhưng response diagnostic được cung cấp chưa đủ để xác định điều kiện nào bị từ chối.

## 10. Thay đổi tối thiểu được khuyến nghị

Không sửa production payload ở thời điểm này.

Minimum next step an toàn là lấy dữ liệu đã được redact của đúng operation/render job sau lần retry:

1. HTTP status của submit.
2. Provider error code.
3. Sanitized provider response JSON.
4. Sanitized request metadata gồm endpoint, model, mode, ratio, subtype, tên field và media host.
5. Xác nhận route config thực tế của `kling_video_motion_3` tại môi trường deploy, gồm base URL, submit path, project ID và domain.

Chỉ khi dữ liệu đó chỉ ra khác biệt contract cụ thể mới nên sửa `Ai79TaskClient.SubmitMotionControlAsync()` hoặc route config. Không thêm `id_base`, không gửi lại `access_token` trong body, và không đổi sang `?full=1` khi chưa có bằng chứng.

## 11. Retry cho job lỗi hiện có

Job đã terminal failed không được worker tự chạy lại. Sau khi có fix đã được chứng minh và deploy, owner dùng action retry RDance hiện có cho job `cb0be134-f835-4f02-8f66-3db9bc00bb9d` (endpoint `POST /api/dance-sell/jobs/{id}/retry`, UI retry tương ứng). Không cập nhật database trực tiếp.

## 12. Kết quả task

- Production code: không đổi.
- Tests: không chạy vì không có thay đổi code để validate.
- Build: không chạy; task dừng tại root-cause gate.
- Publish: không chạy; không có artifact mới.
- Database/schema: NO.
- Production deploy/smoke test: NONE.
- Commit: không tạo.
- Push: không thực hiện.
- Final SHA: `16fa33eba1c3998710c3ea0a92a6de3a0260e454` (trước khi thêm báo cáo chẩn đoán).

## 13. Checklist

- [x] Kiểm tra repository, branch, local HEAD, remote HEAD và working tree.
- [x] Trace UI, API, queue, worker, upload, verify, submit và error mapping.
- [x] Đối chiếu payload submit với contract 79AI v2 lưu trong repository.
- [x] Điều tra URL `?full=1`.
- [x] Phân loại `KIE_TASK_FAILED`.
- [x] Không thay đổi subsystem được bảo vệ.
- [x] Không thực hiện speculative production fix.
