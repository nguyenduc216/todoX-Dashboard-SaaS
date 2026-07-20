# Phase 0 - KIE Provider Va Tinh Nang Nhay Ban Hang

Ngay lap: 2026-07-20

Pham vi: khao sat va lap ke hoach tich hop provider KIE cho tinh nang "Nhay ban hang" (`dance-sell`). Phase nay khong trien khai production code, khong tao migration, khong thay doi luong OpenRouter hoac YEScale.

## 1. Kien truc hien tai

### Provider architecture

- Provider/config dang nam trong bang `public.todox_ai_provider`, `public.todox_ai_provider_capability`, `public.todox_ai_provider_usage_log`.
- Model DTO/catalog: `TodoX.Web/Models/AiProviderModels.cs`.
- Service quan ly/resolver: `TodoX.Web/Services/AiProviders/AiProviderService.cs`, repository Dapper la `AiProviderRepository.cs`.
- Capability hien co gom `avatar_generation`, `chibi_avatar_generation`, `character_generation`, `scene_image_generation`, `image_to_video`, `text_to_video`... Chua co capability rieng cho motion-control/dance-sell.
- Provider resolver dang tra `ProviderOptionDto` theo `capability_code`, default hoac priority. UI admin provider tai `Components/Pages/AiProviders.razor`.
- Logging provider dung `IAiProviderService.LogUsageAsync`, ghi vao `public.todox_ai_provider_usage_log` voi provider/model/request/job/cost/status/metadata.
- OpenRouter/YEScale dang duoc route rieng qua image factory/router va YEScale task client. Khong nen sua cac path nay trong feature KIE.

### Render job architecture

- Queue chinh la DB table `render.render_jobs`, event timeline la `render.render_job_events`.
- Interface worker: `IRenderJobHandler`; service enqueue/claim/status: `TodoX.Web/Services/Render/RenderJobService.cs`.
- Worker:
  - `RenderJobWorker` claim tat ca job tru `render_scene_video`.
  - `SceneVideoJobWorker` claim rieng `render_scene_video`.
  - Claim dung `FOR UPDATE SKIP LOCKED`, `lock_owner`, `lock_until`, `retry_after`, `attempt_count`.
- Status code trong `RenderJobModels.cs`: `queued`, `preparing`, `rendering`, `post_processing`, `pending_reconciliation`, `completed`, `failed`, `cancelled`.
- Job type hien co: `render_video_job`, `render_scene_video`, `merge_video_job`. Can them job type moi cho `dance_sell`.
- Request/response JSON hien co tren job: `input_json`, `prompt_json`, `reference_json`, `output_json`; event data nam o `render_job_events.data_json`.
- Cost/error fields tren job: `point_cost_estimate`, `point_cost_charged`, `point_status`, `provider_code`, `model_code`, `error_code`, `error_message`, `cancel_reason`.
- Scene/video versioning dang dung cac bang `video_render.scene_image_versions`, `video_render.scene_video_versions`, `video_render.final_video_versions`. KIE dance-sell khong nen chen vao scene pipeline neu khong can lien ket project/scene.

### Storage

- Service media: `TodoX.Web/Services/Media/MediaFileService.cs`.
- Code hien tai luu local theo `Storage:LocalUploadRoot` mac dinh `wwwroot/uploads`, public URL theo `Storage:PublicUploadBase` mac dinh `/uploads`. Comment co noi co the swap MinIO sau, nhung implementation doc duoc hien tai chua phai MinIO client truc tiep.
- Bang media: `media.media_files`.
- Upload anh dung `SaveAsync`/`SaveAtObjectKeyAsync`; upload/download video dung `SaveBinaryAtObjectKeyAsync`/`DownloadAndSaveBinaryAtObjectKeyAsync`.
- Gioi han file theo config: `MediaStorage:MaxImageBytes` fallback 20 MB, `MediaStorage:MaxVideoBytes` fallback 500 MB. Mot so UI cu goi `OpenReadStream(10 * 1024 * 1024)` hoac 12 MB cho anh.
- Tai URL ve local co SSRF guard: chi cho http/https, chan localhost/loopback/private IP, kiem tra MIME va sniff MP4.
- TikTok/reference video dang co bang/service rieng `content.reference_videos`, `ReferenceVideoRepository`, page `/reference-videos`, preview dialog `ReferenceVideoPreviewDialog`. Chua thay co downloader TikTok truc tiep cho dance source trong media service; reup flow co cache TikTok qua `ReupVideoCacheService`/TikWM.

### Point system

- Wallet/ledger chinh: `billing.token_wallets`, `billing.token_transactions`.
- Billing reservation moi hon: `billing.ai_image_billing_records`, `billing.ai_image_provider_attempts`, service `IAiImageBillingService`.
- Reserve:
  - khoa logical request bang advisory lock.
  - unique `logical_request_id`.
  - tru `balance`, tang `locked_balance`.
  - record status `reserved`.
- Complete thanh cong:
  - giam `locked_balance`.
  - tao `billing.token_transactions` type `debit`, reference `ai_image_render`.
  - billing record sang `completed`.
- Fail provider:
  - release reservation, cong lai balance, giam locked balance, status `released`.
- Reconciliation: `pending_reconciliation`, worker claim theo lock fields tren billing record.
- Point unit: khong duoc suy doan. Code hien tai co fallback `AiImageBilling:TodoXVndPerPoint = 10000` va SQL billing record default `todox_vnd_per_point = 10000`, nhung gia tri production phai lay tu config/DB hien hanh. `TokenSettingsService` doc `system.app_settings` cho cac key token cu, khong thay key chung "1 point = VND" rieng.
- Ty gia KIE theo yeu cau nghiep vu la `1 USD = 30000 VND`; can them config/billing snapshot rieng cho KIE, khong dung fallback YEScale 8000 VND/USD.

### Background jobs va concurrency

- Co HostedService DB worker, khong thay Hangfire/Quartz/RabbitMQ.
- Distributed locking dung DB row lock/advisory lock.
- Retry job qua `attempt_count`, `max_attempts`, `retry_after`, `ScheduleRetryAsync`.
- Rate limit hien co co `GoogleVertexRateLimiter`; YEScale client co retry HTTP 429, nhung KIE yeu cau provider khong tu queue HTTP 429. KIE can limiter rieng truoc submit: toi da 20 generation request/10 giay/account va cap concurrent khoang 100 task/account.

## 2. Khao sat frontend

- Framework: Blazor Server/Razor components + MudBlazor.
- Route render-job hien tai: `Components/Pages/RenderVideoJobs.razor` khai bao `@page "/render-job"`; menu co item `/render-video-jobs` va redirect logic ve `/render-jobs`.
- Modal detail/error JSON: `Components/Dialogs/SceneRenderErrorDetailDialog.razor` hien request/response/error JSON va copy.
- Upload components co the tai su dung pattern:
  - `Avatar/AvatarEditorForm.razor`
  - `Pages/AvatarBuilder.razor`
  - `Pages/AiCharacterEdit.razor`
  - `Pages/Profile.razor`
- JSON editor hien co chu yeu la `MudTextField` multi-line + parse/pretty JSON trong `RenderVideoJobs.razor`, `ActivityLogDetailDialog.razor`, `SceneRenderErrorDetailDialog.razor`. Chua thay component JSON editor rieng.
- Video preview:
  - inline `<video controls>` trong `RenderVideoJobs.razor`.
  - `ReferenceVideoPreviewDialog.razor` preview TikTok/YouTube embed/fallback.
  - CSS preview trong `wwwroot/css/todox-theme.css`.
- Toast/error UI: `ISnackbar`, `MudAlert`, `MudChip`.
- Polling UI: `RenderVideoJobs.razor` dung `PeriodicTimer` theo `VideoRenderOptions.PollIntervalSeconds`.
- Job history UI: scene image/video/final version history tren `RenderVideoJobs.razor`, event log cua project.
- Admin config UI: `Components/Pages/AiProviders.razor`.

Component nen tai su dung cho `dance-sell`:

- `IMediaFileService` upload/download media.
- Pattern upload tile tu `AvatarEditorForm`/`AvatarBuilder`.
- `ReferenceVideoRepository` + `ReferenceVideoPreviewDialog` cho TikTok URL da luu; can bo sung staging/download source video neu KIE yeu cau public MP4 URL.
- `SceneRenderErrorDetailDialog` hoac tach thanh dialog provider request/response chung.
- `PeriodicTimer` polling pattern tu `RenderVideoJobs.razor`.
- `IAiProviderService` admin/provider dropdown neu can chon provider/capability.

## 3. Khao sat database

| Bang | Cot can dung | Cot con thieu | Can bang moi? |
| --- | --- | --- | --- |
| `public.todox_ai_provider` | `provider_code`, `provider_name`, `provider_type`, `base_url`, `api_key_config_name`, `enabled`, `priority`, `config_json` | seed/provider KIE chua co; config rate-limit/account/callback base nen nam JSON/config | Khong bat buoc, can seed SQL sau Phase 0 |
| `public.todox_ai_provider_capability` | `capability_code`, `display_name`, `model_name`, `endpoint_path`, `unit_type`, `unit_cost_points`, `enabled`, `allow_user_select`, `config_json` | capability moi `dance_sell_motion_control` hoac `motion_control_video`; model `kling-2.6/motion-control`; endpoint submit/poll/cost snapshot | Khong bat buoc, can seed SQL sau Phase 0 |
| `public.todox_ai_provider_usage_log` | `provider_code`, `capability_code`, `feature_code`, `model_name`, `request_id`, `job_id`, `quantity`, `unit_cost_points`, `total_points`, `provider_raw_cost`, `status`, `metadata_json` | du dung neu metadata luu request/response/cost/taskId | Khong |
| `render.render_jobs` | `job_type`, `status`, `input_json`, `prompt_json`, `reference_json`, `output_json`, `provider_code`, `model_code`, `point_*`, `error_*` | status constraint SQL goc chua co `pending_reconciliation` trong V008, code co; can verify DB production da upgrade chua | Khong, co the them job type bang code |
| `render.render_job_events` | `event_type`, `level`, `message`, `data_json`, `provider_code`, `model_code` | du cho timeline/callback event | Khong |
| `media.media_files` | `tenant_id`, `customer_id`, `user_id`, `file_category`, `mime_type`, `file_size_bytes`, `storage_provider`, `object_key`, `file_url`, `public_url` | du cho product image, character image, source video upload, output video | Khong |
| `content.reference_videos` | `platform`, `source_url`, `normalized_url`, `external_video_id`, `thumbnail_url`, `raw_metadata`, `status` | chua co cot media cache/public MP4 URL ro rang trong repository doc duoc; dance-sell can staging video URL/asset id | Co the can bang cache rieng hoac them cot sau khi xac nhan |
| `billing.token_wallets` | `balance`, `locked_balance`, `wallet_scope`, `wallet_code`, `overdraft_limit`, `status` | du cho reserve/deduct/refund | Khong |
| `billing.token_transactions` | `transaction_type`, `amount`, `balance_before`, `balance_after`, `reference_type`, `reference_id`, `description` | reference_type hien service dang hard-code `ai_image_render`; dance-sell nen co `dance_sell_generation` neu mo rong service | Khong neu service billing chap nhan metadata; co the can enum/check constraint neu DB co |
| `billing.ai_image_billing_records` | `logical_request_id`, `provider_*`, `feature_code`, `requested_model`, `provider_task_id`, `*_cost_usd`, `exchange_rate_vnd_per_usd`, `todox_vnd_per_point`, `*_points`, `status`, `tariff_snapshot_json`, `metadata_json` | ten bang la `ai_image` nhung dang duoc dung cho scene video; co the tai su dung cho dance-sell de giam thay doi, hoac doi ten/tao billing generic o phase sau | Khong bat buoc |
| `billing.ai_image_provider_attempts` | attempt/cost/error/raw_usage_json | du cho KIE attempt neu dung billing service hien tai | Khong |
| `video_render.scene_video_versions` | provider task/result/cost fields cho scene | Khong phu hop neu dance-sell khong gan scene | Nen khong dung |
| `system.app_settings` | storage/settings/point defaults | can key KIE exchange rate/rate limit/pricing neu khong de trong provider config | Co the seed key sau Phase 0 |
| Bang dance-sell de xuat | input/output/result/task/callback/idempotency/job relation | Chua ton tai | Co, neu can history rieng ngoai render_jobs |

## 4. File se sua

Backend du kien:

- `TodoX.Web/Program.cs`: dang ky KIE client/service/handler/limiter.
- `TodoX.Web/Models/AiProviderModels.cs`: them capability code va quick default item cho Dance Sell neu dung admin provider UI hien co.
- `TodoX.Web/Services/Render/RenderJobModels.cs`: them `RenderJobTypes.DanceSell`.
- `TodoX.Web/Services/AiProviders/AiProviderRepository.cs`/`AiProviderService.cs`: chi sua neu can expose config/capability moi ngoai schema hien co.
- `TodoX.Web/Services/Media/MediaFileService.cs`: chi sua neu can method upload video tu `IBrowserFile`/staging URL rieng, tranh sua neu co the dung method hien tai.
- `TodoX.Web/Components/Layout/MainLayout.razor`: them menu route `dance-sell` khi feature duoc bat.
- `TodoX.Web/Components/Pages/AiProviders.razor`: co the tu dong hien capability moi neu catalog quick defaults duoc them.

Frontend du kien:

- `TodoX.Web/Components/Pages/DanceSell.razor`: page chinh route `/dance-sell`.
- `TodoX.Web/Components/Dialogs/SceneRenderErrorDetailDialog.razor`: co the tong quat hoa thanh provider JSON detail, hoac tao dialog moi de tranh anh huong scene.
- `TodoX.Web/wwwroot/css/todox-theme.css` hoac page CSS rieng: style preview/upload/history neu can.

Database/manual SQL du kien:

- Tao file manual SQL moi, vi AGENTS cam migration tu dong. Vi du `database/manual/kie-dance-sell/01_seed_kie_provider.sql`, `02_add_dance_sell_jobs.sql`, `03_verify.sql`.

## 5. File se tao

Backend de xuat theo convention repo:

- `TodoX.Web/Services/AiProviders/Kie/KieOptions.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieClient.cs`
- `TodoX.Web/Services/AiProviders/Kie/KiePayloadBuilder.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieTaskModels.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieRateLimiter.cs`
- `TodoX.Web/Services/DanceSell/DanceSellModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellService.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPricingService.cs`
- `TodoX.Web/Services/DanceSell/DanceSellQueueService.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRepository.cs`
- `TodoX.Web/Components/Pages/DanceSell.razor`
- `TodoX.Web/Components/Pages/DanceSell.razor.css`
- `TodoX.Web/Components/Dialogs/DanceSellJobDetailDialog.razor`

Neu callback HTTP API can public endpoint:

- Kiem tra repo co convention controller/minimal API truoc khi tao. Neu dung controller: `TodoX.Web/Controllers/KieCallbackController.cs`. Neu repo uu tien minimal endpoints: them extension map endpoint rieng, khong sua luong provider cu.

Tests de xuat:

- `TodoX.Web.Tests/KiePayloadBuilderTests.cs`
- `TodoX.Web.Tests/KieTaskStatusMapperTests.cs`
- `TodoX.Web.Tests/KieRateLimiterTests.cs`
- `TodoX.Web.Tests/DanceSellPricingServiceTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`

## 6. SQL du kien can tao

Khong tao SQL trong Phase 0. De xuat Phase 1:

- Seed KIE provider vao `public.todox_ai_provider`:
  - `provider_code = 'kie'` hoac `kie_task_video`
  - `provider_type = 'external_api'`
  - `base_url` lay tu config/docs KIE
  - `api_key_config_name` vi du `Kie:ApiKey`, khong luu secret.
  - `config_json`: rate limit, concurrent limit, callback enabled, upload endpoint.
- Seed capability:
  - `capability_code`: de xuat `dance_sell_motion_control` hoac `motion_control_video`.
  - `display_name`: `Nhảy bán hàng`.
  - `model_name`: `kling-2.6/motion-control`.
  - `endpoint_path`: `/api/v1/jobs/createTask`.
  - `unit_type`: `request`.
  - `unit_cost_points`: phai tinh sau khi xac nhan KIE USD price va point unit production.
- Bang moi de xuat `video_render.dance_sell_jobs` hoac schema rieng `dance_sell.dance_sell_jobs`:
  - `id`, `tenant_id`, `customer_id`, `user_id`, `render_job_id`, `logical_request_id`.
  - input media ids/urls: `product_media_id`, `character_media_id`, `source_video_media_id`, `tiktok_reference_video_id`, `source_video_url`.
  - params: `prompt`, `mode`, `character_orientation`.
  - provider fields: `provider_code`, `provider_capability_id`, `model_name`, `provider_task_id`, `provider_status`.
  - JSON: `request_json`, `submit_response_json`, `poll_response_json`, `callback_json`, `error_json`.
  - output: `preview_url`, `result_video_media_id`, `result_video_url`.
  - billing: `billing_logical_request_id`, `estimated_usd`, `actual_usd`, `charged_points`, `refunded_points`, `cost_source`.
  - audit/status: `status`, `error_code`, `error_message`, `submitted_at`, `completed_at`, `created_at`, `updated_at`.
- Optional idempotency/unique:
  - unique `logical_request_id`.
  - index `(customer_id, created_at desc)`.
  - index `(provider_task_id)`.
- Optional rate/concurrency table neu multi-instance:
  - `provider.provider_rate_locks` hoac dung Redis/DB advisory lock. Can quyet dinh theo infrastructure.

## 7. Contract KIE can chuan bi

Nguon tham khao public:

- Motion Control: `https://docs.kie.ai/market/kling/motion-control`
- Get Task Detail: `https://docs.kie.ai/market/common/get-task-detail`
- Upload File API: `https://docs.kie.ai/file-upload-api/upload-file`

Submit payload muc tieu:

```json
{
  "model": "kling-2.6/motion-control",
  "callBackUrl": "https://.../api/kie/callback",
  "input": {
    "prompt": "...",
    "input_urls": ["CHARACTER_OR_COMPOSITE_IMAGE_URL"],
    "video_urls": ["DANCE_SOURCE_VIDEO_URL"],
    "mode": "720p",
    "character_orientation": "image"
  }
}
```

Da xac nhan tu docs public:

- Submit endpoint: `POST /api/v1/jobs/createTask`.
- Poll endpoint: `GET /api/v1/jobs/recordInfo?taskId=...`.
- Submit response co `data.taskId`.
- Poll/detail response co `data.taskId`, `data.state`, `data.resultJson`, `data.failCode`, `data.failMsg`.
- Status docs public: `waiting`, `queuing`, `generating`, `success`, `fail`.
- Result docs public: `data.resultJson` la chuoi JSON, can parse de lay `resultUrls`.
- Auth dung Bearer token theo docs KIE.

Can xac nhan tu tai lieu/tai khoan KIE truoc Phase 1:

- Base URL production chinh xac.
- Callback payload contract va signature/security.
- Exact request constraints: file size, image/video duration, supported MIME, public URL lifetime, TikTok URL co duoc chap nhan truc tiep hay bat buoc video file URL.
- Exact `character_orientation` accepted values ngoai `"image"`.
- Mode accepted casing: docs payload la `"720p"`; can xac nhan `"1080p"`.
- Error body khi submit fail, quota fail, validation fail.
- HTTP 429 behavior chi tiet: docs/account rate-limit header co hay khong, Retry-After co hay khong.
- Pricing USD/request cho `kling-2.6/motion-control` theo mode 720p/1080p.

Status mapping de xuat:

- KIE `waiting`, `queuing` -> TodoX `queued`/UI waiting.
- KIE `generating` -> TodoX `rendering`.
- KIE `success` -> TodoX `completed`.
- KIE `fail` -> TodoX `failed` hoac `pending_reconciliation` neu billing da reserve nhung ket qua ambiguous.
- HTTP 429 submit -> khong goi provider self-queue; rate limiter/queue TodoX phai delay job truoc submit hoac retry job voi `retry_after`.

## 8. Luong submit/poll/callback de xuat

1. User vao `/dance-sell`, upload/nhap:
   - anh san pham;
   - anh nhan vat;
   - TikTok URL hoac video upload;
   - prompt;
   - mode `720p`/`1080p`;
   - `character_orientation`.
2. Service luu media input vao `media.media_files`.
3. Neu TikTok URL:
   - uu tien lien ket `content.reference_videos` neu da co;
   - neu KIE khong chap nhan TikTok URL truc tiep, can resolve/download/stage thanh public MP4 URL bang flow rieng. Khong tu suy doan.
4. Pricing service lay provider capability KIE, KIE USD price da verified, exchange rate 30000 VND/USD, point value production tu config/DB.
5. Reserve points bang billing service voi `logical_request_id` idempotent.
6. Enqueue `render.render_jobs` job type `dance_sell`.
7. Worker claim job, kiem tra limiter:
   - account window 20 submit/10 giay;
   - concurrent active task <= 100.
8. Build payload KIE, luu request JSON.
9. Submit `POST /api/v1/jobs/createTask`, lay `taskId`, luu submit response.
10. Poll `recordInfo` den terminal hoac dung callback neu da xac nhan contract.
11. Khi success:
    - parse `data.resultJson.resultUrls`;
    - download video ve storage qua `IMediaFileService.DownloadAndSaveBinaryAtObjectKeyAsync`;
    - update job/output/dance-sell row;
    - complete billing va log usage.
12. Khi fail:
    - luu failCode/failMsg/raw response;
    - release/refund reservation neu provider fail khong tinh tien;
    - neu khong ro KIE da tinh tien hay chua, mark pending reconciliation.
13. Callback:
    - endpoint idempotent theo `taskId`;
    - verify secret/signature neu KIE co;
    - update row/job event;
    - khong double complete billing neu poll da complete.

## 9. Luong tinh diem

- Khong hard-code point unit.
- Cong thuc de xuat sau khi xac nhan point value:
  - `provider_cost_vnd = provider_price_usd * 30000`.
  - `provider_cost_points = provider_cost_vnd / todox_vnd_per_point`.
  - `customer_charged_points` lay theo tariff admin/capability; co the bang provider cost points hoac co markup, nhung phai la config/data.
- Nguon can lay:
  - KIE price USD/request theo mode: tu docs/account KIE.
  - `todox_vnd_per_point`: tu config/DB production. Code fallback hien co la `AiImageBilling:TodoXVndPerPoint=10000`, SQL default billing record la 10000, nhung khong duoc coi la source of truth production neu chua verify.
  - exchange KIE: 30000 VND/USD theo yeu cau nghiep vu, nen luu snapshot tren billing record.
- Idempotency:
  - `logical_request_id` unique tren billing record.
  - callback/poll complete phai idempotent theo `taskId` va `logical_request_id`.

## 10. Luong queue/rate limit

- KIE khong tu queue 429. TodoX phai chu dong:
  - truoc submit, limiter kiem tra sliding/fixed window 20 request/10 giay/account.
  - neu vuot, khong submit; update job `retry_after` va de worker claim lai.
  - concurrent active tasks toi da khoang 100/account: dem task dang `submitted/generating` trong DB hoac limiter store.
- Multi-instance:
  - neu chi 1 instance co the memory limiter tam thoi.
  - production multi-instance can DB/Redis distributed limiter; neu dung DB thi can bang rate/concurrency rieng hoac advisory lock + event table.
- HTTP 429:
  - coi la transient provider throttle, mark retry job voi backoff.
  - khong loop retry ngay trong `KieClient` de tranh provider-side queue/hammering.

## 11. Rui ro ky thuat

- KIE contract public chua du de lock callback/security/pricing/file constraints.
- TikTok URL co the khong phai direct MP4; KIE co the yeu cau public downloadable video URL.
- Public URL local `/uploads` co the khong truy cap duoc tu KIE neu app chay sau firewall; can CDN/MinIO/public storage production.
- Point unit production chua xac minh; fallback 10000 trong code/SQL khong du lam gia kinh doanh.
- Billing service ten `AiImageBillingService` nhung dang duoc dung cho video; tai su dung duoc nhung naming gay nham lan.
- `render.render_jobs` SQL goc khong co `pending_reconciliation` trong check constraint, code co status nay. Can preflight DB truoc khi dung.
- KIE 429/concurrency can distributed control neu co nhieu worker instance.
- Callback va poll co the race; can idempotency cap row/billing.
- File download SSRF guard hien co ten method `ValidatePublicImageUri` nhung dung cho binary; logic ok, naming gay nham.
- Worktree hien tai dang co nhieu thay doi truoc task, bao gom file key; Phase sau can tach branch/commit sach truoc khi code.

## 12. Ke hoach test tung phase

Phase 0:

- Chi tao bao cao, khong production code.
- Validation: `git status`, doc docs/source, khong build/publish vi docs-only.

Phase 1 - provider skeleton va contract tests:

- Unit test `KiePayloadBuilder`: payload dung model, mode, URL arrays, callback URL.
- Unit test status mapper: `waiting/queuing/generating/success/fail`, unknown status.
- Unit test response parser: `data.taskId`, `data.resultJson` string -> `resultUrls`.
- Test 429: client tra exception transient, handler schedule retry, khong retry tight loop.

Phase 2 - storage/input:

- Test upload anh/video dung gioi han config.
- Test URL staging chan localhost/private IP.
- Test TikTok URL validation va fallback khi chua resolve duoc MP4.

Phase 3 - billing:

- Test tinh diem voi KIE exchange 30000 va point value config.
- Test reserve/complete/release idempotency.
- Test duplicate callback khong debit 2 lan.

Phase 4 - worker end-to-end:

- Fake KIE server: submit -> taskId, poll -> generating -> success.
- Fake fail: poll failCode/failMsg, refund/release dung.
- Rate limiter: 21 request trong 10 giay chi 20 submit, job con lai co `retry_after`.
- Concurrent cap: task thu 101 bi delay.

Phase 5 - frontend:

- bUnit/Playwright cho page `/dance-sell`: upload controls, mode select, prompt, submit disabled state, preview video, JSON tab, history.
- Test toast/error UI khi upload qua size, KIE fail, insufficient points.

Phase 6 - rollout:

- Manual SQL preflight/verify.
- `dotnet build`.
- Relevant test suite.
- Publish theo documented workflow, khong deploy/restart production neu chua duoc yeu cau.

## 13. Lenh da chay trong Phase 0

- `git status --short`: repo dang co nhieu thay doi san truoc task; khong revert/overwrite.
- `rg` va `Get-Content` doc cac file provider/render/storage/billing/frontend/database.
- Tham khao KIE docs public tai cac URL o muc contract.

## 14. Ket luan Phase 0

- Nen them KIE nhu provider/capability rieng va them pipeline `dance-sell` rieng dua tren `render.render_jobs`, `media.media_files`, `billing.ai_image_billing_records`, khong sua OpenRouter/YEScale path.
- Can xac minh them KIE pricing, callback security, file constraints, base URL/account limits truoc khi code production.
- Can xac minh gia tri `todox_vnd_per_point` production truoc khi tinh diem.
- Phase nay khong tao migration, khong tao SQL executable, khong publish/deploy.
