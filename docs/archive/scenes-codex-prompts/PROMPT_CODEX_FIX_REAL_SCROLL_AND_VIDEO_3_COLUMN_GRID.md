# PROMPT GỬI CODEX — SỬA SCROLL THẬT VÀ VIDEO GRID 3 CỘT

Repository: `nguyenduc216/todoX-Dashboard-SaaS`

Commit cần bắt đầu kiểm tra: `d5587cea62b3a3ee8e47bbb5c7f1d0fb6eddce31` hoặc commit mới hơn trên nhánh hiện tại.

## Mục tiêu duy nhất của vòng sửa này

1. Làm cho danh sách ở tab **Scene / Hình ảnh** cuộn dọc được thật sự tới scene cuối trong popup full-screen.
2. Làm tab **Video** hiển thị dạng grid: desktop một hàng 3 scene/video card, tablet 2 card, mobile 1 card.

Không mở rộng phạm vi sang nghiệp vụ khác khi hai tiêu chí này chưa được xác minh bằng browser.

## 1. Quy định bắt buộc

- Đọc và tuân thủ toàn bộ `AGENTS.md` theo đúng scope trước khi code.
- Kiểm tra `git status` và không ghi đè thay đổi ngoài phạm vi.
- Không tạo migration, không chạy SQL/database, không deploy production.
- Không thay đổi nghiệp vụ provider, render, version history, billing hoặc selected version.
- Giữ đúng 4 tab hiện tại: Thông tin, Scene/Hình ảnh, Video, Kết quả.
- Chạy test, build Release và publish artifact riêng sau khi sửa.

## 2. Kết quả khảo sát commit hiện tại

Phải kiểm tra lại các nhận định này trên working tree trước khi sửa:

### 2.1 Scroll hiện tại chỉ đúng trên lý thuyết

Trong `RenderVideoJobs.razor.css` đã có:

```css
.scene-list-scroll {
    flex: 1 1 auto;
    min-height: 0;
    overflow-y: auto;
}
```

Nhưng `overflow-y:auto` chỉ tạo scrollbar khi phần tử có **chiều cao bị giới hạn thật sự**. Chuỗi cha hiện tại đi qua:

```text
.render-project-dialog
→ MudPaper.render-project-dialog-surface
→ .render-project-dialog-body
→ MudTabs.render-project-tabs
→ MudTabs panels/panel
→ .scene-image-tab
→ .scene-list-scroll
```

Chỉ cần một mắt xích không nhận `height/flex/min-height:0`, `.scene-list-scroll` sẽ nở theo nội dung và không thể cuộn.

Commit hiện tại còn dùng selector dạng:

```css
::deep(.render-project-tabs)
::deep(.render-project-tabs .mud-tabs-panels)
```

Đây không phải dạng nên tiếp tục tin tưởng trong Blazor CSS isolation. Phải kiểm tra CSS đã compile và DOM thật. Với CSS isolation, ưu tiên wrapper HTML thật; nếu buộc xuyên child component thì dùng cú pháp `::deep .selector` dưới một ancestor scoped rõ ràng, không dùng cú pháp giống Sass `::deep(...)` nếu pipeline hiện tại không hỗ trợ.

`Class` đặt trực tiếp lên `MudPaper`, `MudTabs` hoặc component con có thể nằm ngoài phạm vi selector isolation. Không được kết luận CSS hoạt động chỉ vì class xuất hiện trong source `.razor`.

### 2.2 Video grid chưa được định nghĩa

Markup tab Video đã có:

```html
<div class="scene-video-grid">
```

nhưng `RenderVideoJobs.razor.css` tại commit khảo sát chưa có rule `.scene-video-grid`. Vì vậy các `MudPaper` con vẫn xếp một card chiếm hết một hàng.

## 3. Sửa scroll bằng wrapper HTML thật

Không được sửa kiểu thử thêm một dòng `overflow:auto`. Hãy sửa toàn bộ chuỗi chiều cao có kiểm soát.

### 3.1 Cấu trúc đề xuất

Trong `RenderVideoJobs.razor`, ưu tiên thay phần surface quan trọng bằng wrapper HTML thật do chính component render:

```razor
<div class="render-project-dialog" role="dialog" aria-modal="true">
    <div class="render-project-dialog-surface">
        <header class="render-project-dialog-header">...</header>
        <main class="render-project-dialog-body">
            <MudTabs Class="render-project-tabs" ...>
                ...
            </MudTabs>
        </main>
    </div>
</div>
```

Có thể giữ MudPaper bên trong nếu cần style, nhưng **layout height/flex/overflow không được phụ thuộc hoàn toàn vào root DOM do MudPaper sinh ra**.

### 3.2 Chuỗi CSS bắt buộc

Thiết lập chuỗi có chiều cao xác định:

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
    flex: 1 1 0;
    height: 0;
    min-height: 0;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}
```

`height:0` trên flex child body là biện pháp rõ ràng để buộc body dùng phần chiều cao còn lại thay vì nở theo nội dung. Có thể dùng giải pháp tương đương nếu chứng minh được bằng computed style.

MudTabs phải thực sự chiếm phần còn lại. Kiểm tra DOM của phiên bản MudBlazor đang dùng rồi viết selector đúng, ví dụ theo DOM thực tế:

```css
.render-project-dialog-body ::deep .render-project-tabs {
    flex: 1 1 0;
    height: 100%;
    min-height: 0;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.render-project-dialog-body ::deep .mud-tabs-toolbar {
    flex: 0 0 auto;
}

.render-project-dialog-body ::deep .mud-tabs-panels {
    flex: 1 1 0;
    height: 0;
    min-height: 0;
    overflow: hidden;
}

.render-project-dialog-body ::deep .render-tab-panel {
    height: 100%;
    min-height: 0;
    overflow: hidden;
}
```

Các tên class MudBlazor ở trên chỉ là hướng dẫn; phải mở DevTools/DOM hoặc kiểm tra version library để dùng đúng class thực tế. Không đoán selector.

Riêng tab Scene:

```css
.scene-image-tab {
    height: 100%;
    min-height: 0;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.scene-image-toolbar {
    flex: 0 0 auto;
}

.scene-list-scroll {
    flex: 1 1 0;
    height: 0;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    overscroll-behavior: contain;
    scrollbar-gutter: stable;
    -webkit-overflow-scrolling: touch;
    padding-right: 10px;
    padding-bottom: 32px;
}
```

Nếu MudTabPanel không cho panel content nhận chiều cao ổn định, hãy đặt một wrapper `.render-tab-scroll-host` trực tiếp bên trong panel và buộc wrapper đó nhận chiều cao từ tabs panel. Không để danh sách scene quyết định chiều cao popup.

### 3.3 Không tạo hai scroll cạnh tranh

- Tab Scene/Hình ảnh: chỉ `.scene-list-scroll` cuộn; toolbar đứng yên.
- Không để cả `.render-project-dialog-body` và `.scene-list-scroll` cùng `overflow-y:auto` ở tab này.
- Trang dashboard phía sau popup phải khóa scroll trong thời gian popup mở và được phục hồi khi đóng/dispose.
- Menu ba chấm và dialog lịch sử không được bị cắt bởi `overflow:hidden`; dùng MudPopover portal đúng cơ chế hiện tại.

## 4. Bắt buộc chứng minh scroll bằng browser/DOM

Không được báo hoàn thành chỉ từ việc nhìn CSS hoặc build pass.

Mở một project có tối thiểu 7 scene ở viewport 1366×768 và chạy kiểm tra tương đương:

```javascript
const el = document.querySelector('.scene-list-scroll');
({
  clientHeight: el?.clientHeight,
  scrollHeight: el?.scrollHeight,
  overflowY: el ? getComputedStyle(el).overflowY : null,
  before: el?.scrollTop
});
```

Điều kiện pass:

- `clientHeight > 0`.
- `scrollHeight > clientHeight`.
- computed `overflowY` là `auto` hoặc `scroll`.
- Sau khi đặt `el.scrollTop = el.scrollHeight`, `scrollTop > 0`.
- Scene cuối xuất hiện trong viewport của `.scene-list-scroll`.
- Mouse wheel/touchpad trên vùng scene làm thay đổi `scrollTop`.
- Header popup, hàng tab và toolbar vẫn đứng yên.

Kiểm tra thêm ở 1920×1080, 1024×768 và 390×844. Chụp ảnh scene đầu và scene cuối làm bằng chứng.

Nếu bất kỳ điều kiện nào không đạt, tiếp tục tìm ancestor có `min-height:auto`, chiều cao `auto`, hoặc selector isolation không áp dụng. Không được kết thúc nhiệm vụ.

## 5. Tab Video phải là grid 3 card mỗi hàng

Người dùng đã xác nhận yêu cầu cuối cùng là **desktop một hàng 3 scene**, tức mỗi card khoảng một phần ba chiều rộng, không phải 40% cố định.

Thêm CSS rõ ràng:

```css
.scene-video-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 16px;
    align-items: start;
    width: 100%;
}

.scene-video-grid > * {
    min-width: 0;
    width: 100%;
}

@media (max-width: 1200px) {
    .scene-video-grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
    }
}

@media (max-width: 700px) {
    .scene-video-grid {
        grid-template-columns: minmax(0, 1fr);
    }
}
```

Có thể hiệu chỉnh breakpoint theo layout thực tế, nhưng kết quả bắt buộc:

- Desktop rộng: 3 card/hàng.
- Tablet: 2 card/hàng.
- Mobile: 1 card/hàng.
- Không có overflow ngang.
- Card trong cùng hàng không cần ép chiều cao bằng nhau nếu prompt/history đang mở; `align-items:start` để card không bị kéo dài vô ích.

### 5.1 Nội dung video card

- Preview video/ảnh poster dùng khung vuông như yêu cầu trước, `aspect-ratio:1/1` và media `object-fit:contain`.
- Prompt video không làm card vỡ grid; textarea width 100%, `min-width:0`.
- Nội dung dài phải wrap, không kéo cột rộng ra.
- Các chip ratio/resolution/duration được wrap.
- Action nhỏ gọn và không làm overflow.
- Lịch sử video mở trong card phải nằm trong đúng cột, hoặc mở dialog riêng nếu nội dung quá dài.

## 6. Test hồi quy cần bổ sung

Thêm test phù hợp để khóa cấu trúc, nhưng test source/CSS không thay thế browser QA:

1. Editor vẫn có đúng 4 tab.
2. Tab Scene có `.scene-list-scroll` bên dưới toolbar.
3. Không có `overflow-y:auto` cạnh tranh ở body cho tab Scene.
4. CSS có `.scene-video-grid` với 3/2/1 cột theo breakpoint.
5. Video card có `min-width:0` và preview không crop.
6. Chuyển tab không mất draft prompt.
7. Scroll vẫn hoạt động sau render ảnh success/failure và sau reload.

## 7. Build, publish và báo cáo

Chạy tối thiểu:

```powershell
dotnet test
dotnet build -c Release
dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release -o .\artifacts\publish\render-scroll-video-grid
```

Không deploy production.

Báo cáo cuối phải có:

1. `AGENTS.md` đã đọc.
2. Root cause chính xác khiến scroll cũ không chạy.
3. DOM element nào là scroll owner cuối cùng.
4. Bảng số liệu `clientHeight`, `scrollHeight`, `scrollTop` trước/sau tại từng viewport.
5. Screenshot đã cuộn đến scene cuối.
6. Screenshot tab Video có 3 card/hàng desktop, 2 tablet, 1 mobile.
7. Danh sách file sửa.
8. Kết quả test/build/publish và đường dẫn artifact.
9. Xác nhận không migration, không SQL, không deploy.

Không được báo “đã hoàn thành” nếu chỉ thấy thanh scrollbar nhưng wheel không chạy, nếu không đến được scene cuối, hoặc nếu desktop tab Video vẫn chỉ có một card mỗi hàng.
