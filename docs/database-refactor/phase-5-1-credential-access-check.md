# Phase 5.1 Credential Access Check

Runtime credential resolver exists and active KIE/YEScale/OpenRouter/Vertex direct credential paths were moved to provider account credential resolution.

Completed changes:

- KIE no longer accepts `KieOptions.ApiKey` or `KIE_API_KEY` fallback in `KieClient`; submit/poll require `ResolvedAiProviderCredential`.
- YEScale no longer accepts `YEScaleOptions.AccessKey`; task submit/status require an account-resolved API key.
- OpenRouter no longer falls back to `OpenRouter:ApiKey`; callers must pass an account-resolved API key.
- Vertex no longer reads `Vertex:ServiceAccountKeyPath`; service-account JSON is resolved via provider account credential references.
- Source scan over `TodoX.Web\Services`, `TodoX.Web\Program.cs`, `TodoX.Web\appsettings.json`, and tests found no remaining direct production credential path for these keys.

YEScale MCP check:

- `yescale_get_current_user` returned `YEScale request failed: fetch failed`.
- No YEScale model metadata, pricing, endpoint, or limit values were changed from unverified memory.

No secret values are included in this report.
