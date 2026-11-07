# PROMPT CODEX — SCROLL TẤT CẢ TAB VÀ BỐ CỤC SCENE GỌN

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Hãy cập nhật editor full-screen của Render Video Job theo phạm vi hẹp dưới đây. Không thay đổi nghiệp vụ render, provider, billing, versioning hoặc database.

## 1. Quy tắc bắt buộc

1. Đọc và tuân thủ toàn bộ `AGENTS.md` đúng scope.
2. Kiểm tra `git status`, commit mới nhất; không ghi đè thay đổi ngoài phạm vi.
3. Đọc `RenderVideoJobs.razor` và `RenderVideoJobs.razor.css` trước khi sửa.
4. Giữ đúng 4 tab: Thông tin, Scene/Hình ảnh, Video, Kết quả.
5. Không migration, không SQL/database, không deploy production.
6. Tab Scene hiện đã scroll được: không làm hỏng cơ chế này.
7. Sau khi sửa phải test, build Release và publish artifact riêng.

## 2. Scroll cho cả bốn tab

Hiện Scene/Hình ảnh đã cuộn được nhưng Thông tin, Video và Kết quả chưa cuộn đầy đủ. Hãy áp dụng cùng chuỗi chiều cao/flex đã sửa thành công cho Scene.

Kiến trúc bắt buộc:

```text
Popup full-screen
├── Header cố định
├── Tab header cố định
└── Active tab body
    └── Scroll host riêng của tab
```

Tạo class dùng chung tương đương:

```css
.render-tab-scroll {
    flex: 1 1 0;
    height: 0;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    overscroll-behavior: contain;
    scrollbar-gutter: stable;
    -webkit-overflow-scrolling: touch;
    padding: 12px 10px 32px 0;
}
```

Mọi ancestor từ popup surface → body → MudTabs → panels → active panel → scroll host phải có chuỗi `flex`, `height` và `min-height:0` phù hợp. Không chỉ thêm `overflow:auto` vào phần tử có chiều cao `auto`.

Nếu phải xuyên qua MudTabs/MudTabPanel, dùng wrapper HTML thật hoặc cú pháp CSS isolation đang hoạt động trong bản sửa Scene. Không dùng lại selector dạng `::deep(...)` nếu pipeline không hỗ trợ.

### Tab Thông tin

Bọc toàn bộ nội dung tab trong một scroll host: nhân vật, prompt video, kiểm tra cấu trúc, ratio, resolution, summary và log. Phải cuộn đến phần cuối cùng ở desktop thấp và mobile.

### Tab Scene/Hình ảnh

- Toolbar đứng yên.
- `.scene-list-scroll` tiếp tục là scroll owner duy nhất.
- Không bọc thêm một `overflow-y:auto` bên ngoài gây hai scrollbar cạnh tranh.
- Sau thay đổi chung của tabs phải kiểm tra lại vẫn cuộn đến scene cuối.

### Tab Video

- Toolbar/thống kê có thể đứng yên.
- Grid video 3/2/1 cột nằm trong vùng cuộn riêng.
- Cuộn đến card cuối và nội dung lịch sử video khi mở.
- Không làm mất grid desktop 3 card, tablet 2, mobile 1.
- Không overflow ngang.

### Tab Kết quả

Final video, thao tác, log, lịch sử task và final versions phải nằm trong scroll host. Cuộn được đến log/task cuối; video preview không được làm popup cao hơn viewport.

Không tạo nhiều scroll owner dọc cạnh tranh trong một tab. Dashboard phía sau popup không được cuộn thay nội dung popup.

## 3. Đưa Purpose, Voice, Voice instruction xuống dưới Prompt ảnh

Hiện ba field này nằm full-width bên dưới cả khung ảnh và prompt. Hãy chuyển chúng vào cột bên phải, ngay dưới Prompt ảnh.

Bố cục desktop:

```text
┌──────────────┬──────────────────────────────────────────────┐
│              │ Prompt ảnh — rộng toàn bộ cột phải          │
│  Khung ảnh   ├────────────┬────────────────┬────────────────┤
│              │ Purpose    │ Voice          │ Voice instruction│
└──────────────┴────────────┴────────────────┴────────────────┘
```

Markup gợi ý:

```razor
<div class="scene-workflow">
    <div class="scene-image-column">
        ...khung ảnh...
    </div>
    <div class="scene-details-column">
        <div class="scene-prompt-panel">...Prompt ảnh...</div>
        <div class="scene-voice-row">
            ...Purpose...
            ...Voice...
            ...Voice instruction...
        </div>
    </div>
</div>
```

CSS gợi ý:

```css
.scene-workflow {
    display: grid;
    grid-template-columns: minmax(220px, 280px) minmax(0, 1fr);
    gap: 12px;
    align-items: stretch;
}

.scene-details-column {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.scene-prompt-panel {
    min-width: 0;
    width: 100%;
}

.scene-voice-row {
    display: grid;
    grid-template-columns: minmax(120px, .75fr) minmax(220px, 1.5fr) minmax(220px, 1.5fr);
    gap: 12px;
}
```

Có thể hiệu chỉnh tỷ lệ theo giao diện thật, nhưng:

- Khung ảnh chỉ nằm ở cột trái.
- Prompt ảnh chiếm toàn bộ chiều rộng cột phải.
- Purpose, Voice, Voice instruction nằm ngay dưới prompt, không kéo qua dưới ảnh.
- Không còn `.scene-voice-row` bên ngoài `.scene-workflow`.

### Responsive

- Desktop: ảnh trái, chi tiết phải; ba field cùng một hàng nếu đủ rộng.
- Tablet: cho ba field wrap/grid phù hợp, không tràn ngang.
- Mobile theo thứ tự: Khung ảnh → Prompt ảnh → Purpose → Voice → Voice instruction.
- Không crop ảnh ngoài quy tắc hiện tại.

### Giữ nguyên binding

Chỉ di chuyển markup/layout; không đổi binding và state:

- `draft.ImagePrompt`
- `draft.Purpose`
- `draft.Voice`
- `draft.VoiceInstruction`
- các `ValueChanged`, `Immediate`, dirty state và `SaveSceneAsync`.

Polling/reload/chuyển tab không được làm mất nội dung đang sửa.

## 4. Browser QA bắt buộc

Không báo hoàn thành chỉ vì thấy scrollbar. Dùng project có ít nhất 7 scene và nội dung/log đủ dài. Với scroll host của từng tab, đo:

```javascript
const el = document.querySelector('SELECTOR_SCROLL_HOST');
({
  clientHeight: el?.clientHeight,
  scrollHeight: el?.scrollHeight,
  overflowY: el ? getComputedStyle(el).overflowY : null,
  before: el?.scrollTop
});
el.scrollTop = el.scrollHeight;
```

Điều kiện pass:

- `clientHeight > 0`.
- Khi nội dung dài, `scrollHeight > clientHeight`.
- `overflowY` là `auto` hoặc `scroll`.
- Sau khi cuộn, `scrollTop > 0`.
- Wheel/touchpad cuộn được và nhìn thấy phần tử cuối.
- Header popup và hàng tab vẫn đứng yên.

Kiểm tra đủ:

1. Thông tin: tới cuối summary/log.
2. Scene/Hình ảnh: tới scene cuối.
3. Video: tới video card cuối.
4. Kết quả: tới log/task cuối.

Viewport: 1920×1080, 1366×768, 1024×768 và 390×844. Chụp bằng chứng ở đầu và cuối từng tab.

## 5. Test hồi quy

Bổ sung test phù hợp:

1. Editor vẫn đúng 4 tab.
2. Thông tin, Video, Kết quả có scroll host.
3. Scene chỉ có một scroll owner cho danh sách.
4. Video giữ grid responsive 3/2/1.
5. Purpose/Voice/Voice instruction nằm trong cột chi tiết dưới prompt ảnh.
6. Binding và dirty state không thay đổi.
7. Chuyển tab không mất draft.
8. Scroll vẫn chạy sau reload và sau render success/failure.

Test source/CSS không thay thế browser QA.

## 6. Build và publish

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-all-tabs-scroll-compact-scene
```

Không deploy production.

## 7. Báo cáo cuối

Phải báo cáo:

1. `AGENTS.md` đã đọc.
2. Scroll owner thực tế của từng tab.
3. Bảng `clientHeight`, `scrollHeight`, `scrollTop` của cả 4 tab ở 1366×768.
4. Screenshot cuộn đến cuối từng tab.
5. Screenshot scene card mới đúng bố cục.
6. File đã sửa.
7. Kết quả test/build/publish và artifact.
8. Xác nhận không migration, không SQL, không deploy.

Không báo hoàn thành nếu Thông tin, Video hoặc Kết quả vẫn không cuộn bằng wheel hoặc không tới được nội dung cuối.
