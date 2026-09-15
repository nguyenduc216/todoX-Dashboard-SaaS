# TDC-RDN-79AI-MOTION-REGRESSION-BISECT-20260915-004

## Kết luận

**ROOT CAUSE: CHƯA ĐƯỢC CHỨNG MINH.**

Runtime đã xác nhận lỗi xảy ra sau khi upload và verify cả reference image
và motion video, tại bước `79AI Kling Motion submit`. Tuy nhiên, Git history
không chứng minh việc chuyển từ auto-render sang nút `Tạo video` đã thay đổi
provider payload hoặc provider pipeline.

`a827d51` là candidate đáng ưu tiên kiểm tra tiếp vì commit này đổi URL được
gửi vào submit từ URL đã verify sang canonical upload URL. Nhưng hiện chưa có
evidence provider cho biết URL upload là nguyên nhân trực tiếp của lỗi job
`cb0be134-f835-4f02-8f66-3db9bc00bb9d`.

Không sửa code, không tạo commit, không push, không gọi live 79AI và không
thay đổi database.

## Base Và HEAD

- Repository: `nguyenduc216/todoX-Dashboard-SaaS`
- Target branch: `feature/rdn-onepage-ui-revamp`
- Base/remote HEAD đã audit: `16fa33eba1c3998710c3ea0a92a6de3a0260e454`
- Audit worktree: detached tại `16fa33e`
- Worktree chính: đang ở branch `feature/admin-job-monitor` và có thay đổi
  AdminJobMonitor không thuộc task này; các thay đổi đó không bị đụng tới.

## Runtime Failure Đã Xác Nhận

- RDance job: `cb0be134-f835-4f02-8f66-3db9bc00bb9d`
- Render job: `84572167-dc6b-4fa5-95f7-dabd590ebc38`
- Operation: `23f20dd3-184c-438e-a6a7-b28155a15802`
- Provider/model: `79ai` / `kling_video_motion_3`

Observed sequence:

```text
JOB_QUEUED
WORKER_CLAIMED
reference upload       PASS
reference verify       PASS
motion upload          PASS
motion verify         PASS
AI79_MOTION_SUBMIT_STARTED
AI79_MOTION_SUBMIT_FAILED
provider_task_id = NULL
```

Provider message:

```text
Tải media lên thất bại, vui lòng kiểm tra file và thử lại.
```

Assets của job này được upload và verify trong cùng attempt. Vì vậy stale asset
reuse đơn thuần không giải thích được failure này.

## Git History Đã Điều Tra

| Commit | Ngày | Thay đổi | Ảnh hưởng provider | Đánh giá |
|---|---|---|---|---|
| `e9b6594` / `36a739d` | 2026-08-16 | Kết nối RDance fashion với 79AI motion | Có | Mốc khởi tạo flow |
| `b75163b` | 2026-08-17 | Chuyển sang upload asset rồi submit URL | Có | Thay đổi provider contract lớn |
| `368c0eb` | 2026-08-18 | Chuẩn hóa bearer auth và v2 route | Có | Provider behavior change |
| `1d96af8` | 2026-08-18 | Dùng approved reference URL | Có | Sau đó bị thay thế bởi binary upload |
| `5ea73e7` | 2026-08-18 | Verify media trước motion submit | Có | Thêm verify provider-side |
| `93a823f` | 2026-08-18 | Chuẩn hóa list endpoint `/images`, `/videos` | Có | Provider endpoint change |
| `b76c742` | 2026-08-18 | Tách base URL cho media list | Có | Provider endpoint change |
| `a827d51` | 2026-08-19 | Giữ canonical upload URL khi submit | Có | Candidate mạnh nhất |
| `8fc8672` | 2026-09-09 | Reuse asset theo render job, reverify asset cũ | Có | Ảnh hưởng asset selection |
| `459af2b` | 2026-09-09 | Bắt buộc URL verify khớp URL asset cũ | Có | Ảnh hưởng reuse |
| `24fcd6b` | 2026-09-07 | Thêm RDance one-page UI | Không | Chỉ thêm UI/route flag |
| `74b977d` | 2026-09-09 | Auto-prepare sau upload | Không | Không queue provider render |
| `4c427dc` | 2026-09-09 | Reference re-entry/approval | Không | Chỉ lifecycle UI/reference |
| `e88fee8` | 2026-09-09 | Sửa terminal New UI đi qua retry | Không | Không đổi provider |
| `ef1261c` | 2026-09-10 | Yêu cầu explicit render bằng nút | Không | Đổi entry point, không đổi handler |
| `715ba3a` | 2026-09-10 | Duration gate cho manual render | Không | Readiness/billing gate |
| `6d6b62e` | 2026-09-10 | Hoàn thiện duration gate | Không | Không đổi provider |

## Last Known Provider Implementation

Mốc provider pipeline gần nhất trước các thay đổi one-page UI là các commit
provider ngày 2026-08-18/19. Trong implementation hiện tại:

```text
RDanceJobDetail.razor
  -> ConfirmAndQueueAsync()
  -> QueueRenderFromUserActionAsync()
  -> DanceSell.QueueRenderAsync()
  -> render job worker
  -> DanceSellRenderHandler.HandleAsync()
  -> Submit79AiAsync()
  -> Ai79TaskClient.SubmitMotionControlAsync()
```

`Ai79TaskClient.SubmitMotionControlAsync()` hiện gửi:

```text
POST /ai/jobs/video/kling_video_motion_3
Authorization: Bearer <token>
Content-Type: application/x-www-form-urlencoded

domain
project_id
model
prompt
image_url
video_url
subType
background_source
mode
ratio
images[0][url]
```

Việc chuyển explicit render không sửa method này, không sửa
`DanceSellRenderHandler.Submit79AiAsync()`, không sửa route construction và
không sửa request fields.

## Auto-Render Change

Các commit liên quan:

- `74b977d`: gọi auto-prepare sau upload/stage motion và tiếp tục reference
  lifecycle.
- `4c427dc`: thêm reference re-entry khi job draft có motion ready.
- `e88fee8`: route terminal job của New UI qua `RetryAsync()`.
- `ef1261c`: bỏ queue tự động trong `ContinueAutoFinishAsync()` và đưa queue
  vào `QueueRenderFromUserActionAsync()`.

Commit có thay đổi explicit render quan trọng nhất là `ef1261c`.

Expected scope của thay đổi:

```text
upload/reference preparation
  -> không queue render
user bấm "Tạo video"
  -> QueueRenderAsync
```

Actual scope:

- `RDanceJobDetail.razor`: thay đổi entry point và guard.
- `DanceSellPhase2Services.cs`: thay đổi validation service/pricing lookup.
- Không thay đổi `DanceSellRenderHandler.cs`.
- Không thay đổi `Ai79TaskClient.cs`.
- Không thay đổi upload, verify, URL selection, endpoint hoặc payload provider.

**UNEXPECTED PROVIDER CHANGES: Không phát hiện trong commit explicit-render.**

## a827d51 Investigation

`a827d51` thay đổi các giá trị sau:

```diff
- referenceUrlUsed = verifiedReference.Url
+ referenceUrlUsed = referenceUpload.Url

- motionProviderUrl = verifiedMotion.Url
+ motionProviderUrl = motionUpload.Url
```

Commit đồng thời lưu riêng:

- `uploadUrl`
- `verificationMatchedUrl`
- `verificationDownloadUrl`

và thêm `GetCanonicalProviderUploadUrl()`.

Ý nghĩa:

- URL upload là URL canonical được submit.
- URL verify/list có thể chứa biến thể như `?full=1`.
- Test được thêm vào commit đang khóa implementation choice
  `uploadUrl`, nhưng test không chứng minh provider chấp nhận URL đó trong
  mọi runtime condition.

Đánh giá hiện tại: **STILL CANDIDATE, CHƯA CHỨNG MINH CAUSAL**.

Lý do:

1. Runtime failure hiện tại dùng fresh upload và fresh verify thành công.
2. Chưa có paired run cùng một input chứng minh:
   - upload URL fail;
   - verified URL pass.
3. Chưa có log provider response đủ chi tiết để xác định provider reject URL
   nào.
4. `a827d51` xảy ra trước one-page UI và là provider behavior change độc lập,
   không phải một phần của explicit-render migration.

## Old Path Và New Path

### Old automatic path

```text
upload/stage motion
  -> AutoPrepareReferenceAsync()
  -> ContinueAutoFinishAsync()
  -> approval
  -> QueueRenderAsync()
  -> render job
  -> DanceSellRenderHandler
  -> upload/verify provider assets
  -> SubmitMotionControlAsync()
```

### New explicit path

```text
upload/stage motion
  -> AutoPrepareReferenceAsync()
  -> reference ready/approved
  -> STOP
  -> user bấm "Tạo video"
  -> ConfirmAndQueueAsync()
  -> QueueRenderFromUserActionAsync()
  -> DanceSell.QueueRenderAsync()
  -> render job
  -> DanceSellRenderHandler
  -> upload/verify provider assets
  -> SubmitMotionControlAsync()
```

### First behavioral divergence

Điểm khác đầu tiên là **thời điểm tạo render job/operation**, không phải
provider request construction.

Handler hiện tại:

- tìm asset reference cùng `renderJobId` trước;
- nếu không có thì fallback `GetLatestAssetAsync()` theo job/operation;
- reverify reference asset cũ nếu reuse;
- upload mới nếu không reuse được;
- tìm motion asset cũ theo job/operation;
- submit cùng route/payload contract.

Do đó, việc trì hoãn queue **có thể** làm khác asset lookup và operation ID,
nhưng chưa chứng minh job lỗi đã dùng asset reuse. Job lỗi thực tế ghi nhận
fresh reference upload/verify và fresh motion upload/verify.

## Blocking Condition Chính Xác

Blocking condition được xác nhận trong runtime là:

```text
AI79_MOTION_SUBMIT_FAILED
provider_task_id = NULL
provider trả lỗi tải media
```

Không phải queue creation, worker claim, local upload, reference generation
hoặc media verification.

## Root Cause Decision

**B. ROOT CAUSE NOT YET PROVEN**

Các candidate xếp hạng:

1. **Provider không chấp nhận canonical upload URL trong submit**
   - Liên quan `a827d51`.
   - Cần paired evidence upload URL versus verified URL.
2. **Provider submit contract/runtime route không còn khớp**
   - Có thể liên quan các thay đổi route/auth trước `a827d51`.
   - Cần sanitized request metadata và route config runtime của job lỗi.
3. **Khác biệt asset/operation do explicit queue timing**
   - Có thể làm đổi asset lookup.
   - Không phù hợp với evidence fresh upload + fresh verify của job lỗi, nên
     xếp sau.
4. **Provider-side media availability/race sau verify**
   - Verify list thành công chưa chắc endpoint submit đọc cùng storage/region.
   - Cần timestamp và provider identity đối chiếu.

## Instrumentation Tối Thiểu Đề Xuất

Không triển khai trong task này. Chỉ cần bổ sung sanitized diagnostics tại
submit attempt:

1. `renderJobId`, `operationId`, `submitAttempt`.
2. `referenceUrlKind`: upload/verified/reused.
3. Reference:
   - URL host/path đã sanitize query;
   - `idBase`;
   - upload timestamp;
   - verify timestamp;
   - verify status.
4. Motion:
   - URL host/path đã sanitize query;
   - `idBase`;
   - upload timestamp;
   - verify timestamp;
   - verify status.
5. Route:
   - base URL host;
   - endpoint path;
   - model;
   - domain;
   - project ID;
   - mode/ratio/subType/background source;
   - không ghi token.
6. Provider HTTP status, error code/message đã sanitize.

Để phân biệt candidate 1, cần một controlled test/mocked provider hoặc
provider-approved retry thử đúng cùng asset identity với:

```text
run A: image_url/video_url = upload URL
run B: image_url/video_url = verified URL
```

Không nên revert `a827d51` trên production chỉ dựa trên suy đoán.

## Fix

**NONE.**

Không có code fix vì root cause chưa đủ bằng chứng. Giữ nguyên yêu cầu nghiệp
vụ:

- upload/reference preparation không auto-render;
- nút `Tạo video` vẫn là explicit render entry point duy nhất;
- không khôi phục auto-render;
- không thay đổi provider payload speculative.

## Files Changed

Không có production file nào thay đổi.

Chỉ tạo report:

- `docs/TDC-RDN-79AI-MOTION-REGRESSION-BISECT-20260915-004.md`

## Validation

Task này là audit/bisect, không sửa code. Vì vậy không chạy build, test hoặc
publish để tránh biến validation thành kết luận sai về regression.

- Build: **NOT RUN**
- Tests: **NOT RUN**
- Publish: **NOT RUN**
- Live 79AI call: **NONE**
- Database/migration: **NONE**
- Production deployment: **NONE**

## Commit Và Push

- Commit: **NONE**
- Push: **NONE**
- Remote HEAD được kiểm tra: `16fa33eba1c3998710c3ea0a92a6de3a0260e454`

## Protected Areas Untouched

- Image generation
- RVideo
- Timelapse
- Voice/audio
- Billing, wallet, points, pricing
- Duration
- TikTok
- Database schema/migrations
- Provider credentials/secrets
- Production configuration/deployment

