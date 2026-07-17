# PROMPT CODEX — HOÀN THIỆN TOÀN BỘ GIAO DIỆN RENDER VIDEO JOB

Đọc toàn bộ `AGENTS.md` trước khi thực hiện và tuân thủ tuyệt đối. Repository: `nguyenduc216/todoX-Dashboard-SaaS`. Phạm vi chính: `/render-job`.

## 1. Kết quả cần đạt

Thiết kế lại chức năng theo đúng luồng:

1. Trang `/render-job` chỉ là danh sách dự án và nút tạo dự án.
2. Chọn dự án sẽ mở editor full-screen, luôn hiện rõ tên, ID và tỷ lệ dự án.
3. Mỗi project bắt buộc có `16:9` (ngang) hoặc `9:16` (dọc).
4. Nếu prompt có `aspect_ratio` hợp lệ thì tự chọn; nếu không có thì bắt buộc người dùng chọn.
5. Mỗi scene trên desktop hiển thị theo một workflow ngang:

   `Khung ảnh -> Prompt ảnh -> Khung video -> Prompt video -> Lưu scene`

6. Khung ảnh và video trên UI là hình vuông; output render vẫn đúng `16:9` hoặc `9:16`.
7. Khu prompt ảnh chứa render/render lại và lịch sử ảnh.
8. Khu prompt video chứa tạo/render lại và lịch sử video.
9. Render video dùng đúng ảnh version đang chọn và prompt video đang chỉnh.
10. Lưu scene atomically biến các prompt và version đang chọn thành mặc định.

Không được báo hoàn thành nếu giao diện vẫn là các banner media toàn chiều ngang hoặc toàn bộ nội dung scene xếp thành một cột trên desktop.

## 2. Quy định an toàn

- Đọc `AGENTS.md` và kiểm tra `git status` trước khi sửa.
- Không tạo migration; không chạy SQL/database.
- Nếu schema thiếu, chỉ tạo SQL idempotent có preflight/verify/rollback để chủ hệ thống tự chạy.
- Không hard-code provider, model, key, user/customer/tenant, cost/point.
- Không làm hỏng YEScale image, billing, storage, image/video versioning.
- Không dùng mock video trên production.
- Không deploy production; chỉ publish ra thư mục artifact riêng.
- Không commit `bin`, `obj`, publish, log hoặc ZIP artifact không cần thiết.

## 3. Khảo sát trước khi code

Đọc tối thiểu:

- `RenderVideoJobs.razor` và `.razor.css`
- `ScenePromptEditorDialog.razor`
- `ScenePromptMetadata.cs`
- `TodoXVideoPromptParser.cs`
- `VideoRenderRepository.cs`
- `SceneMediaVersioningService.cs`
- `SceneImageRenderService.cs`
- `SceneImageBatchRenderHandler.cs`
- Các handler video và models liên quan
- Schema project, scene, image version, video version, final version

Xác nhận trước khi sửa:

- Aspect ratio đang lưu ở đâu.
- Project/version đã có aspect ratio hay chưa.
- Selected image/video version đang cập nhật thế nào.
- Video provider thật đã có hay chỉ có mock/boundary.

## 4. Sửa lỗi layout hiện tại đã xác định

Code hiện tại sai vì:

- Dùng `MudStack` dọc cho toàn bộ scene.
- `.scene-media-frame` có `width:100%`, `aspect-ratio:1/1`, đồng thời `max-height:520px`; max-height làm khung mất hình vuông khi màn hình rộng.
- `.scene-media-image` và `.scene-media-video` dùng `object-fit:cover`, làm crop nội dung.
- CSS isolation có thể không tác động đúng class đặt trên component MudBlazor.

Phải:

- Xóa `max-height` làm phá tỷ lệ.
- Không dùng `object-fit:cover` cho media chính.
- Bọc layout quan trọng trong thẻ HTML thật (`div`) hoặc dùng `::deep` đúng cách.
- Không vá bằng inline style rải rác.

## 5. Project list và editor full-screen

Trang chính hiển thị card dự án gồm thumbnail, tên, ID, character, aspect ratio, số scene, tiến độ ảnh/video, trạng thái, ngày cập nhật và nút Mở.

Khi Mở/Tạo dự án:

- Mở full-screen dialog/editor, không để project editor lẫn dưới danh sách.
- Header sticky: `[Tên] — Project #ID — 16:9 Ngang/9:16 Dọc` và nút đóng.
- Tabs: `Thông tin`, `Scene`, `Kết quả`.
- Giữ deep link `/render-job?projectId=...`.
- Khi đổi project: dừng polling cũ, clear state cũ, load state mới, rồi start đúng một polling loop.
- Khi đóng có dirty state phải hỏi xác nhận.
- Project list phải load sau khi AuthState sẵn sàng, không chỉ khi `CurrentUser` tình cờ đã có trong `OnInitializedAsync`.

Nên tách component thay vì tiếp tục phình một file Razor:

- `RenderVideoProjectDialog.razor`
- `RenderVideoProjectInfoPanel.razor`
- `RenderVideoProjectScenesPanel.razor`
- `RenderVideoProjectResultPanel.razor`
- `SceneEditorCard.razor`

## 6. Quy định aspect ratio

Chỉ hỗ trợ `16:9` và `9:16`.

- Parse `aspect_ratio`; có thể tương thích `aspectRatio`, `video_aspect_ratio`, `ratio` nhưng chuẩn hóa về hai giá trị trên.
- Có giá trị hợp lệ trong prompt: tự preselect.
- Không có: bắt buộc chọn trước khi phân tách scene/render.
- Giá trị khác: báo lỗi rõ, không tự đổi âm thầm.
- Truyền xuyên suốt Project -> Scene -> image request -> image version -> video request -> video version -> final merge.
- Loại bỏ hard-code `9:16` trong luồng `/render-job`.
- Merge bị chặn nếu scene video khác tỷ lệ project.
- Đổi ratio sau khi có version phải cảnh báo chỉ áp dụng cho lần render mới.

Nếu DB thiếu nơi lưu project/version aspect ratio, chỉ chuẩn bị SQL độc lập trong `database/video-render-aspect-ratio/`; không chạy.

## 7. Bố cục scene desktop bắt buộc

Mỗi scene là một card. Header gồm số scene, title, duration, status, aspect badge và menu phụ (lên/xuống/xóa).

Phần chính dùng CSS Grid 5 cột:

```css
.scene-workflow {
  display: grid;
  grid-template-columns:
    minmax(220px, 280px)
    minmax(260px, 1fr)
    minmax(220px, 280px)
    minmax(260px, 1fr)
    auto;
  gap: 12px;
  align-items: stretch;
  width: 100%;
}
```

Thứ tự đúng:

1. Khung ảnh vuông.
2. Panel prompt ảnh và các thao tác ảnh.
3. Khung video vuông.
4. Panel prompt video và các thao tác video.
5. Panel/nút Lưu scene.

Không xếp toàn bộ thành một cột trên desktop. Không để media chiếm toàn chiều rộng. Không gom mọi nút xuống cuối card.

Tablet: hai hàng `ảnh + prompt ảnh`, `video + prompt video`. Mobile: xếp dọc theo đúng thứ tự workflow.

## 8. Khung media vuông đúng kỹ thuật

Khung vuông chỉ là UI; file thật vẫn 16:9/9:16.

```css
.scene-media-square {
  position: relative;
  width: min(100%, 280px);
  aspect-ratio: 1 / 1;
  overflow: hidden;
  border-radius: 10px;
  background: #10161d;
  border: 1px solid var(--mud-palette-lines-default);
  margin-inline: auto;
}

.scene-media-square img,
.scene-media-square video {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  object-position: center;
  display: block;
}
```

- Không crop, không stretch.
- 16:9 có khoảng trống trên/dưới; 9:16 có khoảng trống hai bên.
- Khung ảnh/video bằng nhau, có badge ratio, trạng thái và nút xem full.
- Shimmer chỉ trong đúng media đang render.

## 9. Panel prompt ảnh

Panel cạnh khung ảnh gồm:

- Textarea `image_prompt` trực tiếp.
- Badge dirty nếu đã sửa chưa lưu.
- `Render ảnh`/`Render lại ảnh`.
- `Lịch sử ảnh`.
- Trạng thái/lỗi/provider/model nếu phù hợp.

Lịch sử mở bằng dialog/drawer, không kéo card dài. Hiển thị thumbnail, prompt snapshot, aspect ratio, provider/model/task/time/cost/status/selected.

Chọn version cũ cập nhật working selection và khung ảnh ngay. Có thao tác riêng `Dùng lại ảnh và prompt này`. Không đổi mặc định vĩnh viễn cho đến khi Lưu scene.

## 10. Panel video và prompt video

Khung video:

- Có video thì phát với controls.
- Chưa có video nhưng có ảnh: dùng selected image làm poster, hiển thị `Sẵn sàng tạo video`.
- Không có ảnh: báo `Cần chọn/render ảnh trước`.
- Queued/rendering/failed hiển thị riêng trong khung.

Panel prompt video cạnh khung gồm:

- Textarea `motion_prompt`/`video_prompt` trực tiếp.
- Badge dirty.
- `Tạo video`/`Render lại video`.
- `Lịch sử video`.
- Trạng thái/lỗi/provider/model.

Render video bắt buộc dùng:

- selected/pending image version đang hiển thị;
- MediaId/ObjectKey TodoX, không dùng URL YEScale tạm;
- motion prompt đang có trong textarea, không phải giá trị cũ trong DB;
- image prompt, voice, voice instruction hiện tại;
- duration và aspect ratio project;
- provider capability resolve từ DB;
- logical request idempotent.

Không double-submit/double-charge. Nếu provider thật chưa có thì giữ disabled với tooltip, không giả lập thành công.

Lịch sử video hiển thị video, ảnh nguồn, source image version, prompt snapshots, voice snapshots, ratio, provider/model/task/time/cost/status/selected. Chọn video cũ chỉ đổi working selection cho đến khi lưu.

## 11. Purpose, voice và voice instruction

Bên dưới workflow có hàng compact cho:

- Purpose.
- Voice textarea.
- Voice instruction textarea.

Sửa trực tiếp được; không chỉ `ShortText`. Dùng `ScenePromptMetadata` để giữ known fields và extra metadata. Không bắt buộc cả image/motion prompt chỉ để sửa voice.

Validation theo thao tác:

- Render ảnh cần image prompt.
- Render video cần selected image và motion prompt.
- Lưu scene cho phép trường tùy chọn trống theo nghiệp vụ.

## 12. Working draft và Lưu scene

Tạo state riêng theo scene, không bind trực tiếp DTO server khiến polling ghi đè textarea:

```text
SceneEditorState
- SceneId
- Purpose
- ImagePrompt
- MotionPrompt
- Voice
- VoiceInstruction
- SelectedImageVersionId
- SelectedVideoVersionId
- AspectRatio
- IsDirty
```

- Polling chỉ merge trạng thái server, không ghi đè field dirty.
- Chọn version/sửa prompt đánh dấu dirty.
- Đóng editor có dirty state phải cảnh báo.

`Lưu scene` phải transaction/atomic:

- lưu purpose, image prompt, motion prompt, voice, voice instruction;
- set đúng một selected image version và một selected video version;
- cập nhật projection URL/ObjectKey/MediaId trên scene;
- lưu aspect ratio, UpdatedAt và audit event;
- rollback toàn bộ nếu lỗi;
- giữ local draft nếu lỗi;
- thành công thì reload kiểm chứng và `IsDirty=false`.

Không sửa snapshot version cũ. Render mới tạo version mới với toàn bộ prompt/ratio/provider/task/billing/source lineage snapshot.

## 13. Dọn UI cũ

Xóa các thao tác trùng khỏi thanh cuối scene:

- Menu Prompt chung nếu textarea đã hiện trực tiếp.
- Render/lịch sử ảnh ở thanh chung.
- Render/lịch sử video ở thanh chung.

Menu ba chấm chỉ giữ thao tác phụ như lên/xuống/xóa. Nút Lưu scene phải nổi bật và riêng biệt.

## 14. Kiểm tra trình duyệt bắt buộc

Không kết luận bằng build. Chạy site và dùng Playwright/browser chụp:

- 1920x1080
- 1366x768
- 1024x768
- 390x844

Desktop phải nhìn thấy gần đủ `ảnh -> prompt ảnh -> video -> prompt video -> lưu` trên cùng hàng/vùng màn hình, không có banner media toàn chiều ngang.

Đo thực tế:

```javascript
const r = element.getBoundingClientRect();
Math.abs(r.width - r.height) <= 2;
getComputedStyle(img).objectFit === 'contain';
getComputedStyle(video).objectFit === 'contain';
```

Nếu không đạt thì tiếp tục sửa. Kiểm tra CSS isolation/MudBlazor bằng computed style, không chỉ nhìn source CSS.

## 15. Test bắt buộc

Test tối thiểu:

- Parse/validate 16:9 và 9:16; thiếu/không hợp lệ.
- Ratio truyền đúng vào image/video request và version snapshot.
- Working draft không bị polling ghi đè.
- Chọn ảnh cũ cập nhật đúng selected working image.
- Render video dùng đúng selected image và motion prompt đang chỉnh.
- Save scene atomic; lỗi thì rollback.
- Prompt edits không sửa snapshot version cũ.
- Project switching không rò state; polling đúng một loop.
- Scope tenant/user/customer.
- Nút video bị chặn khi thiếu ảnh/provider.

## 16. Build và publish

Chạy:

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-job-complete-ui
```

Không deploy production.

## 17. Báo cáo cuối

Báo cáo:

- đã đọc AGENTS.md;
- nguyên nhân layout cũ;
- component/file đã sửa;
- aspect ratio được parse/lưu/truyền ra sao;
- working draft/version selection/save atomic;
- video provider thật hay vẫn disabled;
- SQL có cần không và thứ tự chạy nếu có;
- test/build/publish;
- 4 screenshot và kích thước/computed style media;
- blocker còn lại.

Không báo hoàn thành nếu chưa kiểm chứng chuỗi:

`chọn ratio -> render ảnh -> chọn image version -> chỉnh motion prompt -> render video từ đúng ảnh -> chọn video version -> lưu scene -> reload vẫn giữ đúng mặc định`.
