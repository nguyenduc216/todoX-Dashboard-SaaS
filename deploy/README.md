# TodoX deploy

Khi người dùng nói **"deploy"** (hoặc "deployed", "đưa lên production"), làm đúng quy trình này. Không đoán
database hay thư mục server: mọi thứ lấy từ registry.

## Nguồn sự thật

| File | Nội dung | Git |
|---|---|---|
| `deploy/targets.json` | target → project, DLL, server, database logic, thư mục SQL, đường dẫn code để phát hiện thay đổi | commit |
| `deploy/remote.local.json` | tên server → host, port, user, SSH key | **không** commit |
| `C:/TodoXDeploy/deploy-config.json` trên server | target → website IIS, thư mục, app pool, URL kiểm tra | trên server |
| `tools/prod-sql/applied-ledger.tsv` | script SQL đã chạy trên database nào (theo SHA-256) | commit |
| `deploy/deploy-ledger.tsv` | commit đã deploy cho từng target | commit |

Thêm dự án mới: thêm một mục vào `targets.json`, rồi chạy lại `Setup-TodoXDeployServer.bat` trên server để đăng ký website IIS của nó với **cùng tên target**.

## Quy trình

1. Commit xong mọi thay đổi của tính năng (deploy từ chối khi còn file chưa commit).
2. Xem kế hoạch: `powershell -NoProfile -ExecutionPolicy Bypass -File deploy/Deploy-TodoXRemote.ps1 -Target auto -Plan`
   - `auto` = các target có file trong `pathsInRepo` thay đổi kể từ lần deploy cuối, hoặc còn SQL chưa chạy.
   - Báo kế hoạch cho người dùng (target nào, SQL nào, database nào) trước khi chạy thật.
3. Chạy thật: bỏ `-Plan`. Với mỗi target:
   - test → SQL chờ chạy (mỗi file: dry run + test → ROLLBACK, rồi apply + test → COMMIT trong một transaction)
   - publish → zip + SHA-256 → scp → `Apply-TodoXRelease.ps1 -Target <tên>` trên server
     (dừng app pool, sao lưu, thay code, giữ nguyên appsettings/web.config/keys/uploads, kiểm tra, lỗi thì tự rollback).
4. Commit `deploy/deploy-ledger.tsv` và `tools/prod-sql/applied-ledger.tsv`, push, báo cáo: target, commit, SQL đã chạy, kết quả kiểm tra.

## Quy tắc SQL

- Chỉ file trong `sqlDirs` của target **có kèm `NAME.test.sql`** mới được chạy tự động. File cũ không có test bị bỏ qua.
- SQL thay đổi mà không có `.test.sql` → deploy dừng lại.
- Script phải idempotent (`IF NOT EXISTS`) và chỉ thêm mới; file test phải `RAISE EXCEPTION` khi sai.
- Database đích lấy từ `targets.json → databases`, không bao giờ từ trí nhớ.

## Cài server (một lần)

Giải nén `deploy/server` lên server, chạy `Setup-TodoXDeployServer.bat` (Run as administrator), đăng ký từng website
(dashboard, landing, …). Gửi lại nội dung `C:\TodoXDeploy\server-info.txt` để điền `deploy/remote.local.json`.
