# Prompt gửi Codex — sửa metadata render ảnh scene

Bạn đang làm việc trên repository `nguyenduc216/todoX-Dashboard-SaaS`.

## Quy tắc bắt buộc

1. Đọc toàn bộ `AGENTS.md` trước khi phân tích hoặc sửa code.
2. Kiểm tra `git status`, giữ nguyên thay đổi không thuộc nhiệm vụ.
3. Không sửa `keys/todox-vertex-sa.json`, không hard-code secret.
4. Không tạo migration, không chạy SQL, không deploy production.
5. Giữ UTF-8 và tiếng Việt có dấu.
6. Sau khi sửa phải chạy restore, test, build Release và publish local artifact.

## Phạm vi công việc

Kiểm tra commit mới nhất, tối thiểu bao gồm commit `7b332d2ba4ed13d4972caa178c1f6921332bef58`.

Trong luồng tạo/phân tách scene và enqueue `render_scene_images`, loại bỏ metadata/log còn hard-code hoặc mô tả sai là Vertex/Google Cloud khi provider thực tế được router chọn từ database và mặc định dự kiến là YEScale.

### Yêu cầu cụ thể

1. Trong `RenderVideoJobs.razor`, không dùng metadata gây hiểu nhầm:

```csharp
ProviderCode = "todox_image"
ModelCode = "vertex_scene_image"
```

Hãy dùng mã trung lập thể hiện job đi qua router, ví dụ:

```csharp
ProviderCode = "configured_image_router"
ModelCode = "scene_image_default"
```

Nếu codebase đã có constant/mã chuẩn tương đương thì tái sử dụng constant đó, không tạo chuỗi trùng lặp.

2. Sửa comment, Snackbar, project event và log đang ghi “Google Cloud” hoặc “Vertex” trong luồng scene image batch thành nội dung trung lập, ví dụ “AI provider đã cấu hình” hoặc “provider ảnh mặc định”.

3. Không đổi cơ chế routing thực tế. Provider/model thực tế vẫn phải do `AiImageRenderRouter` chọn từ capability `scene_image_generation` trong database.

4. Khi provider trả kết quả, bảo đảm provider/model thực tế tiếp tục được lưu tại:

- `video_render.scene_image_versions.provider_code`
- `provider_capability_id`
- `requested_model`
- `actual_model`
- `provider_task_id`
- `billing_logical_request_id`
- `estimated_usd`
- `actual_usd`
- `charged_points`
- provider usage/attempt logs

5. Job-level metadata chỉ là routing metadata, không được ghi đè provider/model thực tế vào lịch sử billing.

6. Tìm toàn repository các chuỗi liên quan `Google Cloud`, `Vertex`, `vertex_scene_image`, `todox_image` và chỉ sửa những chuỗi thuộc luồng scene image đã chuyển sang router. Không sửa provider Vertex ở chức năng khác nếu nó vẫn thực sự sử dụng Vertex.

7. Bổ sung test tối thiểu xác nhận model/provider job metadata của scene image không còn hard-code Vertex và kết quả provider thực tế vẫn lấy từ router outcome.

## Kiểm tra bắt buộc

Chạy và báo nguyên kết quả:

```powershell
dotnet --info
dotnet restore
dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release
dotnet build -c Release --no-restore
dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o artifacts/publish
```

Nếu solution cần đường dẫn khác, xác định đúng `.sln`/`.csproj` và ghi rõ lệnh thực tế.

## Báo cáo cuối

Nêu rõ:

1. Quy tắc `AGENTS.md` đã tuân thủ.
2. File và chuỗi metadata đã sửa.
3. Metadata routing mới.
4. Nơi lưu provider/model thực tế.
5. Test pass/fail.
6. Build/publish pass/fail và đường dẫn artifact.
7. Xác nhận không tạo migration, không chạy SQL, không deploy, không commit/push.

