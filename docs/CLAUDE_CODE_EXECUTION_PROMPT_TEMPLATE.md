# Mẫu prompt thực thi chuẩn cho Claude Code – Dự án TodoX Dashboard SaaS

> **Người nhận:** ChatGPT (Kiến trúc sư hệ thống)
> **Mục đích:** Sau khi phân tích và chốt hướng xử lý với chủ dự án, hãy viết prompt theo đúng mẫu này để Claude Code (coder thực thi) làm việc chính xác, đúng phạm vi và không phá các phân hệ đã hoàn thành.

---

## 1. Bối cảnh kỹ thuật cố định (không cần nhắc lại trong mỗi prompt)

| Hạng mục | Giá trị |
|---|---|
| Repo root | `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS` |
| Solution | `TodoX.Dashboard.sln` |
| Dự án chính | `TodoX.Web` (Blazor Server, .NET 10, MudBlazor 9.6) |
| Dự án khác | `TodoX.Landing`, `TodoX.SkillEndpoint`, `TodoX.Web.Tests` |
| Data access | PostgreSQL qua Npgsql + Dapper (**không dùng EF Core**) |
| Ảnh | SixLabors.ImageSharp |
| Cấu trúc | `Components/Pages`, `Components/Shared`, `Components/Dialogs`, `Services/*` (Render, VideoRender, Timelapse, DanceSell, PromptAssistant, AiProviders, Platform...), `Data`, `Models` |
| Quy tắc | `TodoX.Web/AGENTS.md`, `TodoX.Web/Components/Pages/AGENTS.md` |
| Publish mặc định | `...\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard` |

**Quy tắc Claude Code luôn tuân thủ** (ChatGPT không cần lặp lại, chỉ ghi khi muốn **ngoại lệ**):

- Không tạo/sửa/chạy migration; không chạy SQL trực tiếp vào DB. Cần đổi DB → tạo file SQL riêng để chủ dự án chạy tay.
- Không commit, không `git reset --hard`, không `git clean`, không force push, trừ khi được yêu cầu rõ.
- Không hard-code cấu hình, ID, model name, URL, credential.
- Phân hệ đã hoàn thành **mặc định bị đóng băng**: IMAGE GENERATION, VIDEO GENERATION, VOICE/AUDIO, POINT/BILLING và payload gửi provider.
- File có tiếng Việt giữ UTF-8, kiểm tra lỗi font (mojibake) sau khi sửa.
- Với YEScale: phải tra YEScale MCP trước, không tự đoán model ID/endpoint/giá.

---

## 2. Cấu trúc prompt chuẩn

Copy khung dưới đây và điền nội dung. Mục có dấu **(*)** là bắt buộc.

````markdown
# [MÃ TASK] – [Tên ngắn gọn của task]

## 1. Loại task (*)
<!-- Chọn MỘT: BUG FIX | FEATURE | ENHANCEMENT | REFACTOR (đã duyệt) | INVESTIGATION (chỉ phân tích) | HOTFIX -->

## 2. Mục tiêu (*)
<!-- 1–3 câu: kết quả cuối cùng người dùng/hệ thống nhận được. -->

## 3. Bối cảnh & hiện trạng (*)
- Màn hình / luồng liên quan: <!-- vd: /rvideo/jobs/{id}, RenderVideoJobs.razor -->
- Hiện trạng: ...
- Vấn đề (nếu là bug): <!-- triệu chứng, log lỗi, job id -->
- Các bước tái hiện (nếu là bug):
  1. ...
- Kết quả mong đợi: ...
- Kết quả thực tế: ...

## 4. Hướng xử lý đã chốt (*)
<!-- Tầng nào xử lý (UI / Service / Repository / Worker), luồng dữ liệu, quy tắc nghiệp vụ,
     trạng thái, điều kiện biên. Ghi rõ phương án ĐÃ CHỌN và lý do loại phương án khác. -->

## 5. Phạm vi (*)
### 5.1 Được phép sửa
- File/class/method dự kiến: ...
- Hành vi được phép thay đổi: ...
### 5.2 Được phép tạo mới
- <!-- file test, service mới, file SQL thủ công -->
### 5.3 Đóng băng – KHÔNG được động vào
- <!-- Mặc định: IMAGE / VIDEO / VOICE / BILLING / provider payload, trừ khi task thuộc chính phân hệ đó. -->

> Nếu buộc phải sửa ngoài phạm vi → Claude Code DỪNG và báo lại.

## 6. Yêu cầu chi tiết (*)
1. ...
2. ...

## 7. Ràng buộc kỹ thuật
- Database: <!-- "Không thay đổi DB" HOẶC mô tả bảng/cột → chỉ xuất file SQL thủ công -->
- Cấu hình: <!-- key mới trong appsettings (không chứa secret) -->
- Public API / contract: <!-- giữ nguyên hay được phép đổi -->
- Provider (79AI / KIE / YEScale...): <!-- model, endpoint đã xác minh; YEScale ghi "tra MCP" -->
- UI/UX: <!-- component MudBlazor, text tiếng Việt chính xác -->
- Hiệu năng / bảo mật / phân quyền: ...

## 8. Tiêu chí nghiệm thu (*)
- [ ] Given ... When ... Then ...
- [ ] Không thay đổi hành vi các phân hệ ở mục 5.3.

## 9. Kiểm thử yêu cầu
- Regression test (bắt buộc với bug): ...
- Kiểm thử thủ công chủ dự án cần làm: ...

## 10. Build & publish (chọn MỘT)
- [ ] Build + test, KHÔNG publish
- [ ] Build + test + publish vào thư mục mặc định
- [ ] Publish vào thư mục khác: ...
> Không deploy server / restart production trừ khi ghi rõ.

## 11. Chế độ thực hiện (chọn MỘT)
- [ ] PLAN: chỉ phân tích, trình bày kế hoạch + danh sách file sẽ sửa, CHỜ duyệt
- [ ] ACT: thực hiện luôn sau khi trình bày kế hoạch ngắn

## 12. Câu hỏi còn mở
<!-- Điểm chưa chốt. Claude Code sẽ hỏi lại thay vì tự đoán. -->
````

---

## 3. Claude Code sẽ trả về (để ChatGPT review)

1. **Kế hoạch & phạm vi** – phạm vi yêu cầu, file được phép sửa, vùng đóng băng.
2. **Nguyên nhân gốc** (với bug) – luồng thực thi đã trace, kèm `file:line`.
3. **File đã thay đổi** – danh sách đầy đủ + tóm tắt từng file.
4. **Hành vi đã thay đổi** – trước / sau.
5. **Vùng đóng băng không bị đụng tới** – xác nhận rõ ràng.
6. **Database** – "Không cần thay đổi DB" hoặc đường dẫn file SQL thủ công.
7. **Kiểm thử** – lệnh chính xác đã chạy + kết quả (build, test, regression test mới).
8. **Publish** – lệnh, thư mục output, kết quả (nếu yêu cầu).
9. **Hạn chế / rủi ro / việc còn lại** – những gì chưa xác minh được.

---

## 4. Nguyên tắc viết prompt hiệu quả

**Nên:**
- Mỗi prompt **một task rõ ràng**. Task lớn → chia Phase 1, 2, 3..., phase sau dựa trên báo cáo của phase trước.
- Ghi tên file, class, method, route, bảng/cột **nếu đã biết**. Không chắc → ghi "Claude Code tự xác định".
- Đưa **log lỗi, job id, payload mẫu** khi có.
- Ghi chính xác **text tiếng Việt** cần hiển thị trên UI.
- Nêu rõ **phương án đã loại** để tránh đề xuất lại.
- Tiêu chí nghiệm thu phải **kiểm chứng được**.

**Không nên:**
- Viết sẵn code hoàn chỉnh trong prompt (chỉ cần pseudo-code/chữ ký khi thật cần) – Claude Code sẽ đối chiếu code thực tế.
- Yêu cầu chung chung như "tối ưu lại", "làm đẹp code" mà không có phạm vi.
- Gộp nhiều phân hệ đóng băng trong một task.
- Bịa model ID, endpoint, giá provider, tên cột DB chưa xác minh.
- Đưa API key, mật khẩu, connection string thật vào prompt.

---

## 5. Prompt mẫu

````markdown
# RVID-042 – Hiển thị lý do thất bại của scene trên trang chi tiết RVideo

## 1. Loại task
ENHANCEMENT

## 2. Mục tiêu
Khi scene render thất bại, người dùng thấy lý do lỗi ngay trên trang chi tiết job.

## 3. Bối cảnh & hiện trạng
- Màn hình: `RVideoJobDetail.razor`
- Hiện trạng: scene lỗi chỉ hiện chip đỏ "Thất bại".
- Lý do lỗi đã lưu trong DB (Claude Code tự xác định nguồn dữ liệu hiện có).

## 4. Hướng xử lý đã chốt
- Chỉ đọc dữ liệu lỗi đã có, không thay đổi worker hay provider.
- Hiển thị tooltip + dòng text nhỏ dưới chip trạng thái; > 200 ký tự thì cắt ngắn.
- Đã loại: gọi lại provider để lấy lỗi (tốn chi phí, chậm).

## 5. Phạm vi
### 5.1 Được phép sửa
- `Components/Pages/RVideoJobDetail.razor`
- Model/repository đọc scene nếu cần thêm field (chỉ SELECT)
### 5.2 Được phép tạo mới
- Test trong `TodoX.Web.Tests`
### 5.3 Đóng băng
- VIDEO GENERATION worker, provider payload, POINT/BILLING, các trang khác.

## 6. Yêu cầu chi tiết
1. Scene thất bại hiển thị lý do lỗi.
2. Không có lý do → hiển thị "Không rõ nguyên nhân".
3. Không hiển thị stack trace hoặc thông tin nhạy cảm (API key, URL có token).

## 7. Ràng buộc kỹ thuật
- Database: Không thay đổi DB.

## 8. Tiêu chí nghiệm thu
- [ ] Given scene thất bại có lý do, When mở trang chi tiết, Then thấy lý do lỗi.
- [ ] Given lý do chứa URL có token, Then token bị ẩn.
- [ ] Hành vi render/billing không đổi.

## 9. Kiểm thử yêu cầu
- Unit test cho hàm làm sạch/cắt ngắn thông báo lỗi.

## 10. Build & publish
- [x] Build + test + publish vào thư mục mặc định

## 11. Chế độ thực hiện
- [x] ACT
````

---

## 6. Prompt nhanh (task nhỏ, rõ ràng)

````markdown
# [MÃ TASK] – [Tên]
- Loại: BUG FIX / ENHANCEMENT
- File/màn hình: ...
- Vấn đề: ...
- Kết quả mong đợi: ...
- Phạm vi được sửa: ...
- Đóng băng: mặc định (IMAGE / VIDEO / VOICE / BILLING / provider payload)
- DB: Không thay đổi
- Build/Publish: Build + test, không publish
- Chế độ: ACT
````

