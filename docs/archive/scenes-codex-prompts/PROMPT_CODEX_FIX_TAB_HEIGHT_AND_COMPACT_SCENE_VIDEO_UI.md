# PROMPT CODEX — SỬA CHIỀU CAO TAB VÀ THU GỌN GIAO DIỆN SCENE/VIDEO

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Đây là vòng hiệu chỉnh UI tiếp theo của Render Video Job. Hãy xử lý đúng hai vấn đề: vùng nội dung tab đang bị co quá thấp và giao diện Scene/Video đang quá lớn, khiến mỗi lần chỉ xem được rất ít dữ liệu.

## 1. Quy định bắt buộc

1. Đọc và tuân thủ `AGENTS.md` đúng scope trước khi sửa.
2. Kiểm tra `git status`, commit mới nhất; bảo toàn thay đổi ngoài phạm vi.
3. Đọc `RenderVideoJobs.razor` và `RenderVideoJobs.razor.css`.
4. Không migration, không SQL/database, không deploy production.
5. Không thay đổi provider, render workflow, billing, versioning hoặc dữ liệu đã lưu.
6. Giữ đúng 4 tab hiện tại.
7. Giữ scroll đã sửa nhưng phải sửa vùng scroll đang bị co chiều cao.
8. Test, build Release và publish artifact riêng sau khi code.

## 2. Lỗi ưu tiên — vùng nội dung tab bị co quá thấp

Ảnh thực tế cho thấy header tab chiếm một hàng bình thường nhưng vùng nội dung/scroll phía dưới chỉ còn một dải rất thấp. Đây là lỗi phân bổ chiều cao flex, không phải yêu cầu làm giao diện thấp đến mức không sử dụng được.

### 2.1. Kết quả mong muốn

- Popup vẫn full-screen.
- Header popup và hàng tab đứng yên.
- Nội dung tab sử dụng **toàn bộ phần chiều cao còn lại của viewport**.
- Scrollbar nằm trong vùng nội dung lớn đó.
- Không hard-code một `max-height` nhỏ cho tab body.
- Không dùng kết hợp `height:0` ở sai nhiều tầng khiến panel bị collapse.

### 2.2. Kiểm tra chuỗi layout thật

Kiểm tra computed style và kích thước của từng mắt xích:

```text
.render-project-dialog
→ .render-project-dialog-surface
→ .render-project-dialog-body
→ .render-project-tabs
→ MudTabs panels
→ active panel
→ scroll host của tab
```

Với viewport 1366×768, ghi lại `clientHeight` của từng phần tử. Tìm chính xác phần tử đầu tiên có chiều cao bị collapse.

Không đặt `height:0` máy móc cho cả body, panels và scroll host. Chỉ dùng `flex:1 1 0`, `min-height:0`, `height:100%` hoặc `height:0` ở đúng tầng đã được chứng minh bằng DOM.

Cấu trúc kỳ vọng tương đương:

```css
.render-project-dialog {
    position: fixed;
    inset: 0;
    height: 100dvh;
    overflow: hidden;
}

.render-project-dialog-surface {
    height: 100%;
    min-height: 0;
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
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.render-project-tabs {
    flex: 1 1 auto;
    min-height: 0;
    height: 100%;
}
```

Phải kiểm tra DOM MudBlazor thực tế để đặt flex/height đúng cho toolbar, panels và active panel. Nếu CSS isolation không chạm component con, dùng wrapper HTML thật hoặc `::deep .selector` đúng cú pháp đang hoạt động.

### 2.3. Tiêu chí chiều cao

Tại viewport 1366×768:

- Tab header không được chiếm chiều cao bất thường.
- Vùng nội dung active tab phải chiếm phần lớn chiều cao còn lại; không được chỉ cao vài chục pixel.
- Scroll host phải có `clientHeight` hợp lý, tối thiểu khoảng 500px nếu header của ứng dụng/popup cho phép.
- Không lấy con số 500px làm hard-code; đây là tiêu chí quan sát tại viewport 768px cao.
- Người dùng nhìn thấy được ít nhất vài hàng/form hoặc nhiều card scene/video trước khi phải cuộn.

## 3. Chế độ giao diện compact cho Scene/Hình ảnh

Mục tiêu là tăng mật độ thông tin, giảm chiều cao mỗi scene card nhưng vẫn dễ đọc và thao tác.

### 3.1. Kích thước card

- Giảm padding scene card còn khoảng 8–10px.
- Giảm gap dọc giữa các vùng còn khoảng 6–8px.
- Header scene gọn một hàng khi đủ rộng.
- Font scene title khoảng 13–14px, metadata/chip khoảng 11–12px.
- Chip status/aspect ratio dùng `Size.Small`, chiều cao compact.
- Menu ba chấm vẫn đủ vùng click và có tooltip/aria-label.

### 3.2. Khung ảnh nhỏ hơn

- Desktop giảm cột ảnh từ khoảng 220–280px xuống khoảng 150–190px tùy tỷ lệ.
- Không crop sai nội dung; giữ `object-fit:contain` hoặc quy tắc media hiện tại.
- Khung ảnh không được quyết định chiều cao card quá lớn.
- Ảnh dọc 9:16 cần có giới hạn chiều cao hợp lý, ví dụ khoảng 220–260px trên desktop compact, không chiếm gần toàn màn hình.
- Click ảnh vẫn có thể mở preview lớn nếu chức năng hiện có hỗ trợ.

### 3.3. Prompt và các field

- Prompt ảnh vẫn rộng toàn bộ cột phải.
- Giảm textarea từ 7 dòng xuống khoảng 4–5 dòng ở chế độ compact; textarea tự có scroll khi prompt dài.
- Purpose, Voice, Voice instruction tiếp tục nằm ngay dưới Prompt ảnh trong cột phải như yêu cầu trước.
- Giảm các field này xuống khoảng 1–2 dòng; không để mỗi field cao quá mức.
- Font trong textarea/input khoảng 12–13px, line-height khoảng 1.35–1.45; không giảm nhỏ đến mức khó đọc.
- Label/fieldset compact nhưng không bị cắt chữ.

### 3.4. Bố cục gợi ý

```css
.scene-workflow {
    grid-template-columns: minmax(150px, 190px) minmax(0, 1fr);
    gap: 10px;
}

.scene-details-column {
    gap: 8px;
}

.scene-voice-row {
    gap: 8px;
}
```

Không hard-code nguyên xi nếu làm hỏng responsive; phải kiểm tra thực tế.

## 4. Chế độ giao diện compact cho tab Video

Giữ grid responsive:

- Desktop: 3 video card/hàng.
- Tablet: 2 card/hàng.
- Mobile: 1 card/hàng.

Thu gọn từng video card:

- Padding khoảng 8–10px.
- Gap khoảng 6–8px.
- Scene title 13–14px; metadata 11–12px.
- Preview nhỏ gọn, không làm card cao bất thường.
- Có thể dùng aspect ratio của project cho preview hoặc một khung compact thống nhất, nhưng media phải `object-fit:contain` và không crop.
- Prompt video hiển thị khoảng 3–4 dòng, có scroll nội bộ khi dài.
- Button/chip dùng size small và wrap đúng.

### 4.1. Tạm ẩn Voice và Voice instruction khỏi tab Video

Trong tab Video, hiện tại chỉ cần hiển thị và chỉnh:

- Video preview/poster.
- Prompt video (`MotionPrompt`).
- Ratio, resolution, duration, status.
- Thao tác render/lịch sử/lưu cần thiết.

**Không hiển thị các field Voice và Voice instruction trong video card ở vòng này.**

Lưu ý:

- Chỉ ẩn khỏi UI tab Video, không xóa dữ liệu khỏi model/database.
- Không thay đổi binding hoặc dữ liệu Voice/VoiceInstruction trong tab Scene/Hình ảnh.
- Không set null hoặc ghi đè hai field khi lưu video scene.
- Nếu `SaveSceneAsync` được gọi từ Video, phải bảo toàn giá trị Voice/VoiceInstruction hiện có.

## 5. Responsive

- Desktop ưu tiên mật độ cao nhưng vẫn không chồng chữ.
- Tablet có thể chuyển field Purpose/Voice/VoiceInstruction thành 2 hàng.
- Mobile chuyển scene về một cột; không cố giữ ảnh trái/prompt phải.
- Không overflow ngang ở bất kỳ tab nào.
- Không để việc giảm kích thước làm vùng click nhỏ hơn tiêu chuẩn sử dụng hợp lý.

## 6. Browser QA bắt buộc

Kiểm tra tại 1920×1080, 1366×768, 1024×768 và 390×844.

### Chiều cao tab

Ghi số liệu của dialog, body, tabs, panels, active panel và scroll host:

```javascript
const selectors = [
  '.render-project-dialog',
  '.render-project-dialog-surface',
  '.render-project-dialog-body',
  '.render-project-tabs',
  '.render-tab-scroll'
];
selectors.map(selector => {
  const el = document.querySelector(selector);
  return {
    selector,
    clientHeight: el?.clientHeight,
    scrollHeight: el?.scrollHeight,
    overflowY: el ? getComputedStyle(el).overflowY : null
  };
});
```

Phải chứng minh active tab body không còn bị co thành một dải thấp.

### Mật độ Scene

- Ở 1366×768 phải nhìn được nhiều nội dung hơn rõ rệt so với giao diện cũ.
- Card không còn bị ảnh dọc và textarea 7 dòng kéo quá cao.
- Prompt và ba field bên dưới vẫn dùng được.
- Scroll đến scene cuối vẫn hoạt động.

### Mật độ Video

- Desktop có 3 card/hàng.
- Nhìn được ít nhất một hàng 3 card đầy đủ hoặc phần lớn card trong viewport hợp lý.
- Video card không hiển thị Voice và Voice instruction.
- Prompt video vẫn chỉnh được.
- Cuộn đến card cuối vẫn hoạt động.

Chụp screenshot cả bốn tab, đặc biệt Scene và Video ở 1366×768.

## 7. Test hồi quy

Bổ sung test phù hợp:

1. Active tab body không dùng style/class làm collapse chiều cao.
2. Cả bốn tab vẫn có scroll owner đúng.
3. Scene card dùng layout compact và ảnh có giới hạn kích thước.
4. Purpose/Voice/VoiceInstruction vẫn nằm dưới Prompt ảnh.
5. Video grid vẫn 3/2/1.
6. Video card không render input Voice/VoiceInstruction.
7. Dữ liệu Voice/VoiceInstruction không bị xóa khi lưu từ tab Video.
8. Dirty state và prompt không mất khi chuyển tab/reload.

Test source/CSS không thay thế browser QA.

## 8. Build và publish

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-compact-ui
```

Không deploy production.

## 9. Báo cáo cuối

Báo cáo:

1. `AGENTS.md` đã đọc.
2. Root cause khiến active tab bị co chiều cao.
3. Bảng chiều cao DOM trước/sau tại 1366×768.
4. Các thông số compact đã áp dụng cho scene/video.
5. Screenshot Scene và Video sau sửa.
6. Xác nhận tab Video đã ẩn Voice/VoiceInstruction nhưng dữ liệu vẫn được bảo toàn.
7. File đã sửa.
8. Kết quả test/build/publish và artifact.
9. Xác nhận không migration, không SQL, không deploy.

Không báo hoàn thành nếu vùng active tab vẫn thấp bất thường, nếu Scene/Video vẫn quá lớn như cũ, hoặc nếu việc ẩn Voice làm mất dữ liệu.
