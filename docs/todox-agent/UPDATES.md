# TodoX EditVideo — cập nhật phiên bản (từ 1.0.0)

## Phát hành bản mới
```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/editvideo/Build-EditVideoRelease.ps1 -Upload -Notes "X.Y.Z: <ghi chú cho khách>"
```
- Tăng `<Version>` trong `TodoX.EditVideoTool/TodoX.EditVideoTool.csproj` trước khi build.
- Ghi chú (`-Notes`) hiện cho khách ở thông báo Windows, thanh vàng trong ứng dụng và lời nhắc qua Claude.

## Bắt buộc cập nhật
Thêm `-MinSupported X.Y.Z`: mọi máy chạy bản thấp hơn bị dừng công cụ dựng video cho tới khi cập nhật.
- App 1.0+ tự chặn và hướng dẫn tắt mở lại Claude/Codex (bản mới đã được tải ngầm).
- Server cũng chặn (mã `app_outdated`) theo header `X-TodoX-App-Version`, hoặc theo phiên bản máy báo lần cuối
  với app cũ hơn 1.0 — nên dùng được cho cả bản cũ.
- Chỉ dùng khi thật cần (lỗi nghiêm trọng, đổi API): khách đang làm dở sẽ bị dừng.

## App biết và tải bản mới thế nào
- Khi Claude/Codex khởi động app và mỗi 6 giờ sau đó: hỏi `GET /api/editvideo/v1/releases/latest`, có bản mới thì
  tải exe, kiểm SHA-256, thay vào thư mục `app` của nơi cài (mặc định `%LOCALAPPDATA%\Programs\TodoX EditVideo`; bản trước 1.1 cài ở `%LOCALAPPDATA%\TodoX\EditVideoTool`) (giữ bản cũ `.old` để quay lại), báo
  Windows. Bản mới chạy từ lần mở lại sau.
- Thành phần mới chưa có thì tải ngầm; thành phần đã có chỉ thay khi khách bấm "Cập nhật ngay"/"Kiểm tra cập nhật".
- Dashboard: cột Phiên bản ở Khách hàng → License và trang khách hàng đánh dấu máy chạy bản cũ.

## Bộ cài (từ 1.1.0)
Mở file tải về (không nằm trong thư mục cài) là hiện bộ cài 5 bước; mở exe đã cài là hiện cửa sổ TodoX EditVideo.
1. Giới thiệu — tự nhận ra máy đã có TodoX EditVideo chưa; có rồi thì thành **cập nhật** vào đúng thư mục cũ.
2. Tùy chọn — thư mục cài (hộp chọn thư mục của Windows), kết nối Claude / Codex (chỉ hiện cái có trên máy),
   tự khởi động cùng Windows (lệnh `autostart`: tải bản mới + skill rồi thoát), biểu tượng Desktop.
3. Cài đặt — từng bước có dấu tích và nhật ký.
4. License — nhập key hoặc tích "Bỏ qua"; có license thì tải bộ skill ngay.
5. Hoàn tất — tuỳ chọn mở todox.vn và mở TodoX EditVideo.

Nhật ký mỗi lần cài nằm ở `<thư mục cài>\logs\install-<thời gian>.log` (đọc được) và `.json`. Khi máy có
license, nhật ký được gửi lên TodoX thành một lượt "Cài đặt / Cập nhật TodoX EditVideo" trong Nhật ký sử dụng
(Khách hàng → License và trang khách hàng); chưa có license thì gửi ở lần mở sau khi đã kích hoạt.

Chạy thử giao diện không cài thật: đặt `TODOX_SETUP_PREVIEW=new` (hoặc `1` để giả lập cập nhật), tuỳ chọn
`TODOX_SETUP_PREVIEW_PAGE=0..4`, rồi chạy `todox-editvideo setup`.
