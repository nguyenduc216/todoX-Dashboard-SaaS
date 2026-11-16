---
name: 79ai
description: Làm việc với 79AI / 79router (Gommo) — tạo ảnh, video, nhạc qua gommo MCP; gọi HTTP API của 79router; quản lý dự án (project) và giới hạn credit (quota/budget) của từng dự án; quỹ credit (credit pool). Dùng khi người dùng nhắc 79AI, 79router, Gommo, gommo-mcp, tạo project/dự án 79AI, đặt quota hay ngân sách credit cho dự án, nạp credit cho dự án.
---

# 79AI / 79router (Gommo)

79router.com là cổng developer của 79AI (backend Gommo). Một token đăng nhập dùng cho cả MCP lẫn HTTP.
Skill này được bổ sung dần: mục nào ghi **CHƯA KIỂM CHỨNG** là mới đọc từ tài liệu, chưa gọi thật.
Khi gọi thật thành công hay thất bại, sửa lại mục đó (request, response mẫu đã che token) để lần sau dùng ngay.

## Nguồn sự thật

- Spec OpenAPI 3.0: `https://79router.com/openapi.yaml` (đọc bằng fetch; ~75 path). Trang người: `/reference/openapi`.
- MCP: `https://79router.com/mcp/`. Token lấy ở `https://79router.com/app/token/` (người dùng tự đăng nhập và copy).
- Playground (cần đăng nhập): `https://79router.com/app/playground/?worker=<worker>`, ví dụ `worker=ai-projects-create`.
- Trong repo TodoX: tích hợp 79AI ở `TodoX.Web` (base `https://api.gommo.net/ai`, chat `https://api.gommo.net/api/v2/chat`),
  ghi chép ở `TodoX.Web/docs/ai-provider-79ai-catalog-phase-c-report.md`.

## Quy tắc

- Không bao giờ in, ghi log hay commit token. Token đặt trong biến môi trường hoặc lấy từ người dùng, hiển thị dạng che `abcd…wxyz`.
- Tạo ảnh/video/nhạc, nạp credit vào quỹ, đặt budget là **tốn tiền hoặc đổi cấu hình thật**: báo giá/thay đổi và chờ người dùng đồng ý trước.
- Không tự đoán `ratio`, `mode`, `resolution`, `duration`: luôn đọc từ catalog (`gommo_models_list` hoặc `POST /ai/models`).
- Không tự đăng nhập bằng mật khẩu của người dùng. Cần token thì nhờ người dùng dán vào (hoặc đã có trong cấu hình MCP).

## Hai cách tích hợp

| | Gommo MCP | HTTP (79router API) |
|---|---|---|
| Dùng trong Claude / Cursor | ✅ | ❌ |
| Web, app, script, backend TodoX | ❌ | ✅ |

### MCP (đã cấu hình trên máy: `gommo-mcp`, `gommo-todox`)

URL `https://api.gommo.net/api/v2/gommo-mcp`, header `Authorization: Bearer <token>`. 10 tool:
`gommo_account_info`, `gommo_credit_balance`, `gommo_models_list`, `gommo_image_create`, `gommo_video_create`,
`gommo_image_status`, `gommo_video_status`, `gommo_task_stream`, `gommo_tasks_list`, `gommo_notify_send`.
Tạo media là bất đồng bộ: giữ `imageInfo.id_base` / `videoInfo.id_base` rồi hỏi trạng thái theo id đó.
**MCP chưa có tool quản lý dự án / quota** → phần dự án phải gọi HTTP (mục dưới).

### HTTP — tạo media (đã có trong code TodoX)

Host `https://v2.api.gommo.net`, `Authorization: Bearer <token>`, body `application/x-www-form-urlencoded`:
1. `POST /ai/models` — catalog, lấy giá trị hợp lệ.
2. `POST /ai/jobs/{type}/{modelSlug}` (type `image`/`video`…) — trả `id_base`.
3. `POST /ai/jobs/{id}` — hỏi lại mỗi ~3,5 giây, tối đa ~80 lần, tới khi có `result_url`. Không có webhook.

Host `https://api.gommo.net`: `/ai/me`, `/api/v2/chat`, `/ai/info/image/{id}`, `/ai/audio`, đăng nhập.

## Dự án (project) và quota — CHƯA KIỂM CHỨNG

Spec ghi gateway `POST /gateway/ai-projects` chuyển tiếp tới upstream `POST …/api/v2/ai-projects`
(host upstream chưa xác nhận, nhiều khả năng `https://api.gommo.net` giống `/api/v2/chat`). Body JSON, Bearer, trường `action`:

| action | Trường cần | Ghi chú |
|---|---|---|
| `list` | — | lấy `id_base` của từng dự án |
| `create` | `name`, tuỳ chọn `description`, `language` (`vi`) | tạo dự án |
| `get` | `id_base`, tuỳ chọn `password` | dự án có mật khẩu trả `needs_password` |
| `update` / `delete` | `id_base` (+ `name`, `description`) | |
| `set_password` | `id_base`, `password` (chuỗi rỗng = bỏ) | |
| `set_budget` | `id_base`, `budget` số nguyên ≥ 0 | **giới hạn credit của dự án; 0 = không giới hạn** |
| `stats` | `id_base`, `period` = `all`/`today`/`yesterday`/`this_month`/`last_month` | thống kê tiêu dùng |
| `activity_list` | `id_base`, tuỳ chọn `limit` (50), `offset` | nhật ký hoạt động |
| `member_list` / `member_kick` | `id_base` (+ `user_id`) | |
| `invite` | `id_base`, `identifier` (username/email), `role` = `member`/`mod`/`admin`, `scopes` | |
| `invite_revoke` | `invite_id` | |

Ví dụ (thay `<HOST>` khi đã xác nhận):

```bash
curl -s -X POST "<HOST>/api/v2/ai-projects" -H "Authorization: Bearer $GOMMO_TOKEN" -H "Content-Type: application/json" \
  -d '{"action":"create","name":"Khách A - TVC","description":"Dự án video khách A","language":"vi"}'
curl -s -X POST "<HOST>/api/v2/ai-projects" -H "Authorization: Bearer $GOMMO_TOKEN" -H "Content-Type: application/json" \
  -d '{"action":"set_budget","id_base":"<id_base>","budget":500000}'
```

Ảnh/video theo dự án: `POST /ai/library/album-images` và `album-videos` nhận `project_id` (mặc định `default`).
Spec chưa ghi `project_id` cho `POST /ai/jobs/...` → chưa rõ job tạo ra có tính vào dự án/budget hay không; cần thử.

### Quỹ credit (credit pool) — CHƯA KIỂM CHỨNG

Upstream `POST …/api/v2/credit-pools`, `action`: `list`, `ledger`, `create`, `deposit`, `delete`, `set_share_credit`.
- `create`: `scope_type` = `project`, `scope_id` = `id_base` dự án, `name`, `currency_name` = `credit_ai`, `amount`.
- `deposit` (bơm credit): `pool_id`, `amount`; ví phải còn dư 100.000 sau khi bơm (cần amount + 100k).
- `delete` trả ok không có nghĩa là credit được hoàn về ví.
- `set_share_credit`: `id_base`, `enabled` 0/1 — cho dự án dùng chung credit.

Phân biệt: `set_budget` là **trần chi tiêu** của dự án; credit pool là **tiền nạp riêng** cho dự án.

## Việc còn mở (bổ sung dần)

- [ ] Xác nhận host upstream của `ai-projects` / `credit-pools` (gọi `list` với token thật — chỉ đọc, an toàn).
- [ ] Gọi `create` + `set_budget` trên một dự án thử, ghi lại response mẫu.
- [ ] Thử `POST /ai/jobs/...` kèm `project_id` xem có trừ vào budget dự án không.
- [ ] Đơn vị của `budget` (credit hay VND) và hành vi khi vượt trần.
