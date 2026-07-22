# Dance Sell hardening result

Ngay tao: 2026-07-22

## Git

- Branch: `main`
- Base commit truoc task: `dce80831440823dfbd5967dfd7df87711633ca3b`
- Commit SHA moi: `PENDING`
- Push result: `PENDING`

## Thay doi da thuc hien

### KIE image provider

- `GENERATE_REFERENCE` khong con chay ImageSharp khi route la KIE/provider ben ngoai.
- Them `IDanceSellReferenceProvider`, `IDanceSellReferenceProviderFactory`, va adapter `KieDanceSellReferenceProvider`.
- KIE reference route `kie / gpt-image-2-image-to-image` submit provider task that qua `KieClient`.
- Payload da xac minh theo KIE docs:
  - Submit: `POST /api/v1/jobs/createTask`
  - Poll/detail: `/api/v1/jobs/recordInfo`
  - Payload image-to-image: `model`, `callBackUrl`, `input.prompt`, `input.input_urls`, `input.aspect_ratio`
  - Ket qua: `resultJson.resultUrls`
  - Usage: `creditsConsumed`
- Gioi han can xac nhan bang account/tai lieu KIE thuc te: max image count, exact balance endpoint, va callback schema day du cho image job.

### Direct reference

- Upload direct reference chi cap nhat business job/media.
- Khong tao provider operation gia voi `operation_type=output_stage` gan sai motion provider.
- Direct reference asset duoc ghi khi motion operation duoc tao.

### Capability va validation

- Mode/orientation duoc validate theo provider route config thay vi dung chung `KieOptions`.
- Them endpoint `GET /api/dance-sell/providers/{routeId}/capability`.
- Backend reload/validate capability theo route da chon.

### Attempt va operation log

- Them `GetNextAttemptNoAsync(jobId, operationType)`.
- Motion/reference attempt dua tren max attempt cua operation type, khong dua tren `poll_count`.
- Render handler dung operation id tu queue input de submit/poll vao operation hien co.
- Parser KIE ho tro `resultJson` la JSON string hoac object, va `creditsConsumed` number/string/null.
- Usage/cost lay tu provider response khi complete, khong hard-code sample credits.

### Billing va balance

- Cost estimator doc pricing tu route `config_json` truoc, sau do moi fallback config.
- Ho tro pricing fields: pricing unit, usage, provider unit price, provider cost, exchange rate, VND, markup, TodoX VND/point, estimated points, rounding rule, pricing source.
- Khi billing chua bat that, charge/refund/retry khong tra success gia; service nem `DANCE_SELL_BILLING_DISABLED`.
- KIE balance van manual mode vi chua xac minh duoc endpoint balance chinh thuc. Khong doan endpoint.

### SQL manual

- Tao `database/manual/ai-operation-logs/10_harden_ai_operation_logs.sql`
  - Them bang audit `public.todox_ai_operation_billing_transactions`.
  - Them/check constraints cho operation type, status, billing/refund status, usage unit, ledger tx type, billing tx type.
- Tao `database/manual/ai-operation-logs/11_verify_runtime_contract.sql`
  - Verify tables, columns, route seeds, va constraints runtime.
- Cap nhat `database/manual/ai-operation-logs/README.md`.
- Khong tao migration, khong chay SQL vao database.

## Files changed

- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieClient.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieResponseParser.cs`
- `TodoX.Web/Services/AiProviders/Kie/KieTaskModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs`
- `TodoX.Web/Services/DanceSell/DanceSellModels.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Endpoints.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web.Tests/DanceSellAiOperationsTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `database/manual/ai-operation-logs/README.md`

## Files created

- `database/manual/ai-operation-logs/10_harden_ai_operation_logs.sql`
- `database/manual/ai-operation-logs/11_verify_runtime_contract.sql`
- `docs/dance-sell-hardening-result.md`

## Validation

- `dotnet restore TodoX.Dashboard.sln`: passed, all projects up-to-date.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build`: passed, 231 passed, 1 skipped, 0 failed, total 232.
- `git diff --check`: passed; only Git line-ending warnings on Windows.
- `dotnet format TodoX.Dashboard.sln whitespace --verify-no-changes --include ...`: passed for changed code/test files.
- Full `dotnet format TodoX.Dashboard.sln --verify-no-changes`: failed on pre-existing whitespace in unrelated files, including AccountRepository, AuditRepository, CatalogAdminRepository, PromptTemplateRepository, SettingsApiRepository, SocialPageRepository, WalletService.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/dance-sell-hardening`: passed.
- Publish output: `artifacts/publish/dance-sell-hardening`, 334 files.

## Limitations

- Reference image operation submit is provider-backed, but full async reference polling/callback/staging completion still needs a dedicated background flow in the next phase.
- KIE balance endpoint remains unimplemented/manual because no official contract was confirmed.
- Billing is guarded as disabled unless real wallet policy/config is enabled; no fake charge/refund success is returned.
- Unrelated dirty secret-like file `TodoX.Web/keys/todox-vertex-sa.json` existed before final staging and is excluded from commit.
