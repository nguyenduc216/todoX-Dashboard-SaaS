# 79AI MCP Discovery Report

Date: 2026-07-22

Branch: `refactor/ai-core-reset`

## 1. Summary

79AI has not been integrated in this repository yet. A source scan for `79AI`, `79ai`, and related strings returned no runtime references.

The public page `https://79ai.net/mcp#connect` is reachable, but it is a client-side SPA route. The public bundle exposes enough information to identify a 79AI/Gommo MCP management API at `https://api.gommo.net/api/v2/mcp`, but that API requires a valid `access_token`. No 79AI/Gommo/MCP token is configured in the current environment.

Per the required rule "do not guess endpoint, MCP tool name, request schema or response schema", production implementation must stop here until a valid 79AI/Gommo credential or documented MCP server endpoint is provided.

## 2. Source Findings

Current reusable TodoX runtime pieces:

- Provider registry/account/credential/lease: `public.todox_ai_provider*` and `IAiProviderAccountRepository`.
- Credential resolution: `IAiProviderCredentialResolver`, already designed to resolve provider account credentials without logging secret values.
- Generic provider task abstraction: `IAiProviderTaskClient`, `AiProviderTaskSubmitRequest`, `AiProviderTaskStatusRequest`, `AiProviderTaskResult`.
- Render job core: `render.render_jobs`, `RenderJobService`, `RenderJobWorker`, `IRenderJobHandler`.
- Completion/idempotency: `IAiRenderCompletionService` writes render job status, events, artifacts, usage and billing.
- Billing: `IAiBillingService` and `AiBillingRepository` reserve, complete, release and reconcile points.
- Existing video async pattern: `SceneVideoWorkerHandler` submits a provider task, stores provider task ID, polls status, downloads the final video, saves it through `IMediaFileService`, then completes billing and usage.
- Storage/download: `IMediaFileService.DownloadAndSaveBinaryAtObjectKeyAsync` can persist remote video output to TodoX storage and `media.media_files`.

Important current constraints:

- Provider account lease must be claimed before any paid/external provider submit.
- Credentials must be read through provider account resolver, not direct appsettings secret values.
- Render job output should only be marked successful after the output video has been downloaded and saved locally.
- The existing YEScale provider must not be modified for 79AI discovery.

## 3. 79AI Public Discovery

Checked sources:

- `https://79ai.net/mcp#connect`: HTTP 200, returns SPA HTML.
- `https://79ai.net/assets/index-270fb8b23ec29b72.js`: public SPA bundle.
- `https://79ai.net/manifest.json`: identifies Auto AI/79AI as a creative platform for video, image and voiceover.

Findings from the public bundle:

- The UI calls `https://api.gommo.net/api/v2/mcp`.
- The request is `POST` with `application/x-www-form-urlencoded;charset=UTF-8`.
- The form includes `action` and `access_token`.
- The UI error text refers to `gommo-token`.
- This appears to be 79AI/Gommo's MCP management API, not necessarily the final MCP JSON-RPC transport URL.

## 4. MCP Handshake Attempts

Attempted JSON-RPC `initialize`:

| URL | Result |
| --- | --- |
| `https://79ai.net/mcp` | HTTP 200 HTML SPA, not MCP JSON-RPC. |
| `https://api.gommo.net/mcp` | HTTP 404. |
| `https://api.gommo.net/api/v2/mcp` | HTTP 200 JSON application error: `The access_token not work. Please check again`. |

Attempted likely management API actions without a token:

- `server_list`
- `server_tools`
- `tools_list`
- `tool_list`
- `list_tools`
- `discover`
- `discovery`
- `server_preview`
- `server_get`

All returned:

```json
{
  "error": 100,
  "message": "The access_token not work. Please check again",
  "runtime": 0
}
```

No token value was printed or saved.

Redacted discovery evidence file:

- `docs/database-refactor/79ai-mcp-discovery-redacted.json`

## 5. MCP URL, Transport And Authentication

Verified:

- Public SPA route: `https://79ai.net/mcp#connect`.
- Public MCP management API seen in bundle: `https://api.gommo.net/api/v2/mcp`.
- Management API body format: form URL encoded.
- Required authentication: `access_token` in the request body for the management API.

Not verified:

- Actual MCP server URL to use from TodoX.
- Transport: Streamable HTTP, SSE, stdio or other.
- JSON-RPC MCP protocol version supported by 79AI.
- Session ID behavior.
- `initialize`, `notifications/initialized`, `tools/list`, `tools/call`, `resources/list`, `prompts/list` behavior.
- Any video tool/model schema.

Blocker:

- A valid 79AI/Gommo access token or official MCP server configuration is required.

## 6. Video Tools And Models

No MCP `tools/list` response was obtained, so no tool/model can be truthfully reported as supported.

| Tool/model | T2V | I2V | V2V | Motion Control | Input required | Duration | Aspect ratio | Resolution | Audio | Price/credits |
| ---------- | --- | --- | --- | -------------- | -------------- | -------- | ------------ | ---------- | ----- | ------------- |
| Not verified | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Not available from MCP |

Do not create a `79AI` video submit adapter until `tools/list` and the relevant `inputSchema` are captured from a real MCP session.

## 7. Proposed TodoX Integration Design After Discovery

Suggested provider code: `seventy_nine_ai` or `79ai`.

Use `seventy_nine_ai` for C# class/config names because .NET identifiers and section names are cleaner when they do not start with a digit.

Potential files after real MCP discovery:

- `TodoX.Web/Services/AiProviders/SeventyNineAI/SeventyNineAiOptions.cs`
- `TodoX.Web/Services/AiProviders/SeventyNineAI/SeventyNineAiMcpClient.cs`
- `TodoX.Web/Services/AiProviders/SeventyNineAI/SeventyNineAiVideoTaskClient.cs`
- `TodoX.Web/Services/AiProviders/SeventyNineAI/SeventyNineAiDiscoveryModels.cs`
- `TodoX.Web/Services/AiProviders/SeventyNineAI/SeventyNineAiStatusMapper.cs`
- `TodoX.Web/Services/VideoRender/SeventyNineAiVideoWorkerHandler.cs`, only if the generic `IAiProviderTaskClient` path is not enough.
- `scripts/diagnostics/Discover-79AiMcp.ps1`
- `TodoX.Web.Tests/SeventyNineAiMcpClientTests.cs`
- `TodoX.Web.Tests/SeventyNineAiVideoTaskClientTests.cs`

Preferred route:

1. Add a diagnostic discovery script first.
2. Store redacted `tools/list`, `resources/list`, `prompts/list` JSON.
3. Map only verified video tools into TodoX provider capabilities.
4. Implement `IAiProviderTaskClient` for `seventy_nine_ai`.
5. Reuse render job, provider account lease, generic billing and completion services.

## 8. Submit To Poll To Download Flow

Target flow after schema is verified:

1. Create/claim `render.render_jobs`.
2. Validate T2V/I2V/V2V request.
3. Resolve provider/capability/model from DB.
4. Claim provider account lease with `FOR UPDATE SKIP LOCKED`.
5. Resolve account credential through `IAiProviderCredentialResolver`.
6. Reserve billing by `logical_request_id`.
7. Call verified MCP tool for submit.
8. Store provider task ID immediately.
9. Poll verified status/result tool or handle callback if MCP exposes one.
10. On terminal success, get output URL.
11. Download immediately using safe download rules.
12. Save to TodoX storage/media table.
13. Complete render job through shared completion service.
14. Finalize usage and billing exactly once.
15. Release provider account lease.

If the provider completes but download fails:

- Keep provider task ID and redacted output URL.
- Mark job as pending reconciliation or download retry.
- Retry download only.
- Do not resubmit the generation task.
- Do not deduct points twice.

## 9. Database Impact

No SQL was created or executed for this report.

Expected reuse:

- `public.todox_ai_provider`
- `public.todox_ai_provider_capability`
- `public.todox_ai_provider_account`
- `public.todox_ai_provider_account_credential`
- `public.todox_ai_provider_account_lease`
- `render.render_jobs`
- `render.render_job_inputs`
- `render.render_artifacts`
- `render.render_job_events`
- `public.todox_ai_provider_usage_log`
- `billing.ai_billing_records`
- `billing.ai_provider_attempts`
- `media.media_files`

Potential standalone SQL later:

- Seed provider `seventy_nine_ai`.
- Seed provider accounts with credential reference only.
- Seed verified T2V/I2V/V2V/Motion Control capabilities after MCP discovery.

Do not run migrations automatically.

## 10. Environment Variables Needed Later

Recommended naming:

```powershell
setx AiProviders__SeventyNineAI__Enabled "true" /M
setx AiProviders__SeventyNineAI__McpUrl "<verified-mcp-url>" /M
setx SEVENTY_NINE_AI_ACCESS_TOKEN "<real-token>" /M
```

Provider account credential rows should reference `SEVENTY_NINE_AI_ACCESS_TOKEN`; the secret value must not be stored in appsettings, database reports, logs or source.

Additional config after real schema:

- `AiProviders__SeventyNineAI__PollIntervalSeconds`
- `AiProviders__SeventyNineAI__RequestTimeoutSeconds`
- `AiProviders__SeventyNineAI__DownloadTimeoutSeconds`
- `AiProviders__SeventyNineAI__MaxDownloadBytes`
- `AiProviders__SeventyNineAI__DefaultVideoModel`

## 11. PowerShell Discovery Command Draft

This command is intentionally a draft until the actual MCP server URL and auth scheme are verified:

```powershell
$env:SEVENTY_NINE_AI_ACCESS_TOKEN = "<real-token>"
$env:SEVENTY_NINE_AI_MCP_URL = "<verified-mcp-url>"

# After token is available, run the diagnostic script that performs:
# initialize -> notifications/initialized -> tools/list -> resources/list -> prompts/list
# and writes redacted JSON to docs/database-refactor/79ai-mcp-discovery-redacted.json.
```

## 12. Smoke Test Draft

Do not run paid smoke yet.

After real MCP discovery:

```powershell
dotnet test TodoX.Dashboard.sln -c Release --filter "FullyQualifiedName~SeventyNineAi"
```

Paid smoke should be explicit and minimal:

- T2V: shortest supported duration, lowest verified resolution, short prompt.
- I2V: one public image URL, shortest supported duration.
- V2V/Motion Control: only if verified by MCP tools/list and approved.

## 13. Build And Test

No production code was changed in this report phase.

Validation performed:

- `git status --short --branch`
- Source scan for `79AI` references.
- Public page/API discovery.

Build/test were not run because this task intentionally stopped before implementation due to missing MCP credential.

## 14. Unverified Items

- Actual MCP server URL.
- Transport and session behavior.
- Auth header/body field for final MCP JSON-RPC endpoint.
- Tool names and input schemas.
- Video model names.
- Status mapping.
- Result URL field.
- Price/credits.
- URL expiration behavior.
- Webhook/callback support.
- Rate limits and retry guidance.

## 15. Required Input From User

Please provide one of the following:

1. A valid 79AI/Gommo access token in an environment variable, preferably `SEVENTY_NINE_AI_ACCESS_TOKEN`.
2. Official MCP connection JSON/config from `https://79ai.net/mcp#connect`.
3. A screenshot or exported config from the 79AI MCP connect page showing the MCP server URL, transport and auth requirements, with secrets redacted.

After that, the next safe step is MCP discovery only, not production integration.

## 16. IIS Deployment Checklist Later

- Add environment variables at machine/app-pool scope.
- Restart the IIS app pool after setting variables.
- Verify diagnostics endpoint/script can list tools without printing token.
- Verify provider account credential reference resolves.
- Run non-paid discovery tests.
- Run paid smoke only after explicit approval.

## 17. Rollback Checklist Later

- Disable provider `seventy_nine_ai` in provider table or admin UI.
- Disable related provider accounts.
- Remove or rotate `SEVENTY_NINE_AI_ACCESS_TOKEN`.
- Stop pending render jobs for provider code `seventy_nine_ai`.
- Keep downloaded local artifacts; do not depend on expiring 79AI URLs.
