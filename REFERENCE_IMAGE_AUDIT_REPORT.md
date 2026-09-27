# Reference Image Audit Report

Ngày audit: 2026-09-22

Repository: `todoX-Dashboard-SaaS`

Branch: `feature/admin-job-monitor`

Commit được audit: `b116298` — `fix: make AI prompt generation default in video creation UX`

Phạm vi: audit-only. Không sửa code, không tạo migration, không commit/push, không thay đổi render pipeline, provider, worker hoặc queue.

## 1. UI Flow

### File chính

`TodoX.Web/Components/Pages/RenderVideoJobs.razor`

### Upload hình ảnh tham chiếu

- Handler: `UploadReferenceCharacterAsync`
- Lưu file qua `MediaFiles.SaveAsync(..., "rvideo_character", ...)`.
- Sau khi upload, UI tạo `UploadedCharacterSnapshot`.
- State liên quan:
  - `_uploadedCharacter`
  - `_sharedReferenceImageUrl`
  - `_sharedReferenceImageObjectKey`
  - `_sharedReferenceImageMediaId`

### Character Library

- Handler: `OnCharacterChangedAsync`
- API/service call: `Characters.GetCharacterAsync(...)`
- Dữ liệu lấy từ Character:
  - `MasterImageUrl`
  - `MasterImageObjectKey`
  - `NormalizedPrompt`
- State liên quan:
  - `_characterMode`
  - `_selectedCharacterId`
  - `_selectedCharacter`

### Checkbox

`Không sử dụng nhân vật`

- State: `_skipCharacter`
- Handler: `OnSkipCharacterChanged`

`Giữ nguyên hình ảnh tham khảo cho mọi scene`

- State: `_useReferenceImageForAllScenes`

### Kết luận UI

UI đã nhận và giữ các giá trị character/reference/keep-reference. Các giá trị này được đưa vào project/job settings cho các bước render downstream, nhưng chưa được truyền vào request Generate AI Prompt.

## 2. Database Flow

### `public.todox_ai_character`

Dùng cho Character Library:

- `id`
- `master_image_url`
- `master_image_object_key`
- `normalized_prompt`

### `public.todox_ai_character_reference`

Dùng cho reference history/storage của Character và được truy cập bởi `AiCharacterRepository`.

Trong Prompt Assistant flow hiện tại, hệ thống chủ yếu dùng master image của Character; không thấy việc đọc trực tiếp bảng này để xây dựng input cho Prompt Assistant.

### `public.todox_ai_character_render`

Lưu lịch sử render Character. Không tham gia trực tiếp vào Prompt Assistant generation flow.

### `video_render.video_projects`

Các trường liên quan:

- `character_id`: liên kết Character Library.
- `source_image_url`: URL ảnh nguồn/reference của project flow.
- `uploaded_character_url`: cột có tồn tại, nhưng upload UI hiện tại chủ yếu giữ snapshot trong settings.

### `video_render.rvideo_job_settings`

Các giá trị liên quan:

- `skip_character`
- `use_reference_image_for_all_scenes`
- `character_mode`
- `selected_character_id`
- `character_snapshot_json`

`character_snapshot_json` lưu snapshot của Character Library hoặc ảnh upload.

Ví dụ snapshot Library:

```json
{
  "source": "LIBRARY",
  "id": 123,
  "name": "...",
  "masterImageUrl": "...",
  "storageKey": "..."
}
```

Ví dụ snapshot Upload:

```json
{
  "source": "UPLOAD",
  "fileName": "...",
  "storageKey": "...",
  "fileUrl": "..."
}
```

### `video_render.video_project_scenes`

Các trường prompt/output liên quan:

- `scene_prompt`
- `image_prompt`
- `video_prompt`
- output URLs

Không thấy cột riêng lưu `keep-reference`; cờ này nằm ở job settings.

### Kết luận database

Reference data và cờ keep-reference hiện đã có nơi lưu trong flow project/job settings. Điểm thiếu không nằm ở database schema mà nằm ở việc Prompt Assistant chưa load và sử dụng context này khi generate JSON.

## 3. AI Prompt Flow

### UI entry point

File: `TodoX.Web/Components/Pages/RenderVideoJobs.razor`

Method: `GenerateAiPromptAsync`

### HTTP/API endpoint

File: `TodoX.Web/Services/PromptAssistant/PromptAssistantEndpoints.cs`

Methods liên quan:

- `BuildAgentInput`
- `HandleGenerateAsync`

### Service

File: `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`

Methods liên quan:

- `GeneratePromptAsync`
- `PersistAndReturnAsync`

### Provider client

File: `TodoX.Web/Services/PromptAssistant/ServicePrompt79AiClient.cs`

Method: `CompleteAsync`

### Persistence

File: `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantRepository.cs`

Methods liên quan:

- `SaveGenerationAsync`
- `SaveGenerationAndSetActiveAsync`

### Input hiện tại gửi vào Agent

Agent input hiện chỉ gồm:

- User input
- Duration
- Scene count
- Yêu cầu trả về final TodoX prompt JSON

### Dữ liệu chưa được truyền vào Agent

Prompt Assistant hiện chưa nhận các dữ liệu sau:

- `character_id`
- `selected_character_id`
- Reference image URL
- `uploaded_character_url`
- `character_snapshot_json`
- `use_reference_image_for_all_scenes`
- `normalized_prompt`
- Keep-reference flag

`VideoProjectId` hiện được dùng để link/persist generation, nhưng chưa được dùng để load reference context trước khi build Agent input.

### Kết luận

Flow hiện tại là:

```text
User input
  |
  v
BuildAgentInput
  |
  v
Gommo / 79AI Agent
  |
  v
Generated JSON
  |
  v
Persist generation
```

Reference context không đi vào bước `BuildAgentInput`, vì vậy Agent không biết project đang có Character/reference image nào hoặc cờ keep-reference đang bật.

## 4. Existing Injection Logic

### Image prompt enrichment

File: `TodoX.Web/Services/Render/SceneImagePromptBuilder.cs`

Method: `SceneImagePromptBuilder.Build(...)`

Logic hiện có thêm các chỉ dẫn downstream tương tự:

- Giữ identity của character nhất quán.
- Duy trì cùng một character qua các scene.

Logic này chạy sau khi Agent đã trả về JSON, trong nhánh image generation/render; đây không phải enrichment trước khi lưu Generated JSON của Prompt Assistant.

### Reference resolution

File: `TodoX.Web/Services/Render/RVideoSceneImageReferenceSelection.cs`

Methods:

- `Resolve(...)`
- `Resolve(RVideoJobSettingsDto settings)`

Logic xử lý:

- Uploaded reference snapshot.
- Library Character/master image.
- None mode.

### Shared reference/render handling

Các file liên quan:

- `RVideoCoreExecutionAdapter.cs`
- `SceneVideoRenderHandler.cs`
- `RVideoSceneVideoAutoChainService.cs`
- `SceneVideoWorkerHandler.cs`

### Video prompt guard

`RVideoReferenceOnlyPromptGuard` trong `SceneVideoWorkerHandler.cs` thêm chỉ dẫn bảo toàn reference ở giai đoạn video worker.

### Kết luận injection

Reference/identity instruction hiện có ở downstream render/image/video execution. Không có code path hiện tại đảm bảo chèn các instruction này vào `scene.image_prompt` và `scene.video_prompt` trước khi Prompt Assistant lưu JSON cuối cùng.

## 5. Current Generated JSON And Missing Logic

Không có code path hiện tại enrich Generated JSON bằng character/reference instruction trước khi persist.

Với project có Character reference và `keep-reference = true`:

- `scene.image_prompt`: không được bảo đảm có reference rule từ Prompt Assistant.
- `scene.video_prompt`: không được bảo đảm có reference rule từ Prompt Assistant.
- Chuỗi như `MAIN CHARACTER IDENTITY IS DEFINED BY THE ATTACHED REFERENCE IMAGE`: không được Prompt Assistant inject trực tiếp.
- Reference vẫn có thể được dùng ở downstream render, nên reference không hoàn toàn bị mất khỏi render pipeline.

### Kết luận phân loại

**B. Có reference data nhưng chưa đưa vào Prompt Assistant generation.**

Đồng thời cũng đúng với:

**D. Checkbox được lưu và được dùng cho render settings, nhưng chưa được dùng trong AI prompt generation.**

Flow thực tế:

```text
Reference UI/settings
        |
        |  chưa truyền vào Prompt Assistant
        X
Prompt Assistant Generate
        |
        v
Agent JSON
        |
        v
Persist JSON gốc từ Agent
        |
        v
Downstream image/video enrichment
```

Không có bằng chứng từ static code audit cho thấy `scene.image_prompt` hoặc `scene.video_prompt` đã chứa reference rule trong JSON được persist. Chưa thực hiện live database query hoặc chạy một project thực tế để kiểm tra dữ liệu runtime.

## 6. Recommended Minimal Fix

Đề xuất tối thiểu cho phase tiếp theo:

```text
AI Generated JSON
        |
        v
Load project reference/keep-reference settings
        |
        v
Inject reference instruction vào scene image/video prompts
        |
        v
Validate TodoX JSON
        |
        v
Persist final enriched JSON
```

Khuyến nghị:

1. Load reference context từ project/job settings bằng `VideoProjectId`.
2. Chỉ inject khi có Character/reference và `use_reference_image_for_all_scenes = true`.
3. Giữ nguyên provider payload và provider routing.
4. Giữ nguyên image/video render pipeline, worker, queue và schema.
5. Validate JSON sau enrichment rồi mới persist.
6. Bổ sung test cho:
   - Library Character + keep-reference.
   - Uploaded reference + keep-reference.
   - Skip character.
   - Keep-reference tắt.
   - JSON không bị leak URL/token ngoài contract hiện tại.

Không cần tạo bảng mới hoặc migration cho hướng sửa này.

## 7. Files Cần Sửa Trong Phase Tiếp Theo

Các file có khả năng cần thay đổi:

- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantService.cs`
- `TodoX.Web/Services/PromptAssistant/ServicePromptAssistantRepository.cs`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/VideoRender/RVideoJobSettingsRepository.cs`
- `TodoX.Web/Services/AiCharacters/AiCharacterRepository.cs`
- Test tương ứng trong `TodoX.Web.Tests`

Đây chỉ là danh sách đề xuất cho implementation phase tiếp theo. Trong lượt audit này không file code nào được sửa.

## Final Result

Phase audit: **FAIL / INCOMPLETE**

Lý do chính: reference data đã được lưu và downstream render đã có logic xử lý, nhưng Prompt Assistant generation chưa nhận reference context và chưa inject reference instruction vào JSON trước khi persist.

Protected areas đã audit và không thay đổi:

- Image generation pipeline
- Video generation pipeline
- Render queue
- Worker
- Provider routing
- Database schema
- Migration

Files changed trong lượt audit:

- `REFERENCE_IMAGE_AUDIT_REPORT.md` — chỉ là file báo cáo, không phải production code.

Commit/push:

- Không commit.
- Không push.
