# PROMPT CODEX — HỢP NHẤT RENDER JOBS VÀ SỬA POPUP KHÔNG CUỘN ĐƯỢC

Đọc và tuân thủ toàn bộ `AGENTS.md` trước khi thực hiện.

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Phạm vi chính:

- `/render-jobs`
- `/render-job`
- `TodoX.Web/Components/Pages/RenderJobs.razor`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `VideoRenderRepository`
- `CatalogRepository` và `RenderOrderView`
- Các component/dialog quản lý video project

## 1. Mục tiêu cuối cùng

Hệ thống chỉ có **một trang danh sách quản lý render**:

- `/render-jobs` là trang dashboard và danh sách chính thức.
- Không còn danh sách project thứ hai trong `/render-job`.
- Tất cả video project tạo từ Render Video Job phải xuất hiện trên dashboard `/render-jobs`.
- Tạo mới hoặc mở project hiển thị editor full-screen ngay trên dashboard.
- Popup editor có header/footer cố định và vùng body cuộn độc lập được đến scene cuối.
- Không làm mất chức năng scene, prompt, image/video history, selected version, billing, provider và storage.

Không được báo hoàn thành nếu vẫn tồn tại hai danh sách hoặc popup không cuộn tới cuối.

## 2. Quy định bắt buộc

- Đọc `AGENTS.md`, kiểm tra `git status` trước khi sửa.
- Không ghi đè thay đổi ngoài phạm vi.
- Không tạo migration và không tự chạy SQL/database.
- Nếu thiếu schema, chỉ tạo SQL idempotent có preflight/verify/rollback để chủ hệ thống tự chạy.
- Không hard-code provider, model, secret, user/customer/tenant hoặc cost/point.
- Không bật video mock trong production.
- Không làm hỏng YEScale image, billing, storage và versioning.
- Không deploy production; chỉ publish ra artifact riêng.
- Không commit `bin`, `obj`, publish, log hoặc ZIP artifact không cần thiết.

## 3. Vấn đề hiện tại đã xác định

### Trang `/render-jobs`

`RenderJobs.razor` có dashboard và `Danh sách đơn render`, nhưng đang đọc:

```csharp
Catalog.GetRenderStatsAsync(...)
Catalog.GetRenderOrdersAsync(...)
```

Nguồn này không trả video projects đang được tạo tại `/render-job`, vì vậy dashboard có thể toàn số 0 và danh sách trống.

### Trang `/render-job`

`RenderVideoJobs.razor` lại có `Danh sách dự án render` và đọc:

```csharp
VideoRepo.ListProjectsAsync(...)
```

Kết quả là có hai trang danh sách và hai nguồn dữ liệu khác nhau.

### Popup chi tiết

Editor full-screen hiện không cuộn xuống được, có khả năng do `position:fixed`, `height:100%`, `overflow:hidden`, flex child thiếu `min-height:0`, hoặc CSS isolation/MudDialog content không nhận đúng style.

## 4. Kiến trúc route mới

Route quản lý chính thức:

```text
/render-jobs
```

Deep link:

```text
/render-jobs?projectId=39
```

- Có `projectId`: tự mở editor full-screen của project đó.
- Đóng editor: xóa query `projectId` nhưng không reload mất dashboard/filter/scroll.

Route cũ:

```text
/render-job
```

- Không còn hiển thị danh sách riêng.
- `/render-job` redirect về `/render-jobs`.
- `/render-job?projectId=39` redirect về `/render-jobs?projectId=39`.
- Không tạo vòng lặp redirect.
- Link/bookmark cũ vẫn hoạt động.

## 5. Một nguồn danh sách project duy nhất

Trang `/render-jobs` dùng video project làm nguồn chính:

```csharp
VideoRepo.ListProjectsAsync(currentUser, ...)
```

Tạo DTO/query quản lý rõ ràng, ví dụ:

```csharp
RenderProjectManagementItemDto
{
    ProjectId
    Title
    CustomerId
    CustomerName
    UserId
    CharacterId
    CharacterName
    AspectRatio
    SceneCount
    ImageReadyCount
    VideoReadyCount
    Status
    EstimatedPoints
    ChargedPoints
    ThumbnailUrl
    CreatedAt
    UpdatedAt
    LastError
}
```

Nếu vẫn còn render order cũ ngoài video project:

- Khảo sát nơi chúng được tạo/tiêu thụ; không xóa âm thầm.
- Có thể tạo unified management view có `SourceType = video_project | legacy_render_order`.
- Một video project chỉ xuất hiện đúng một lần.
- Không biến background execution jobs thành các dòng project riêng.

Phân biệt:

1. Project/job nghiệp vụ: một video người dùng đang làm — xuất hiện trên dashboard.
2. Execution job: batch ảnh, render scene, merge, retry — chỉ xuất hiện trong chi tiết project tại `Lịch sử tác vụ xử lý`.

## 6. Dashboard `/render-jobs`

Các số liệu:

- Tổng dự án.
- Đang xử lý.
- Hoàn tất.
- Trong hàng đợi/chờ.
- Lỗi.

Mapping project status được chuẩn hóa, ví dụ:

- Đang xử lý: `rendering`, `merging`.
- Chờ: `draft`, `scene_ready`, `ready_to_merge`.
- Hoàn tất: `completed`.
- Lỗi: `failed`.

Không nhân số project theo số scene hoặc execution job con.

Dashboard và danh sách dùng cùng scope/filter:

- tenant;
- user;
- customer;
- root/admin/manage permission;
- search;
- status.

## 7. Danh sách quản lý duy nhất

Danh sách `/render-jobs` hiển thị tối thiểu:

- Mã project.
- Thumbnail.
- Tên project.
- Khách hàng.
- Character.
- Tỷ lệ `16:9` hoặc `9:16`.
- Số scene.
- Tiến độ ảnh.
- Tiến độ video.
- Điểm ước tính/đã dùng nếu có.
- Trạng thái.
- Ngày tạo/cập nhật.
- Hành động Mở/Tiếp tục.

Bổ sung search/filter/paging server-side phù hợp.

Xóa nút `Mở job video` vì đây đã là trang quản lý video job. Chỉ giữ `Tạo mới`, `Refresh` và filter cần thiết.

## 8. Loại bỏ danh sách trùng trong `/render-job`

Xóa khỏi editor:

- Tab/khu `Danh sách dự án render`.
- `_projects` và `LoadProjectListAsync()` nếu chỉ phục vụ danh sách trùng.
- Các nút điều hướng về danh sách thứ hai.

Editor chỉ còn:

1. Thông tin.
2. Scene.
3. Kết quả.
4. Lịch sử tác vụ xử lý nếu phù hợp.

Phân trách nhiệm:

- `RenderJobs.razor`: dashboard/list/filter.
- Project editor/dialog: tạo/chỉnh một project.
- Repository/service: dữ liệu dùng chung.

## 9. Tách editor thành component

Không nhúng nguyên một page route khổng lồ vào dialog. Nên tách:

```text
Components/RenderJobs/RenderVideoProjectEditor.razor
Components/RenderJobs/RenderVideoProjectInfoPanel.razor
Components/RenderJobs/RenderVideoProjectScenesPanel.razor
Components/RenderJobs/RenderVideoProjectResultPanel.razor
Components/RenderJobs/SceneEditorCard.razor
Components/Dialogs/RenderVideoProjectDialog.razor
```

Editor nhận tham số/callback rõ ràng:

```csharp
[Parameter] public long? ProjectId { get; set; }
[Parameter] public EventCallback<long> ProjectCreated { get; set; }
[Parameter] public EventCallback ProjectSaved { get; set; }
[Parameter] public EventCallback Closed { get; set; }
```

Không sao chép business logic giữa route và dialog.

## 10. Popup editor full-screen

Khi Mở/Tạo mới:

- Dùng `MudDialog` full-screen nếu phiên bản MudBlazor hỗ trợ.
- Nếu cần custom class, vẫn phải giữ cấu trúc flex chính xác.

```text
Header cố định: tên | ID | aspect ratio | close
Body cuộn độc lập: Thông tin / Scene / Kết quả
Footer cố định: Lưu project | Đóng
```

CSS bắt buộc tương đương:

```css
.render-project-dialog-shell {
    width: 100%;
    height: 100dvh;
    max-height: 100dvh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.render-project-dialog-header {
    flex: 0 0 auto;
}

.render-project-dialog-body {
    flex: 1 1 auto;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    overscroll-behavior: contain;
    -webkit-overflow-scrolling: touch;
    padding-bottom: 24px;
}

.render-project-dialog-footer {
    flex: 0 0 auto;
}
```

Bắt buộc:

- `min-height:0` trên flex body.
- Không để shell và body cùng tranh scroll.
- Không khóa overflow toàn dialog mà thiếu child scroll.
- Header/footer không che nội dung.
- Trang phía sau không scroll khi dialog mở.
- Đóng dialog phục hồi scroll dashboard.
- Nếu CSS isolation không áp dụng lên MudBlazor, dùng wrapper HTML thật hoặc `::deep` có kiểm chứng.

## 11. Tái hiện và sửa lỗi không scroll

Trước khi sửa, kiểm tra computed layout của:

- `.render-project-dialog`
- `.render-project-dialog-surface`
- `.mud-dialog`
- `.mud-dialog-content`
- các parent MudStack/MudPaper
- body scroll lock
- `height`, `max-height`, `min-height`, `overflow`

Sau khi sửa phải thao tác được:

- Wheel/mouse.
- Thanh scrollbar.
- PageDown, Home, End.
- Touch scroll mobile.
- Cuộn tới scene cuối và phần Kết quả.
- Mở lịch sử ảnh/video mà không làm mất khả năng cuộn.
- Footer không che `Lưu scene` hoặc nội dung cuối.

Không sửa bằng cách bỏ full-screen hoặc để trang nền cuộn thay body dialog.

## 12. Tạo project mới

Nút `Tạo mới` trên `/render-jobs` mở cùng editor full-screen với `ProjectId=null`.

- Không điều hướng đến danh sách khác.
- Không tạo DB record chỉ vì vừa mở dialog.
- Sau khi người dùng tạo project thành công: cập nhật `projectId` query, editor tiếp tục dùng project vừa tạo.
- Đóng editor: dashboard refresh và project mới xuất hiện.
- Không mở popup thứ hai.

## 13. Giữ nguyên chức năng scene

Không được làm mất thiết kế scene đã yêu cầu:

Desktop:

```text
Khung ảnh -> Prompt ảnh/thao tác ảnh -> Khung video -> Prompt video/thao tác video -> Lưu scene
```

- Khung ảnh/video vuông, `object-fit:contain`, không crop.
- Lịch sử và render lại ảnh nằm tại panel ảnh.
- Lịch sử và render lại video nằm tại panel video.
- Voice/voice instruction chỉnh được.
- Render video dùng đúng selected image version và motion prompt đang chỉnh.
- Lưu scene cập nhật prompts + selected versions atomically.
- Polling không ghi đè working draft dirty.

## 14. Polling và lifecycle

Dashboard:

- Một polling loop nhẹ hoặc refresh thủ công.
- Chỉ refresh stats/list.

Editor:

- Mở project: start đúng một polling loop.
- Đóng: cancel/dispose loop.
- Đổi project: dừng loop cũ trước.
- Không poll project sau khi dialog đóng.
- Không ghi đè prompt đang chỉnh.
- Không tạo nhiều request trùng nhau giữa dashboard và editor.

## 15. Quyền truy cập

Stats, list và open project phải dùng cùng quyền:

- tenant hiện tại;
- user/customer;
- admin/root/manage permissions.

Repository phải enforce quyền, không chỉ UI. Không để dashboard đếm project người dùng không mở được hoặc đoán `projectId` để xem project khác tenant/customer.

## 16. Test bắt buộc

Bổ sung test tối thiểu:

1. `/render-jobs` trả video projects.
2. Project mới xuất hiện trên dashboard.
3. Stats không nhân theo scene/job con.
4. Scope tenant/user/customer đúng.
5. Mở project từ dashboard.
6. `/render-jobs?projectId=...` mở đúng editor.
7. `/render-job` redirect đúng và không loop.
8. Không còn danh sách project thứ hai.
9. Tạo mới mở editor, không mở trang list khác.
10. Đóng editor refresh dashboard.
11. Editor chỉ có một polling loop.
12. Đóng editor dừng polling.
13. Dirty prompt không bị polling ghi đè.
14. Execution jobs chỉ hiện trong chi tiết.
15. Dialog cuộn được đến phần tử cuối.
16. Header/footer giữ nguyên khi body cuộn.
17. Footer không che nội dung.
18. Mobile touch scroll hoạt động.
19. Đóng dialog phục hồi vị trí scroll danh sách.

## 17. Kiểm tra browser bắt buộc

Chạy site và kiểm tra với project ít nhất 7 scene tại:

- 1920x1080
- 1366x768
- 1024x768
- 390x844

Đo body dialog:

```javascript
const body = document.querySelector('.render-project-dialog-body');
const canScroll = body.scrollHeight > body.clientHeight;
body.scrollTop = body.scrollHeight;
const reachedBottom = Math.abs(
  body.scrollHeight - body.clientHeight - body.scrollTop
) < 3;
```

`canScroll` và `reachedBottom` phải bằng `true`. `getComputedStyle(body).overflowY` phải là `auto` hoặc `scroll`.

Chụp screenshot:

1. Dashboard/list.
2. Popup ở đầu.
3. Popup ở giữa sau khi scroll.
4. Popup ở cuối.
5. Mobile popup ở cuối.

Không báo hoàn thành nếu không có bằng chứng scroll.

## 18. Build và publish

Chạy:

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-jobs-unified
```

Không deploy production và không sửa secrets/appsettings production.

## 19. Báo cáo cuối

Báo cáo rõ:

- Đã đọc AGENTS.md.
- Nguyên nhân có hai danh sách.
- Nguồn dữ liệu cũ và nguồn thống nhất mới.
- Route được giữ và redirect tương thích.
- Component đã tách.
- Cách tính dashboard stats.
- Cách phân biệt project với execution job.
- Nguyên nhân popup không scroll.
- CSS/layout scroll đã sửa.
- Kết quả browser/screenshot.
- Kết quả test/build/publish.
- Có cần SQL không.
- Blocker còn lại.

Không báo hoàn thành nếu:

- `/render-job` vẫn có danh sách thứ hai;
- `/render-jobs` trống khi có video projects;
- nút `Mở job video` còn điều hướng sang list thứ hai;
- popup không cuộn đến scene cuối;
- execution jobs bị hiển thị thành nhiều project;
- mất chức năng scene/image/video/versioning hiện có.
