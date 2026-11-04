# TodoX EditVideo — kịch bản chạy đến bản phát hành

> Lập ngày 2026-11-03 · Nhánh `feature/todox-agent-20261103` · Hiện tại: P0–P3 xong, license chạy trên production.

## A. Đầu việc còn lại (chạy theo thứ tự)

| # | Việc | Kết quả |
|---|---|---|
| R1 | Đưa "bộ não" dựng video lên server: `POST /api/editvideo/v1/plan` nhận metadata clip + yêu cầu, trả kế hoạch dựng có chữ ký. Gỡ logic dựng khỏi exe. | Bí quyết không nằm trên máy khách; không license → không dựng. |
| R2 | Danh mục phong cách do server cấp (Sạch, Năng động, Bất động sản, Bán hàng): màu, font, kiểu phụ đề, chuyển cảnh. | Thêm phong cách không cần phát hành lại exe. |
| R3 | Làm cứng exe: thử NativeAOT; không được thì obfuscator. Minify bundle Remotion. | Rất khó dịch ngược. |
| R4 | P4 FFmpeg: chuẩn hóa clip điện thoại (xoay, HEVC, khung hình lệch, 4K → 1080p trung gian), tách tiếng; tool `todox_convert`. | Clip quay thật dựng ổn định. |
| R5 | P5 giọng Vbee qua TodoX: endpoint danh sách giọng + đọc văn bản, trừ point qua ví hiện có; tool `todox_voice_list`, `todox_voice_tts`; `todox_auto_edit` tự đọc lời và căn phụ đề theo giọng. Clone giọng cá nhân nếu Vbee có API. | Video có giọng đọc tiếng Việt. |
| R6 | Giao diện cửa sổ cho exe khi bấm đúp: nhập key, trạng thái, thành phần, kết nối Claude/Codex, gỡ máy. | Hết cửa sổ đen trống. |
| R7 | Bộ cài `TodoX-EditVideo-Setup.exe`: tải thành phần có checksum từ server TodoX, đăng ký Claude Desktop / Claude Code / Codex, cài skill, gỡ cài sạch, tự cập nhật. | Khách tải 1 file là dùng. |
| R8 | Trang khách tự quản lý trên dashboard: license, thiết bị, tự gỡ máy, tải bộ cài. | Khách tự đổi máy. |
| R9 | Trang giới thiệu trên `todox.vn`. | Có trang bán hàng. |
| R10 | Kiểm thử cuối: build sạch, toàn bộ test, cài như khách mới, kích hoạt key thật, dựng 9:16 + 16:9 có giọng, thử vượt máy, gỡ máy. | Bằng chứng chạy trọn. |
| R11 | Phát hành: SQL mới (có test), deploy dashboard + landing, đưa bộ cài lên server, commit, push. | Bản cuối. |
| R12 | Báo cáo + tài liệu cài cho khách + tài liệu vận hành. | Gửi về điện thoại. |

## B. Quy tắc khi chạy một mạch

- Không hỏi giữa chừng. Việc nào bị chặn → ghi vào báo cáo, làm tiếp việc khác.
- SQL production chỉ qua `tools/prod-sql/Invoke-TodoXSql.ps1`; deploy chỉ qua `deploy/Deploy-TodoXRemote.ps1`.
- Không sửa luồng billing / voice / video đang chạy, chỉ gọi lại.
- Trên server chỉ đụng site `todoX.SaaS`, `todoX` và `C:\TodoXDeploy` (+ thư mục tải bộ cài).
- Không xoá dữ liệu, không force push.
- Báo điện thoại khi xong toàn bộ hoặc khi bị chặn.

## C. Đã chốt với chủ dự án (2026-11-03)

| # | Quyết định |
|---|---|
| C1 | Kế hoạch dựng chạy trên server. **Trước mỗi lần dùng, exe chạy "kiểm tra hệ thống"** (R0 bên dưới). |
| C2 | Làm cứng exe: NativeAOT, không được thì obfuscator. |
| C3 | Vbee: dùng cấu hình có sẵn `public.todox_config` khóa `rvideo.vbee.*` (tài khoản thật). Giọng có sẵn trước; clone giọng nếu Vbee có API. |
| C4 | Giá giọng đọc = giá voice đang cấu hình trong dashboard (`billing.point_rate_config`, mặc định Voice standard 500 point/lần, Voice premium 800 point/lần — 1 point ≈ 1 đồng). Đọc giá lúc chạy, không ghi cứng. |
| C5 | Trừ point qua ví có sẵn, không sửa luồng billing. Khách hết point → báo khách khi họ dùng giọng. **Tài khoản Vbee của TodoX hết tiền → báo admin TodoX** (log hệ thống + cảnh báo dashboard) và báo khách "dịch vụ giọng tạm ngưng". |
| C6 | Bộ cài + thành phần + bản cập nhật tải từ `dashboard.todox.vn/downloads/editvideo/`. **Mỗi lần khởi động exe kiểm tra bản mới**, có thì tải về và cập nhật. |
| C7 | FFmpeg bản LGPL. |
| C8 | Chưa ký số. Bộ cài và cửa sổ exe hiện rõ: "Ứng dụng chưa đăng ký chữ ký số với Windows — đây là phần mềm chính hãng TodoX. Hỗ trợ: todox.vn" kèm thông tin liên hệ. |
| C9 | Thêm quyền cho tài khoản khách xem trang thiết bị (SQL có test). |
| C10 | Phát hành: **chỉ deploy**, chưa merge `main`. |
| C11 | Một gói Standard (1 máy tính + 1 điện thoại). |
| C12 | Thương hiệu: vàng `#FFBE2E` trên nền tối `#0B0D10`, logo `TodoX.Landing/wwwroot/img/logo/todox-logo.png` (chữ Todo trắng, X vàng), slogan "TODO EXCELLENCE". |
| C13 | License key mới dạng **token dài** (ví dụ `tdxv_` + 48 ký tự). Key dạng cũ `TDXV-…` vẫn dùng được. |

## D. Việc bổ sung theo yêu cầu (chạy trước R1)

| # | Việc |
|---|---|
| R0 | **Kiểm tra hệ thống trước khi chạy** (cửa sổ console + tool `todox_status`): FFmpeg/ffprobe, Node, Remotion, trình duyệt dựng hình, license, kết nối server TodoX, phiên bản mới. **Không kết nối được server TodoX** → hiện ngay thông báo Windows (toast) "Không kết nối được máy chủ TodoX, sẽ thử lại sau 15 giây", tự thử lại mỗi 15 giây. **Kết nối được mà có lỗi** → gửi lên server. |
| R0b | **Nhật ký sử dụng theo từng lần dùng**: bảng mới `editvideo.usage_sessions` + `editvideo.usage_logs` (SQL có test); endpoint nhận log; mỗi lần khách dùng tool = 1 phiên, ghi các bước, lỗi, thời gian dựng; dashboard xem nhật ký theo khách. Log chỉ chứa thông tin kỹ thuật, không gửi video hay nội dung lời thoại. |

## E. Điều kiện để chạy không cần người

1. Key thử do chủ dự án cấp trên dashboard (để test R10).
2. Quy tắc cho phép trong `.claude/settings.local.json` (SSH/SCP tới server bằng khóa deploy, script deploy, script SQL).
3. Máy tính bật, app Claude mở, có mạng.
