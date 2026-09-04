# Project instructions

## Before editing

- Read README.md and relevant architecture documentation.
- Run git status before making changes.
- Do not overwrite or revert existing user changes.
- Trace the complete execution path before fixing a bug.
- Reproduce the issue before modifying code.

## Scope

- Make the smallest change that fixes the root cause.
- Do not refactor unrelated code.
- Do not change public APIs without explicit approval.
- Do not hard-code configuration values, IDs, model names, URLs, or credentials.
- Do not modify existing migrations after they have been deployed.
- Never commit secrets or environment-specific values.

## Protected subsystem rule

COMPLETED SUBSYSTEMS ARE FROZEN BY DEFAULT.

- Only modify functionality explicitly requested by the user.
- Never refactor a completed subsystem merely because nearby code is being edited.
- Never repair unrelated failing tests by changing unrelated production behavior.
- Treat IMAGE GENERATION, VIDEO GENERATION, VOICE / AUDIO, and POINT / BILLING as separately protected contracts.
- A task in one protected area must not change the behavior of another protected area.
- Provider payload contracts are frozen unless the user explicitly asks to change them.
- Before coding, list the requested scope, allowed files/behaviors, and frozen areas.
- After coding, report changed files, changed behaviors, and explicitly untouched protected areas.
- If a necessary fix crosses into a frozen area, stop and request approval.
- Prefer minimum-diff fixes over architecture cleanup.

## Validation

Before declaring completion:

- Run the project build.
- Run linting.
- Run relevant tests.
- Add a regression test for the bug.
- Report every changed file.
- Report exact commands and their results.
- State clearly if any validation could not be completed.

## Git safety

- Do not use git reset --hard.
- Do not use git clean.
- Do not force push.
- Do not commit unless explicitly requested.
- Do not revert changes that existed before the current task.

## Database safety

- Do not create, modify, or execute database migrations unless explicitly requested.
- Never apply SQL or schema changes directly to any database.
- If a database change is necessary, create a standalone SQL script and provide it to the user for manual execution.
- Clearly state when the requested change does not require a database update.

## Build and publish

- After validation succeeds, build and publish the application using the repository's documented workflow.
- Do not deploy to a server or restart production services unless explicitly requested.
- Report the publish command, output directory, and result.
- Default publish root: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish`.
- For the dashboard application, publish to `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard` unless the user explicitly requests another subfolder.

## YEScale provider workflow

- YEScale MCP is the authoritative live source for YEScale model IDs, capabilities, modalities, API compatibility, parameters, pricing, limits, deprecation status, and provider documentation.
- Before adding or modifying a YEScale model/provider, query the YEScale MCP tools first.
- Do not invent or guess YEScale model IDs, endpoints, request parameters, pricing, rate limits, or supported modalities.
- Do not rely solely on model information remembered by the AI.
- If YEScale MCP is unavailable, stop the provider-specific implementation and report the blocker.
- Never put YESCALE_ACCESS_KEY or any YEScale credential in source code, AGENTS.md, prompts, logs, tests, screenshots, appsettings.json, or Git.
- Read credentials only from environment variables, user secrets, or the production secret store.
- Treat model IDs as configuration/data, not hard-coded constants.
- Map every model to an existing TodoX capability based on verified input/output modalities.
- Before completion, report which YEScale MCP tools were called and which model metadata was verified.
- Run tests, build, and publish according to this AGENTS.md.
- Do not create or run migrations. If a database change is necessary, provide a standalone SQL file for manual execution.
