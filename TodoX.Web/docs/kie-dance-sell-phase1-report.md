# KIE Dance Sell Phase 1.1 Handover Report

Ngay lap: 2026-07-21

Pham vi: hoan tat cac blocker Phase 1.1 cho KIE Provider Core va Dance Sell runtime security/tests. Khong bat dau Phase 2, khong lam UI Dance Sell hoan chinh, khong bat billing production, khong chay SQL production, khong deploy/restart IIS.

## 1. Commit

- Commit dau vao duoc yeu cau kiem tra: `d1a6ddc7c0839855fc32f93b06723804b9a1500e`
- Commit runtime blocker da co: `0a97f6bca59fccaf716c6f11d681e609636c70f7`
- Commit bao cao/hardening bo sung: commit chua file bao cao nay; SHA cuoi duoc bao cao trong phan ban giao.

Worktree sau commit con 1 thay doi co san ngoai pham vi:

- `keys/todox-vertex-sa.json`

File nay khong duoc stage/commit.

## 2. File da sua

- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieClient.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieOptions.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRepository.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase1Endpoints.cs`
- `TodoX.Web.Tests/KieClientTests.cs`

## 3. File moi

- `TodoX.Web/Services/DanceSell/DanceSellCompletionService.cs`
- `TodoX.Web.Tests/DanceSellRepositoryTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/DanceSellCallbackTests.cs`
- `TodoX.Web/docs/kie-dance-sell-phase1-report.md`

## 4. SQL manual da ra soat

Da ra soat cac file:

- `database/manual/kie-dance-sell/01_seed_kie_provider.sql`
- `database/manual/kie-dance-sell/02_create_dance_sell_schema.sql`
- `database/manual/kie-dance-sell/03_verify_kie_phase1.sql`

Ket qua ra soat:

- `provider_code = 'kie'`
- `capability_code = 'motion_control_video'`
- `model_name = 'kling-2.6/motion-control'`
- Metadata feature co `dance_sell`
- SQL khong chua API key that; chi dung `api_key_config_name = 'KIE_API_KEY'`
- Provider va capability disabled mac dinh
- `unit_cost_points = 0` placeholder Phase 1
- Bang `dance_sell.dance_sell_jobs` co cac cot repository dang dung
- Co unique `logical_request_id`
- Co unique index nullable cho `provider_task_id`
- Co index `status,next_poll_at`
- Co index `customer_id,created_at`
- Co FK `render_job_id`
- JSONB defaults co tren `request_json`
- Status check gom `queued`, `submitted`, `rendering`, `completed`, `failed`, `timeout`

Khong auto-run SQL, khong tao migration, khong them startup migration.

## 5. Loi UpdateCompletedAsync

Da sua `DanceSellRepository.UpdateCompletedAsync`:

- Bo parameter `@status` khong ton tai
- Set `completed_at=COALESCE(completed_at, now())`
- Giu `status='completed'`
- Luu `provider_status`
- Luu `poll_response_json`
- Luu `result_video_url=COALESCE(result_video_url, @resultVideoUrl)`
- Cap nhat `last_polled_at`, `completed_at`, `updated_at`
- Khong update row terminal: `completed`, `failed`, `timeout`
- Method tra `bool` theo rows affected de phuc vu idempotency

## 6. Callback Security

Da sua `DanceSellPhase1Endpoints`:

- Neu `KIE_CALLBACK_SECRET` rong/chua cau hinh: tra HTTP `503`
- Khong parse body khi secret chua cau hinh
- Khong update job khi secret chua cau hinh
- Neu secret sai/thieu: tra HTTP `401`
- Neu secret dung: tiep tuc parse callback va xu ly
- Uu tien header `X-KIE-CALLBACK-SECRET`; query `secret` van duoc giu lam fallback compatibility
- Khong log secret, khong tra secret trong response

Polling van hoat dong binh thuong khi callback khong duoc cau hinh.

## 7. Timeout Mapping

Da sua `KieClient`:

- Caller cancellation duoc propagate bang `OperationCanceledException`
- Internal timeout tu `HttpTimeoutSeconds` duoc map thanh `KieProviderException`
- Internal timeout co:
  - `ErrorCode = KIE_PROVIDER_UNAVAILABLE`
  - `IsTransient = true`
  - Message: `KIE request timed out.`
- Ap dung cho ca submit va poll content read/send path

## 8. Completion Service Chung

Da tao `IDanceSellCompletionService` va `DanceSellCompletionService`.

Poll va callback success cung dung service nay de:

- Update Dance Sell job
- Update render job status completed
- Add render event `KIE_TASK_COMPLETED`
- Log provider usage completed
- Luu metadata phase `phase1_no_billing`

Poll va callback failure cung dung service nay de:

- Update Dance Sell job failed/timeout
- Update render job status failed
- Add render event `KIE_TASK_FAILED`
- Log provider usage failed
- Luu metadata phase `phase1_no_billing`

## 9. Idempotency Callback/Poll

Da tang idempotency cho success/failure completion:

- Repository atomic conditional update bang `WHERE status NOT IN ('completed','failed','timeout')`
- Completion service chi add event/log usage khi `UpdateCompletedAsync` tra rows affected > 0
- Failure completion service chi add event/log usage khi `UpdateFailedAsync` tra rows affected > 0
- Duplicate callback success khong duplicate event/log
- Poll success sau callback khong duplicate event/log
- Duplicate callback failed khong duplicate event/log
- Completed row khong bi ghi de thanh terminal moi trong success path
- Failed/timeout row khong bi ghi de thanh completed neu success den sau

## 10. CustomerId Usage Log

Da sua usage log Dance Sell:

- Khong con `CustomerId = null`
- Dung customer cua `danceJob.CustomerId`
- Vi `AiProviderUsageLog.CustomerId` la `long?`, service dung convention quy doi Guid -> non-negative bigint da co trong codebase
- Log giu:
  - `ProviderCode = kie`
  - `CapabilityCode = motion_control_video`
  - `FeatureCode = dance_sell`
  - `RequestId = danceJob.LogicalRequestId`
  - `TotalPoints = 0`
  - `phase = phase1_no_billing`

## 11. Tests da them/cap nhat

File tests:

- `DanceSellRepositoryTests`
- `DanceSellRenderHandlerTests`
- `DanceSellCallbackTests`
- `KieClientTests`

Coverage da them:

- `UpdateCompletedAsync` khong con `@status`
- `completed_at=COALESCE(completed_at, now())`
- Callback secret chua cau hinh
- Callback thieu secret
- Callback secret sai
- Callback secret dung
- Callback query fallback
- Internal timeout -> transient `KIE_PROVIDER_UNAVAILABLE`
- Caller cancellation -> propagate cancel
- Retry-After delta
- Retry-After date
- Duplicate callback success khong duplicate render event/usage
- Duplicate callback failed khong duplicate render event/usage
- Poll after callback khong duplicate completion
- First terminal success update dance job/render job/event/usage once
- First terminal failure update dance job/render job/event/usage once
- Usage log lay customer tu Dance Sell job

Ghi chu: cac repository tests hien la regression test tren SQL constant, khong ket noi DB that. Khong chay SQL production.

## 12. Validation

Lenh da chay:

```powershell
dotnet build ..\TodoX.Dashboard.sln
```

Ket qua:

- Restore: all projects up-to-date
- Build succeeded
- `0 Warning(s), 0 Error(s)`
- Time elapsed: `00:00:02.29`

Lenh da chay:

```powershell
dotnet test ..\TodoX.Dashboard.sln
```

Ket qua:

- Test project: `TodoX.Web.Tests.dll (net10.0)`
- Failed: `0`
- Passed: `211`
- Skipped: `1`
- Total: `212`
- Duration: `5 s`
- Skipped existing test: `SceneImageRenderServiceTests.Rerender_Throws_WhenResolvedProviderIsNotRoutedImageProvider`

Lenh lint/check da chay:

```powershell
dotnet format ..\TodoX.Dashboard.sln --verify-no-changes
```

Ket qua:

- Failed do whitespace co san o cac file ngoai pham vi thay doi, vi du:
  - `Services/AccountRepository.cs`
  - `Services/AuditRepository.cs`
  - `Services/CatalogAdminRepository.cs`
  - `Services/Profile/ChibiAvatarService.Generate.cs`
  - `Services/Settings/PromptTemplateRepository.cs`
  - `Services/Settings/SettingsApiRepository.cs`
  - `Services/SocialPageRepository.cs`
  - `Services/WalletService.cs`
- Khong sua lan sang cac file unrelated.

Lenh publish artifact da chay truoc luot hardening bo sung:

```powershell
dotnet publish ..\TodoX.Web\TodoX.Web.csproj -c Release -o ..\artifacts\publish\kie-dance-sell-phase1-blockers
```

Ket qua:

- Publish succeeded
- Output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\kie-dance-sell-phase1-blockers\`
- Khong deploy, khong restart IIS.
- Theo yeu cau Phase 1.1 trong file moi, khong chay publish/deploy them trong luot hardening nay.

Lenh test project truoc khi chay solution day du:

```powershell
dotnet build ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --no-restore
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --no-build
```

Ket qua:

- Build succeeded
- Test failed: `0`
- Test passed: `211`
- Test skipped: `1`
- Test total: `212`

## 13. Rate Limit va Concurrency

Trang thai Phase 1:

- `KIE_RATE_LIMIT_REQUESTS_PER_10S`: da enforce bang in-memory limiter single-instance
- `KIE_MAX_CONCURRENT_TASKS`: da co option/config placeholder, chua enforce trong Phase 1
- Distributed limiter: chua co
- Concurrent task cap khoang 100/account: chua enforce

Khong duoc tuyen bo Phase 1 da kiem soat day du rate limit production. Multi-instance/distributed limiter va concurrent task cap can chuyen sang phase hardening sau.

## 14. Han che con lai

- Pricing KIE chua xac nhan
- Billing production chua bat
- Khong tru diem that trong Phase 1
- `KIE_MAX_CONCURRENT_TASKS` chua enforce
- Distributed limiter chua co
- Callback signature/security contract chinh thuc cua KIE chua xac nhan; hien tai dung shared secret header/query
- UI Dance Sell hoan chinh chua trien khai
- Composite anh san pham chua trien khai
- Phase 2 chua trien khai

## 15. Acceptance Summary

Da dat:

- `UpdateCompletedAsync` khong con `@status`
- `completed_at` set dung bang `COALESCE(completed_at, now())`
- Callback secret rong khong duoc authorize
- Internal timeout map dung transient provider error
- Caller cancellation duoc propagate
- Poll/callback success dung completion logic chung
- Poll/callback failure dung completion logic chung
- Success/failure completion idempotent
- CustomerId usage log dung theo Dance Sell job
- Repository tests co
- Handler/completion tests co
- Callback tests co
- KieClient timeout tests co
- SQL manual da ra soat
- `dotnet build` pass
- `dotnet test` pass
- Khong tru diem that
- Khong thay doi OpenRouter/YEScale/scene pipeline
- Co commit SHA moi

Can theo doi neu reviewer yeu cau acceptance nghiem ngat hon:

- Bo sung integration tests DB that cho repository thay vi SQL text regression
