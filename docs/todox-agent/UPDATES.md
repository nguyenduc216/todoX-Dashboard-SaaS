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
  tải exe, kiểm SHA-256, thay vào `%LOCALAPPDATA%\TodoX\EditVideoTool\app` (giữ bản cũ `.old` để quay lại), báo
  Windows. Bản mới chạy từ lần mở lại sau.
- Thành phần mới chưa có thì tải ngầm; thành phần đã có chỉ thay khi khách bấm "Cập nhật ngay"/"Kiểm tra cập nhật".
- Dashboard: cột Phiên bản ở Khách hàng → License và trang khách hàng đánh dấu máy chạy bản cũ.
