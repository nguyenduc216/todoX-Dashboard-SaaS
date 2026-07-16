# Next Video Provider Plan

Tài liệu này mô tả hợp đồng dữ liệu cần giữ ổn định trước khi tích hợp provider video thật. Các model video bên dưới là mục tiêu theo yêu cầu sản phẩm; trước khi triển khai provider, phải xác minh lại bằng YEScale MCP và không được hard-code metadata trong UI.

## 1. Luồng image-to-video dự kiến

1. Người dùng chọn hoặc render ảnh tĩnh cho từng scene.
2. Scene giữ `selected_image_version_id` làm ảnh nguồn mặc định.
3. Job `render_video_job` tạo `scene_video_versions` cho từng scene sau khi job tồn tại.
4. Mỗi `scene_video_version` snapshot `source_image_version_id`, prompt ảnh, prompt video, scene config và render config.
5. Provider video submit async và trả về `provider_task_id`.
6. Worker poll task, lưu video vào storage key bất biến của version, rồi complete hoặc fail version.
7. Merge final video chỉ dùng danh sách `scene_video_version_id` đã snapshot trong `final_video_version_items`.

## 2. Hợp đồng request video provider

Request dự kiến cần tối thiểu:

- `logical_request_id`
- `tenant_id`, `customer_id`, `project_id`, `scene_id`, `render_job_id`
- `source_image_version_id`
- URL hoặc object key của ảnh nguồn đã chọn
- `image_prompt_snapshot`
- `video_prompt_snapshot`
- `scene_snapshot_json`
- `render_config_json`
- provider/capability/model lấy từ database config

TODO: Xác minh bằng YEScale MCP endpoint/protocol, request parameters bắt buộc, timeout, poll interval, pricing, rate limit và output format trước khi viết adapter thật.

## 3. Cách chọn source image version

Mặc định lấy `video_project_scenes.selected_image_version_id`. Nếu scene chưa có selected image version completed thì không submit video, đánh dấu scene video version failed với lỗi dữ liệu đầu vào thiếu. Không tự lấy ảnh mới nhất bằng thời gian nếu database chưa chọn rõ.

## 4. Submit/poll async

Provider video phải chạy theo job nền:

- Submit một lần cho mỗi `logical_request_id`.
- Lưu `provider_task_id` ngay sau submit.
- Poll bằng task ID cho đến trạng thái terminal.
- Retry chỉ dùng lại logical request hoặc tạo logical request mới theo chính sách retry đã định nghĩa.

TODO: Xác minh YEScale MCP cho cơ chế poll chính thức của `grok-video`, `grok-video-1.5` và `omni-flash`.

## 5. Provider task, cost và points

Mỗi lần provider được gọi phải có bản ghi đối soát:

- Provider/model thực tế.
- `provider_task_id`.
- Estimated/actual USD.
- Provider usage/status.
- TodoX reserved/charged/refunded points.
- Billing logical request ID.
- Link tới job, scene video version và source image version.

Không bypass billing cho admin/root; nếu dùng ví hệ thống thì vẫn phải có wallet hợp lệ theo schema.

## 6. Retry không double charge

Retry hợp lệ phải đảm bảo:

- Cùng `logical_request_id` trả về cùng version và cùng billing record.
- Không tạo thêm `version_number` cho retry idempotent.
- Không gọi provider lần hai nếu request trước đã có terminal successful result.
- Nếu tạo retry mới có chủ ý, logical request phải khác và UI/log phải thể hiện đó là version mới.

## 7. Model dự kiến

- `grok-video`: mặc định/giá rẻ.
- `grok-video-1.5`: chất lượng/backup.
- `omni-flash`: multi-mode.

TODO: Chưa triển khai provider video thật. Phải xác minh model ID, modalities, endpoint/protocol, async/polling, parameters, pricing, limits và deprecation status bằng YEScale MCP trước khi seed config hoặc viết adapter.

