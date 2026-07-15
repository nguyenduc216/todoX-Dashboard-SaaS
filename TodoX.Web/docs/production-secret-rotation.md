# Production Secret Rotation

Sensitive source configuration was found in tracked `appsettings.json` and replaced with empty placeholders.

Keys that must be supplied via environment variables, user secrets, or production secret store:

- `ConnectionStrings__TodoXSaaS`
- `ConnectionStrings__TodoXAutomation`
- `TodoX__SeedAdminUsername`
- `TodoX__SeedAdminPassword`
- `TodoX__SeedRootUsername`
- `Vertex__ServiceAccountKeyPath` or a secret-store equivalent for Vertex credentials
- `AiProviders__YEScale__Enabled`
- `AiProviders__YEScale__AccessKey`
- `AiProviders__YEScale__BaseUrl`
- Any existing Facebook/OpenRouter/Google credentials used by the deployment

Operational tasks outside this code change:

1. Rotate database passwords and any credentials previously committed.
2. Rotate seed/admin passwords if they were ever used outside local development.
3. Rotate any Vertex service account key referenced by source or build artifacts.
4. Do not rewrite Git history as part of app deployment; handle history cleanup as a separate security operation.
5. Rebuild and republish after production secret store is configured.
