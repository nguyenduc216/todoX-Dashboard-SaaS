# PROMPT GỬI CODEX — SỬA TRIỆT ĐỂ TAB SCENE/HÌNH ẢNH

Hãy làm việc trực tiếp trên repository `nguyenduc216/todoX-Dashboard-SaaS` và xử lý triệt để giao diện cùng lỗi trạng thái của tab **SCENE / HÌNH ẢNH** trong Render Video Job.

## 1. Quy tắc bắt buộc

1. Đọc toàn bộ `AGENTS.md` ở đúng scope trước khi sửa và tuân thủ tuyệt đối.
2. Kiểm tra `git status`, commit mới nhất và các thay đổi sẵn có. Không ghi đè thay đổi không thuộc phạm vi.
3. Không tạo migration, không tự chạy migration, không sửa database production và không deploy production.
4. Không làm mất kiến trúc 4 tab đã thống nhất:
   - THÔNG TIN
   - SCENE / HÌNH ẢNH
   - VIDEO
   - KẾT QUẢ
5. Giữ nguyên nghiệp vụ version image/video, selected version, prompt và billing hiện có, trừ khi cần sửa bug rõ ràng.
6. Trước khi code, phải đọc và đối chiếu tối thiểu:
   - `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
   - `TodoX.Web/Components/Pages/RenderVideoJobs.razor.css`
   - `TodoX.Web/Components/Pages/AvatarBuilder.razor`
   - `TodoX.Web/Components/Pages/AvatarBuilder.razor.css`
7. Không mô phỏng hiệu ứng Avatar Builder bằng phỏng đoán. Phải tái sử dụng đúng CSS/component/cơ chế flash đang có; nếu hợp lý, tách thành component/class dùng chung để tránh sao chép lệch hành vi.

## 2. Lỗi phải tái hiện trước khi sửa

Hãy chạy hệ thống hoặc dùng test phù hợp để xác nhận các lỗi sau:

- Popup chi tiết job và danh sách scene không cuộn được tới scene cuối.
- Cụm nút `Tạo ảnh tĩnh scene còn thiếu`, `Thêm scene`, `Tiếp tục tạo video`, `Refresh` nằm quá sát hàng tab.
- Các nút quá lớn, thiếu icon và chưa căn phải.
- Prompt ảnh chỉ chiếm một phần nhỏ, để dư khoảng trống lớn.
- Các nút `Lưu scene`, `Render/Render lại ảnh`, `Chỉnh sửa`, `Lịch sử ảnh` đang chiếm chỗ trong card.
- Hiệu ứng đang render ảnh chưa giống flash của Avatar Builder.
- Lỗi nghiêm trọng: sau khi render ảnh xong, màn hình bị đơ hoặc có lớp vô hình chặn click; không mở được menu, không chuyển tab, không xem thông tin và không bấm được nút.

Ghi lại nguyên nhân kỹ thuật của từng lỗi trước khi chỉnh. Không chỉ thay CSS nếu nguyên nhân nằm ở Blazor state, polling, lock hoặc overlay.

## 3. Sửa scroll của popup và danh sách scene

Thiết kế popup chi tiết job theo flex column đúng chuẩn:

- Popup/surface: `height: 100dvh`, `max-height: 100dvh`, `min-height: 0`, `overflow: hidden`.
- Header popup và hàng tab: `flex: 0 0 auto`.
- Vùng nội dung: `flex: 1 1 auto`, `min-height: 0`.
- Riêng tab Scene/Hình ảnh:
  - root của tab là flex column và có `min-height: 0`;
  - toolbar không cuộn theo danh sách;
  - danh sách scene là vùng cuộn thật với `flex: 1 1 auto`, `min-height: 0`, `overflow-y: auto`, `overscroll-behavior: contain`, `scrollbar-gutter: stable`;
  - có padding phải và padding dưới đủ để scene cuối không bị che.

Nếu CSS isolation không xuyên qua DOM do MudBlazor render child component, phải dùng wrapper HTML thật hoặc selector `::deep` có phạm vi rõ ràng. Không để thuộc tính scroll trên một element không có chiều cao bị giới hạn.

Không tạo nhiều vùng cuộn dọc cạnh tranh trong cùng tab. Các tab khác có thể dùng body scroll; tab Scene/Hình ảnh phải có scene-list scroll rõ ràng.

Tiêu chí bắt buộc:

- Với ít nhất 7 scene, người dùng kéo được đến toàn bộ scene cuối.
- Wheel, touchpad, kéo scrollbar và PageDown đều hoạt động.
- Popup vẫn giữ header/tab nhìn thấy được.
- Không làm trang nền phía sau popup cuộn thay cho popup.

## 4. Chỉnh toolbar của tab Scene/Hình ảnh

Tách toolbar khỏi hàng tab bằng khoảng cách trực quan khoảng 12–16px.

- Căn toàn bộ toolbar về góc phải.
- Dùng button compact/small, chiều cao đồng nhất.
- Thêm icon phù hợp:
  - Tạo ảnh tĩnh scene còn thiếu: icon Image/AddPhotoAlternate.
  - Thêm scene: icon Add.
  - Tiếp tục tạo video: icon Movie/ArrowForward.
  - Refresh: icon Refresh.
- Cho phép wrap hợp lý ở màn hình nhỏ nhưng không đè tab hoặc card.
- Giữ đúng primary/outline hierarchy; không biến tất cả thành nút lớn màu vàng.
- Có tooltip/aria-label cho icon và nút.

## 5. Bố cục card scene

Header của mỗi scene:

- Bên trái: số scene, tên, thời lượng.
- Bên phải: aspect ratio, trạng thái như `Image ready`, và nút ba chấm.
- Nút ba chấm phải là điểm truy cập duy nhất cho nhóm hành động phụ.

Vùng chính của scene:

- Dùng grid hai cột: khung ảnh có chiều rộng hợp lý/cố định và prompt ảnh là `minmax(0, 1fr)`.
- Prompt ảnh phải chiếm toàn bộ phần chiều ngang còn lại của card, không còn khoảng trắng lớn bên phải.
- Textarea prompt rộng 100%, đủ cao để sửa nội dung, có scroll nội bộ khi prompt dài.
- Không còn cột riêng chỉ để đặt nút `Lưu scene`.
- Ở mobile/tablet nhỏ, chuyển về một cột mà không tràn ngang.
- Hàng `Purpose`, `Voice`, `Voice instruction` giữ bố cục và dữ liệu hiện tại.

## 6. Gom hành động vào menu ba chấm

Chuyển các hành động sau vào menu ba chấm ở cuối header của scene:

1. Lưu scene.
2. Render ảnh hoặc Render lại ảnh tùy trạng thái.
3. Chỉnh sửa prompt ảnh — mở editor hoặc focus đúng textarea của scene.
4. Lịch sử ảnh.

Nếu đang có các thao tác đổi nhân vật/ảnh tham chiếu, di chuyển lên/xuống hoặc xóa scene thì bố trí trong cùng menu theo nhóm, có divider và xác nhận cho thao tác phá hủy.

Yêu cầu:

- Mỗi item có icon và nhãn tiếng Việt đúng UTF-8.
- Disable đúng item đang xung đột, không disable toàn menu hoặc toàn màn hình.
- Không hiển thị lặp lại các nút này bên ngoài card.
- Menu phải đóng đúng sau thao tác và mở lại được sau khi render thành công/thất bại.

## 7. Hiệu ứng flash khi render ảnh

Đọc đúng implementation trong Avatar Builder và áp dụng cùng ngôn ngữ chuyển động cho ảnh scene:

- Chỉ khung ảnh của scene đang render có hiệu ứng flash.
- Không dùng overlay toàn màn hình và không chặn thao tác ở scene khác.
- Có thể giữ ảnh cũ phía dưới trong lúc render lại; phủ flash/pulse đúng như Avatar Builder.
- Có trạng thái dễ hiểu: queued, rendering, success, failed.
- Hiệu ứng phải dừng ở mọi terminal path: success, failure, exception, cancellation và timeout.
- Overlay trang trí phải có `pointer-events: none`.
- Tôn trọng `prefers-reduced-motion`.

Ưu tiên trích xuất component/class dùng chung nếu Avatar Builder và Scene Image cùng nhu cầu. Không copy một phiên bản CSS thứ hai rồi để hai nơi lệch nhau.

## 8. Sửa triệt để lỗi render xong bị đơ màn hình

Đây là lỗi ưu tiên cao nhất. Hãy truy nguyên và sửa ở state/event/polling/overlay, không che lỗi bằng refresh trang.

### 8.1. State theo từng scene

- Không dùng một `_busy` toàn cục để khóa toàn editor khi chỉ một scene render.
- Dùng trạng thái theo `sceneId`/`sceneCode`, ví dụ dictionary hoặc set cho queued/rendering.
- Chỉ disable thao tác xung đột của đúng scene đang render.
- Người dùng vẫn phải chuyển tab, cuộn, mở prompt, xem lịch sử và thao tác scene khác.
- Chặn double-click/submit trùng trên cùng một scene.

### 8.2. Async và polling

- Không dùng `.Wait()`, `.Result`, sync-over-async hoặc giữ lock trong lúc await network/polling.
- Nếu render là job nền: enqueue nhanh rồi trả UI về trạng thái tương tác; polling chạy có cancellation và timeout.
- Không giữ `_reloadLock`/`SemaphoreSlim` qua vòng polling dài.
- Ngăn nhiều vòng `ReloadAsync` chồng nhau hoặc deadlock lẫn nhau.
- Polling chỉ cập nhật scene/version liên quan; không ghi đè prompt đang sửa (`dirty state`) của scene khác.
- Khi component dispose/đóng popup, phải cancel polling an toàn.

### 8.3. Dọn state trong mọi trường hợp

Mọi handler render phải có `try/catch/finally`:

- ghi log có `jobId`, `sceneId`, provider task id và trạng thái;
- thông báo lỗi thân thiện;
- trong `finally`, luôn xóa trạng thái busy/rendering tương ứng;
- gọi cập nhật UI đúng Blazor synchronization context (`InvokeAsync(StateHasChanged)` khi cần);
- không để backdrop/overlay/menu blocker còn tồn tại sau terminal state.

Kiểm tra DOM/CSS sau render để bảo đảm không có element phủ toàn viewport với `pointer-events: auto`, `z-index` cao hoặc backdrop chưa được gỡ.

## 9. Không phá nghiệp vụ

- Render lại ảnh vẫn tạo image version mới và chọn version mới nhất theo quy tắc hiện tại.
- Lịch sử ảnh vẫn mở được và chọn lại version cũ.
- Lưu scene vẫn lưu prompt/voice/voice instruction/version đang chọn.
- Không làm mất aspect ratio, resolution, provider routing, billing hoặc usage log.
- Không đổi schema database trong nhiệm vụ UI/bug này.

## 10. Test bắt buộc

Bổ sung test phù hợp vào `TodoX.Web.Tests` hoặc test helper hiện có:

1. Render scene A không khóa scene B và không khóa tab/editor.
2. Double-click scene A chỉ tạo một request/job hợp lệ.
3. Busy/flash state của scene được xóa khi success.
4. Busy/flash state được xóa khi provider failure, exception, cancellation và timeout.
5. Polling/reload không ghi đè prompt ảnh đang dirty.
6. Menu ba chấm có đúng các action và disable đúng trạng thái.
7. Không có overlay trang trí bắt pointer events.
8. Popup và scene list có cấu trúc scroll đúng.

Nếu test UI hiện tại chưa hỗ trợ đầy đủ, hãy tách state machine/service nhỏ có thể unit test; không bỏ qua kiểm thử chỉ vì logic đang nằm trong `.razor`.

## 11. Kiểm thử giao diện thực tế

Chạy browser/manual QA với job có tối thiểu 7 scene ở các viewport:

- 1920×1080
- 1366×768
- 1024×768
- 390×844

Xác nhận và ghi bằng chứng:

- scroll được đến scene cuối;
- toolbar cách tab, nằm bên phải, button compact có icon;
- prompt ảnh chiếm hết phần còn lại;
- menu ba chấm mở/đóng và gọi đúng action;
- khi scene 1 render, vẫn click scene 2, chuyển tab, mở lịch sử và cuộn được;
- sau success hoặc failure, toàn màn hình vẫn click được;
- hiệu ứng giống Avatar Builder và chỉ xuất hiện trên scene đang render;
- không có lỗi console và không có request render trùng.

Nên chụp screenshot trước render, trong khi render và sau render để đối chiếu.

## 12. Build và publish

Sau khi sửa:

1. Chạy format/check phù hợp cho các file đã đổi.
2. Chạy toàn bộ test liên quan.
3. Chạy build solution/project ở Release.
4. Chạy publish Release vào thư mục artifacts tạm riêng, không ghi đè thư mục publish production.
5. Không deploy.

Nếu build/test/publish lỗi, phải sửa đến khi pass; nếu bị chặn bởi hạ tầng, báo chính xác command, lỗi và phần nào đã xác minh.

## 13. Báo cáo cuối cùng

Báo cáo theo cấu trúc:

1. Đã đọc những `AGENTS.md` nào.
2. Root cause của lỗi scroll.
3. Root cause của lỗi render xong bị đơ.
4. Danh sách file đã sửa và vai trò từng file.
5. Cách tái sử dụng hiệu ứng Avatar Builder.
6. Test đã thêm và kết quả.
7. Kết quả build/publish với đường dẫn artifact.
8. Các bước QA thủ công đã chạy và bằng chứng.
9. Xác nhận không tạo migration, không chạy database và không deploy production.

Không kết luận “đã xong” nếu chưa chứng minh được rằng sau một lần render success và một lần render failure, popup vẫn cuộn và toàn bộ editor vẫn thao tác được.
