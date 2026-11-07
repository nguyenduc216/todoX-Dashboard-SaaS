# PROMPT CODEX — THIẾT KẾ RENDER VIDEO JOB THEO 4 TAB

Đọc và tuân thủ toàn bộ `AGENTS.md` trước khi thực hiện.

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Phạm vi chính:

- `/render-jobs`
- Editor full-screen của video project
- `RenderJobs.razor`
- `RenderVideoJobs.razor`
- Prompt parser, project/scene models và repositories
- Image/video render và versioning services

## 1. Mục tiêu chính xác

Editor Video Render Project chỉ có **4 tab**:

```text
1. Thông tin
2. Scene / Hình ảnh
3. Video
4. Kết quả
```

Không tạo tab Nhân vật riêng. Phần chọn nhân vật vẫn nằm trong tab Thông tin như giao diện hiện tại.

Workflow:

```text
Nhập thông tin + chọn nhân vật
→ kiểm tra cấu trúc prompt
→ tách scene
→ chỉnh/render/chọn ảnh từng scene
→ render video từ selected image của từng scene
→ ghép các selected scene video thành final video
```

Giữ `/render-jobs` là trang danh sách/dashboard duy nhất. Project mở trong editor full-screen cuộn được.

## 2. Quy định bắt buộc

- Đọc `AGENTS.md`; kiểm tra `git status` trước và sau.
- Không tạo migration và không tự chạy SQL/database.
- Nếu thiếu schema, chỉ tạo SQL idempotent có preflight/verify/rollback để chủ hệ thống tự chạy.
- Không hard-code provider, model, secret, user/customer/tenant, cost/point.
- Không làm hỏng YEScale image, billing, media storage, selected versions và version history.
- Không bật mock video trong production.
- Không deploy production; chỉ publish artifact riêng.
- Không commit `bin`, `obj`, publish, log hoặc ZIP không cần thiết.

## 3. Giữ nguyên phần đang đúng

Tab Thông tin hiện tại có bố cục hai vùng:

- Chọn nhân vật.
- Nhập prompt video và kiểm tra cấu trúc.

Giữ nguyên giao diện và luồng đang hoạt động của hai vùng này. Chỉ bổ sung các trường còn thiếu và sửa validation. Không tách Nhân vật thành tab riêng và không thiết kế lại ngoài phạm vi cần thiết.

Phần nhân vật vẫn phải:

- Chọn một character hoạt động.
- Hiển thị tên và ảnh master của character đã chọn.
- Click xem ảnh lớn.
- Cho phép `Không sử dụng nhân vật`.
- Ảnh character được truyền làm reference cho render ảnh scene.

## 4. Cấu trúc editor và scroll

Header full-screen hiển thị tên project, ID, trạng thái, aspect ratio, resolution và nút đóng.

Tabs:

1. Thông tin.
2. Scene / Hình ảnh.
3. Video.
4. Kết quả.

Body phải cuộn độc lập:

```css
.render-project-dialog-shell {
  height: 100dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.render-project-dialog-body {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
}
```

Ưu tiên wrapper HTML thật cho layout quan trọng để tránh CSS isolation không tác động lên MudBlazor child component.

## 5. Tab 1 — Thông tin

Giữ nguyên phần chọn nhân vật và phần nhập prompt video hiện tại.

Các trường/tab Thông tin gồm:

- Character.
- Prompt video/original prompt.
- Tổng thời lượng.
- Thời lượng mặc định mỗi scene.
- Suy nghĩ kịch bản nếu đang dùng.
- Kiểm tra cấu trúc.
- Aspect ratio.
- Resolution.
- Các summary đã parse: title, objective, style, CTA, duration, scene count.

### 5.1 Bổ sung aspect ratio

Prompt có thể chứa:

```json
"aspect_ratio": "16:9"
```

hoặc:

```json
"aspect_ratio": "9:16"
```

Parser hỗ trợ thêm alias nếu dữ liệu cũ có:

- `aspectRatio`
- `video_aspect_ratio`
- `ratio`

Nhưng phải chuẩn hóa về đúng:

- `16:9` — video ngang.
- `9:16` — video dọc.

Khi bấm `Kiểm tra cấu trúc`:

- Prompt có aspect ratio hợp lệ: tự điền dropdown.
- Prompt không có: để người dùng chọn.
- Prompt có giá trị khác: báo `Render Video Job hiện chỉ hỗ trợ 16:9 hoặc 9:16` và yêu cầu chọn lại.

Không cho Tạo/Phân tách scene nếu chưa có aspect ratio hợp lệ.

### 5.2 Bổ sung resolution

Prompt có thể chứa:

```json
"resolution": "720p"
```

hoặc `1080p`, `4K` nếu provider/capability thực tế hỗ trợ.

Parser có thể hỗ trợ alias:

- `video_resolution`
- `output_resolution`
- `quality_resolution`

Khi kiểm tra cấu trúc:

- Có resolution hợp lệ: tự điền field/dropdown.
- Không có: người dùng bắt buộc chọn hoặc nhập trước khi render video.
- Không hard-code danh sách capability nếu hệ thống đã có provider config; ưu tiên lấy danh sách resolution được hỗ trợ từ cấu hình provider/capability.
- Nếu chưa có provider video thật, dùng danh sách cấu hình hệ thống rõ ràng, không tuyên bố provider hỗ trợ resolution chưa xác minh.

UI resolution nên là dropdown có thể cấu hình, ví dụ:

- 720p.
- 1080p.
- 4K chỉ khi provider hỗ trợ.

Hiển thị nhãn rõ:

```text
Tỷ lệ video: 16:9 — Ngang
Độ phân giải: 1080p
```

Aspect ratio không phải resolution. Không dùng chung một field.

### 5.3 Nguồn dữ liệu chuẩn

Sau khi parse/chọn:

- AspectRatio lưu ở project/draft và truyền vào image/video request.
- Resolution lưu ở project/draft và truyền vào video request.
- Image resolution nếu khác video resolution phải dùng field/config riêng; không tự suy đoán rằng hai loại hoàn toàn giống nhau.
- Version image/video phải snapshot ratio/resolution phù hợp nếu schema đã hỗ trợ.

Nếu schema thiếu nơi lưu, không migration; chỉ chuẩn bị SQL để chủ hệ thống duyệt.

### 5.4 Kiểm tra cấu trúc

Sau khi bấm Kiểm tra cấu trúc phải hiển thị summary:

- Video title.
- Video objective.
- Style.
- CTA.
- Declared duration.
- Scene count.
- Aspect ratio.
- Resolution.
- Cảnh báo field thiếu/không hợp lệ.

Nếu prompt có scenes hợp lệ, lưu parse result để dùng khi bấm `Tạo / phân tách scene`; chưa tự render API tại bước kiểm tra.

## 6. Tạo và phân tách scene

Nút `Tạo / phân tách scene` nằm ở tab Thông tin.

Khi bấm:

1. Validate prompt.
2. Validate aspect ratio.
3. Tạo/lưu project nếu chưa có.
4. Tạo danh sách scene và prompt scene.
5. Chuyển sang tab `Scene / Hình ảnh`.
6. Không tự động render video.

Việc tự render ảnh sau khi tách scene chỉ giữ nếu đúng quy trình sản phẩm đã thống nhất; nếu tab Hình ảnh cần cho người dùng kiểm tra prompt/character trước thì không auto-render tất cả. Ưu tiên hiển thị scene plan và cho người dùng chủ động `Render tất cả ảnh còn thiếu`.

## 7. Tab 2 — Scene / Hình ảnh

Tab này kết hợp:

- Danh sách scene sau khi phân tách.
- Chỉnh prompt ảnh.
- Chọn character/reference theo scene nếu cần.
- Render và chọn image version.
- Thêm scene thủ công.

Giao diện tham khảo dạng danh sách hàng, không trộn video vào tab này.

Toolbar:

- Tổng scene.
- Chọn tất cả.
- `+ Thêm scene`.
- Xóa scene đã chọn.
- Render tất cả ảnh còn thiếu.
- Render lại scene được chọn.
- Refresh.
- Thống kê chưa render/đang render/hoàn tất/lỗi.

### 7.1 Mỗi hàng scene

Bố cục gợi ý:

```text
Checkbox | Scene/STT | Character/reference | Selected image | Image prompt | Trạng thái | Thao tác
```

Mỗi scene hiển thị:

- Scene number/title.
- Duration.
- Purpose.
- Character/reference thumbnails.
- Khung selected image.
- Image prompt textarea/editable.
- Aspect ratio.
- Trạng thái render ảnh.
- Render ảnh/Render lại ảnh.
- Lịch sử ảnh.
- Chọn image version mặc định.
- Edit scene.
- Di chuyển lên/xuống.
- Xóa scene.

Không hiển thị video preview hoặc nút render video trong tab này.

### 7.2 Render ảnh

Render ảnh dùng:

- Image prompt hiện tại.
- Character/reference đang chọn.
- Aspect ratio project.
- Image resolution/config phù hợp.
- Provider capability từ database.
- Media storage TodoX.
- Logical request idempotent và billing đúng một lần.

Mỗi lần render tạo image version mới, không ghi đè version cũ.

Lịch sử ảnh hiển thị thumbnail, prompt snapshot, character/reference, ratio, resolution, provider/model/task/time/cost/status. Người dùng chọn lại ảnh cũ và chọn dùng lại prompt snapshot nếu muốn.

### 7.3 Thêm scene thủ công

Nút `+ Thêm scene` mở dialog gồm:

- Vị trí: cuối danh sách hoặc sau scene đang chọn.
- Title.
- Purpose.
- Duration seconds.
- Character/reference; mặc định kế thừa project.
- Image prompt.
- Motion/video prompt.
- Voice.
- Voice instruction.
- Aspect ratio kế thừa project.

Sau khi thêm:

- Persist scene thật vào database.
- Không chỉ thêm trong UI memory.
- Re-index scene liên tục và transaction-safe.
- Cập nhật scene_count và kiểm tra tổng duration.
- Ghi event `SCENE_MANUALLY_ADDED`.
- Không tự render ảnh/video.
- Không dùng `ReplaceScenesAsync` theo cách xóa toàn bộ scene, vì sẽ làm mất liên kết versions.

### 7.4 Edit/move/delete scene

- Edit cập nhật prompt/duration/character nhưng không sửa version snapshot cũ.
- Reorder cập nhật indexes atomically.
- Delete phải xác nhận, đặc biệt khi có versions.
- Không xóa media/version vật lý ngoài ý muốn.

### 7.5 Chuyển sang Video

Có nút `Tiếp tục tạo video`.

- Chuyển tab Video.
- Không tự render video nếu chưa xác nhận.
- Scene chưa có selected image vẫn xuất hiện trong Video nhưng nút render bị disabled và có hướng dẫn quay lại tab Hình ảnh.

## 8. Tab 3 — Video

Tab Video dùng selected image list từ tab Scene/Hình ảnh để tạo video tương ứng.

Giao diện dạng grid/card giống hình tham khảo.

Toolbar:

- Tổng scene.
- Đủ ảnh.
- Đang render.
- Hoàn tất.
- Lỗi.
- List/grid toggle.
- Render video còn thiếu.
- Refresh.

### 8.1 Video card

Mỗi card gồm:

- Scene index/title.
- Selected image source thumbnail.
- Video preview/controls.
- Nếu chưa có video: ảnh selected làm poster và `Sẵn sàng tạo video`.
- Aspect ratio.
- Resolution.
- Duration.
- Motion/video prompt editable.
- Voice/voice instruction.
- Trạng thái.
- Tạo video/Render lại video.
- Lịch sử video.
- Chọn video version mặc định.
- Mở rộng preview.

### 8.2 Render video

Bắt buộc dùng:

- Selected image version đang hiển thị.
- MediaId/ObjectKey TodoX; không dùng URL provider tạm thời.
- Motion prompt đang chỉnh.
- Image prompt snapshot.
- Voice và voice instruction.
- Duration.
- Aspect ratio project.
- Resolution project.
- Provider capability resolve từ database.
- Logical request idempotent và billing đúng một lần.

Mỗi image scene tương ứng tạo một video scene.

Mỗi lần render tạo scene video version mới, giữ version cũ và lineage tới source image version.

Nếu video provider thật chưa có:

- Nút disabled có tooltip.
- Không dùng mock production.
- Hoàn thiện UI/request boundary nhưng báo blocker rõ.

### 8.3 Lịch sử video

Hiển thị:

- Video.
- Source image và source image version.
- Prompt snapshots.
- Voice snapshots.
- Ratio/resolution/duration.
- Provider/model/task/time/cost/status.
- Selected/default.

Người dùng có thể chọn lại video cũ làm mặc định.

## 9. Tab 4 — Kết quả

Tab này dành cho bước ghép final video sau này:

- Danh sách selected video theo thứ tự scene.
- Kiểm tra scene thiếu video.
- Kiểm tra aspect ratio/resolution nhất quán.
- Tổng duration.
- Preview từng scene.
- Ghép final video.
- Trạng thái merge.
- Preview/download final video.
- Lịch sử final versions.
- Lịch sử execution jobs/log.

Chặn merge nếu thiếu selected video, video chưa ready hoặc khác ratio/resolution project.

Nếu merger chưa có thật, giữ UI/boundary disabled và không giả lập thành công production.

## 10. Working draft và polling

Tạo state theo scene:

```text
SceneEditorState
- SceneId
- Character/reference
- Purpose
- Duration
- ImagePrompt
- MotionPrompt
- Voice
- VoiceInstruction
- SelectedImageVersionId
- SelectedVideoVersionId
- IsDirty
```

- Polling không ghi đè dirty fields.
- Chuyển tab không mất draft.
- Đóng editor có dirty state phải cảnh báo.
- Save scene atomic: prompts, character, selected image/video và projection.
- Lỗi save rollback và giữ draft.

## 11. Dashboard và route

- `/render-jobs` vẫn là dashboard/list duy nhất.
- `/render-job` chỉ redirect tương thích, không phục hồi danh sách thứ hai.
- Tạo/Mở project bằng editor full-screen.
- Đóng editor refresh dashboard.
- Popup cuộn được tới scene cuối trên desktop/mobile.

## 12. Responsive

- Tab Scene/Hình ảnh: desktop table/list; tablet/mobile card/list phù hợp.
- Tab Video: desktop grid 2–3 cột, tablet 2, mobile 1.
- Không overflow ngang không cần thiết.
- Preview media dùng `object-fit:contain`, không crop media chính.

## 13. Test bắt buộc

1. Editor chỉ có đúng 4 tabs.
2. Phần Nhân vật vẫn nằm trong tab Thông tin.
3. Parse aspect_ratio 16:9/9:16 và aliases.
4. Parse resolution và aliases.
5. Prompt có ratio/resolution tự điền fields.
6. Thiếu ratio buộc chọn; resolution validation đúng thời điểm.
7. Phân tách scene chuyển đúng tab Hình ảnh.
8. Thêm scene thủ công persist DB và re-index.
9. Thêm scene không xóa versions scene khác.
10. Tab Hình ảnh không chứa video UI.
11. Render ảnh dùng đúng character/prompt/ratio.
12. Image render tạo version mới.
13. Chọn image version cũ cập nhật selected đúng.
14. Tab Video dùng đúng selected image.
15. Video request nhận current motion prompt, ratio và resolution.
16. Video version lưu source image lineage.
17. Polling không ghi đè dirty draft.
18. Chuyển tab không mất dữ liệu.
19. Merge bị chặn nếu thiếu/sai ratio-resolution.
20. Popup cuộn được tới cuối.
21. Quyền tenant/user/customer đúng.

## 14. Browser QA bắt buộc

Với project ít nhất 7 scene, chụp:

- Tab Thông tin có character, prompt, aspect ratio, resolution.
- Tab Scene/Hình ảnh có scene tự động và scene thủ công.
- Tab Video dạng grid.
- Tab Kết quả.

Viewport:

- 1920x1080
- 1366x768
- 1024x768
- 390x844

Kiểm tra scroll popup, chuyển tab, không mất draft, không crop media và không xuất hiện danh sách project thứ hai.

## 15. Build và publish

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-video-four-tabs
```

Không deploy production.

## 16. Báo cáo cuối

Báo cáo:

- Đã đọc AGENTS.md.
- Xác nhận chỉ có 4 tabs.
- Phần hiện tại nào được giữ nguyên.
- Cách parse/lưu aspect ratio và resolution.
- Thêm scene thủ công được persist thế nào.
- Image/video version và lineage.
- Video provider/merger thật hay vẫn disabled.
- Có cần SQL không.
- Test/build/publish.
- Screenshot 4 tabs và breakpoints.
- Blocker còn lại.

Không báo hoàn thành nếu:

- Có tab Nhân vật riêng.
- Không giữ phần Nhân vật trong tab Thông tin.
- Không tự điền ratio/resolution từ prompt.
- Tab Scene/Hình ảnh vẫn trộn video UI.
- Chưa thêm scene thủ công được.
- Render video lấy sai selected image.
- Popup không cuộn được.
- `/render-job` lại có danh sách thứ hai.
