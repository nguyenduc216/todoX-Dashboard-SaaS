# TodoX.SkillEndpoint

API gateway dành riêng cho AI/ChatGPT kiểm tra và vận hành render job TodoX một cách có kiểm soát.

## Mục tiêu

- Đọc job + scene + provider task + billing/usage.
- Chẩn đoán lỗi lệch state, timeout polling, missing provider task, scene failed nhưng retry query không thấy.
- Tạo repair plan trước khi thay đổi dữ liệu.
- Reconcile trạng thái local với provider.
- Retry chỉ scene lỗi hoặc scene được chỉ định.
- Resume job từ bước phù hợp, giữ lại media đã thành công.
- Thực thi một số repair action đã whitelist, không cho AI chạy SQL tự do.
- Có audit log và idempotency cho mọi thao tác write.

## Kiến trúc đề xuất

```text
ChatGPT Skill / Custom Connector
        |
        | HTTPS + X-TodoX-Skill-Key
        v
TodoX.SkillEndpoint
        |
        | HTTPS + X-TodoX-Ops-Key
        v
TodoX Operations API (/api/ops/v1)
        |
        +--> PostgreSQL / render state
        +--> n8n orchestration
        +--> 79AI / YEScale / KIE ... provider status
        +--> billing + usage
```

Không cho SkillEndpoint kết nối database production với quyền SQL tùy ý. Logic sửa dữ liệu phải nằm trong TodoX Operations API để có transaction, validation, tenant/customer scope, audit và idempotency.

## Public skill endpoints

| Method | Endpoint | Mục đích |
|---|---|---|
| GET | `/health` | health check |
| GET | `/api/skill/v1/jobs/{jobId}` | snapshot job + scenes |
| GET | `/api/skill/v1/jobs/{jobId}/diagnostic` | chẩn đoán toàn bộ job |
| POST | `/api/skill/v1/jobs/{jobId}/repair-plan` | tạo phương án sửa, read-only |
| POST | `/api/skill/v1/jobs/{jobId}/reconcile` | kiểm tra provider và đồng bộ local state |
| POST | `/api/skill/v1/jobs/{jobId}/retry` | retry scene lỗi/chỉ định |
| POST | `/api/skill/v1/jobs/{jobId}/resume` | tiếp tục pipeline từ trạng thái hiện tại |
| POST | `/api/skill/v1/jobs/{jobId}/repair` | thực thi repair code whitelist |
| GET | `/api/skill/v1/actions/{actionId}` | kiểm tra action async |

## TodoX Operations API cần triển khai

SkillEndpoint hiện gọi các endpoint nội bộ tương ứng:

```text
GET  /api/ops/v1/render-jobs/{jobId}
GET  /api/ops/v1/render-jobs/{jobId}/diagnostic
POST /api/ops/v1/render-jobs/{jobId}/repair-plan
POST /api/ops/v1/render-jobs/{jobId}/reconcile
POST /api/ops/v1/render-jobs/{jobId}/retry
POST /api/ops/v1/render-jobs/{jobId}/resume
POST /api/ops/v1/render-jobs/{jobId}/repair
GET  /api/ops/v1/actions/{actionId}
```

### Diagnostic response tối thiểu

```json
{
  "success": true,
  "job": {
    "id": 397,
    "job_uuid": "...",
    "status": "completed_with_errors",
    "pipeline_step": "finalized"
  },
  "summary": {
    "scene_count": 5,
    "success": 4,
    "failed": 1,
    "pending": 0
  },
  "scenes": [
    {
      "scene_index": 2,
      "image_status": "completed",
      "video_status": "timeout_pending",
      "provider_task_id": "...",
      "provider_status": "processing",
      "poll_count": 31,
      "retryable": true,
      "local_state_mismatch": true,
      "error_code": "TIMEOUT_PENDING",
      "error_message": "video vẫn đang được provider xử lý"
    }
  ],
  "issues": [
    {
      "code": "SCENE_RETRY_STATE_MISMATCH",
      "severity": "error",
      "scene_index": 2,
      "message": "Finalizer coi scene là lỗi nhưng retry query không chọn scene này."
    }
  ],
  "recommended_actions": [
    {
      "code": "RECONCILE_PROVIDER_TASK",
      "safe": true,
      "requires_confirmation": false
    },
    {
      "code": "MARK_SCENE_RETRYABLE_AFTER_TIMEOUT",
      "safe": false,
      "requires_confirmation": true
    }
  ]
}
```

## Quy tắc retry bắt buộc

1. `failed_only` mặc định.
2. Scene đã completed không submit provider lần nữa.
3. Nếu local báo timeout nhưng provider task vẫn tồn tại, phải `reconcile` trước; không tạo task mới ngay.
4. Chỉ tạo task mới khi provider task đã failed/expired/not_found hoặc repair policy cho phép.
5. Mọi write phải nhận `X-Idempotency-Key` và lưu kết quả để request lặp không tạo task trùng.
6. Retry phải lưu `retry_of`, attempt number, provider task cũ/mới, điểm reserve/complete/refund.
7. Finalizer và retry selector phải dùng cùng một canonical state machine / hàm `IsRetryable(scene)`.

## Repair codes whitelist đề xuất

- `RECONCILE_PROVIDER_TASK`
- `MARK_TIMEOUT_SCENE_RETRYABLE`
- `CLEAR_STALE_SCENE_LOCK`
- `RESET_FAILED_SCENE_TO_QUEUED`
- `REBUILD_JOB_SUMMARY`
- `REQUEUE_FINALIZER`
- `REQUEUE_VIDEO_WORKER`
- `REQUEUE_MERGE`
- `RECONCILE_BILLING`

Không hỗ trợ `execute_sql`, `update_table`, `delete_row` hoặc bất kỳ action SQL tổng quát nào.

## Bảo mật

Public SkillEndpoint:

```http
X-TodoX-Skill-Key: <secret>
```

Các thao tác thay đổi dữ liệu còn bắt buộc:

```http
X-Idempotency-Key: <uuid-or-unique-request-key>
```

SkillEndpoint gọi TodoX Operations API bằng secret riêng:

```http
X-TodoX-Ops-Key: <internal-secret>
```

Nên đặt SkillEndpoint sau Cloudflare/WAF, chỉ HTTPS, rate limit và IP rules nếu hạ tầng cho phép. Không log API key, token provider hoặc prompt/media base64.

## Cấu hình

Copy `appsettings.example.json` thành cấu hình deployment hoặc dùng environment variables:

```text
SkillEndpoint__ApiKey
SkillEndpoint__TodoXOperationsBaseUrl
SkillEndpoint__TodoXOperationsApiKey
SkillEndpoint__AuditLogPath
```

## Chạy local

```bash
dotnet restore TodoX.SkillEndpoint/TodoX.SkillEndpoint.csproj
dotnet run --project TodoX.SkillEndpoint/TodoX.SkillEndpoint.csproj
```

Sau khi TodoX Operations API được triển khai, có thể dùng OpenAPI của project này làm schema cho plugin/skill để ChatGPT tự gọi các action chẩn đoán và sửa job.
