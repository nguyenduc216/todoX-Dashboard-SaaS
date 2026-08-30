# todoX Codex Task Workflow

## Mục đích

Skill này quy định cách Codex xử lý mọi yêu cầu code cho dự án todoX. Mục tiêu là kiểm soát phạm vi, giảm token/code dư thừa, giúp người dùng theo dõi tiến độ rõ ràng và tránh sửa ngoài yêu cầu.

## Ngôn ngữ bắt buộc

- Mọi phần diễn giải, báo cáo tiến độ, checklist, kết quả build/test và báo cáo cuối cùng phải viết bằng **tiếng Việt**.
- Tên class, method, biến, file, command, SQL, code và thông báo kỹ thuật gốc được giữ nguyên khi cần thiết.

## Quy tắc phạm vi

1. Chỉ sửa đúng nội dung người dùng yêu cầu trong task hiện tại.
2. Không tự mở rộng sang refactor, cleanup, redesign, tối ưu kiến trúc, sửa UI khác, sửa phân hệ khác hoặc xử lý lỗi ngoài scope nếu người dùng không yêu cầu.
3. Nếu phát hiện vấn đề ngoài scope:
   - chỉ ghi vào mục `Phát hiện ngoài phạm vi`;
   - không sửa;
   - không thêm vào checklist thực thi trừ khi người dùng phê duyệt.
4. Trước khi code, đọc code hiện tại đủ để xác định đúng file/phạm vi cần sửa. Không audit toàn repo nếu không cần thiết.
5. Ưu tiên thay đổi nhỏ nhất có thể để đạt yêu cầu.
6. Không thay đổi database/schema nếu task không thực sự cần. Nếu cần SQL, phải nói rõ vì sao.

## TASK CHECKLIST bắt buộc

Trước khi sửa code, Codex phải tạo checklist ngắn, chỉ chứa các việc thật sự cần làm cho task hiện tại.

Mẫu:

```text
TASK CHECKLIST
[ ] 1. <việc cụ thể thứ nhất>
[ ] 2. <việc cụ thể thứ hai>
[ ] 3. <test liên quan>
[ ] 4. Build
[ ] 5. Commit + push
```

Quy tắc checklist:

- Không thêm mục chung chung như `audit toàn hệ thống` nếu không cần.
- Mỗi mục phải kiểm chứng được.
- Hoàn thành mục nào thì đổi `[ ]` thành `[x]` ngay trong báo cáo tiến độ kế tiếp.
- Không được đánh `[x]` nếu chưa thực hiện hoặc chưa có bằng chứng phù hợp.
- Nếu một mục thất bại, giữ `[ ]` và ghi ngắn lý do ngay dưới mục đó.
- Nếu phát hiện task cần thêm một bước bắt buộc mới, phải giải thích trước khi thêm vào checklist.
- Không được âm thầm mở rộng checklist để sửa việc ngoài yêu cầu.

## Cập nhật tiến độ

Sau mỗi nhóm thay đổi đáng kể, Codex phải in lại checklist hiện tại bằng tiếng Việt.

Ví dụ:

```text
TASK CHECKLIST
[x] 1. Bỏ validation điểm ở RVIDEO
[x] 2. Ẩn thông báo điểm cũ trong scene history
[ ] 3. Test retry job cũ
[ ] 4. Build
[ ] 5. Commit + push
```

Không cần báo cáo dài. Chỉ nêu:
- mục vừa hoàn thành;
- file chính đã sửa;
- blocker nếu có.

## Kiểm soát thay đổi

Trước khi commit, Codex phải kiểm tra diff và xác nhận:

- Không có file ngoài scope bị sửa nhầm.
- Không có `bin/`, `obj/`, `publish/`, `artifacts/`, file build tạm, file secret hoặc credential bị commit.
- Không có refactor không cần thiết.
- Không thay đổi behavior của phân hệ khác ngoài phạm vi task.

Nếu có thay đổi ngoài scope do working tree đã bẩn từ trước:
- không tự revert nếu không chắc đó là thay đổi của người khác;
- báo rõ file nào không thuộc task;
- chỉ commit các file thuộc task nếu có thể tách an toàn.

## Build và test

- Chỉ chạy test liên quan trực tiếp trước; không chạy test suite khổng lồ nếu không cần.
- Build phải chạy trước khi đánh mục `Build` là `[x]`.
- Nếu build/test fail vì lỗi ngoài scope có sẵn từ trước, báo rõ và không sửa lỗi đó nếu chưa được yêu cầu.
- Không tuyên bố `đã hoàn thành` nếu build bắt buộc chưa pass.

## Commit và push

Khi task yêu cầu Codex hoàn tất code:

1. Build/test theo checklist.
2. Kiểm tra diff lần cuối.
3. Commit với message ngắn, đúng nội dung task.
4. Push lên đúng branch người dùng chỉ định.
5. Chỉ sau khi push thành công mới đánh `[x] Commit + push`.

Không commit file publish/build.

## Báo cáo cuối cùng bắt buộc

Báo cáo bằng tiếng Việt và ngắn gọn theo cấu trúc:

```text
TASK CHECKLIST
[x] ...
[x] ...

Đã sửa:
- <file>: <1 câu mô tả>

Build/Test:
- Build: PASS/FAIL
- Test: PASS/FAIL + tên test chính

Commit:
- SHA: <sha>
- Branch: <branch>

Phát hiện ngoài phạm vi:
- <nếu có; chỉ báo cáo, không sửa>
```

Nếu checklist còn `[ ]`, không được nói task đã hoàn tất.

## Quy tắc đặc biệt cho todoX

- Giữ tenant/customer authorization và dữ liệu production an toàn.
- Không tự ý thay đổi workflow Timelapse, RVIDEO, RDance, DanceSell hoặc billing khi task chỉ liên quan một phân hệ khác.
- Không tự ý chạy migration phá dữ liệu.
- SQL production phải additive/idempotent nếu có thể.
- Provider cost tracking và customer billing/point logic là hai khái niệm khác nhau; không trộn lẫn nếu task không yêu cầu.
- Khi có endpoint `/system/version`, sau deploy phải dùng nó để đối chiếu commit production nếu task có bước deploy/version verification.

## Cách hiểu yêu cầu từ prompt

Nếu prompt bắt đầu bằng hoặc có nội dung:

`Áp dụng skill todoX Codex Task Workflow`

thì Codex phải:
1. đọc file `.codex/skills/todox-codex-task-workflow/SKILL.md`;
2. tạo TASK CHECKLIST trước khi code;
3. làm đúng checklist;
4. báo cáo bằng tiếng Việt;
5. không mở rộng scope.
