# TodoX.Landing - project instructions

Landing site for todox.vn: an ASP.NET Core minimal app serving static pages from `wwwroot`.
These rules cover `TodoX.Landing`. `TodoX.Web` (dashboard) has its own `TodoX.Web/AGENTS.md`.

## Before editing

- Read `README.md` and run `git status`.
- Do not overwrite or revert existing user changes.
- Make the smallest change that fixes the root cause. Do not refactor unrelated code.
- Do not change behavior in `TodoX.Web` unless the task asks for it.

## Landing conventions

- Pages are static HTML. Standalone pages (`/bat-dong-san`, `/video-ngan`) need a route in `Program.cs` and an entry in `wwwroot/sitemap.xml`.
- Static files are cached for 7 days: bump the `?v=` query in the HTML whenever a CSS or JS file changes.
- Render data from the API with `textContent`, not `innerHTML`.
- Public API endpoints must degrade safely when the database or settings are unavailable, so pages keep working with their built-in content.
- Do not add new secrets, IDs or environment-specific values to source or `appsettings.json`.

## Database safety

- Do not create, modify or run migrations, and never apply SQL or schema changes directly to a database.
- If a database change is needed, add a standalone script under `database/manual/` and give it to the user to run.
- Clearly state when a change does not need a database update.

## Validation and publish - TEST/VERIFY -> BUILD -> COMMIT -> PUBLISH -> PUSH -> REPORT SHA

Run from the repository or worktree root:

1. Tests: `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter "FullyQualifiedName~Landing"` (add a regression test for each bug fix).
2. Build: `dotnet build TodoX.Landing/TodoX.Landing.csproj -c Release` must report 0 errors.
3. Commit first, so the published DLL carries the commit SHA.
4. Publish to the designated folder, using this absolute path even from a worktree:
   `dotnet publish TodoX.Landing/TodoX.Landing.csproj -c Release -o "D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\publish\todoX-landing"`
5. Verify that `TodoX.Landing.dll` exists there and its ProductVersion carries the expected commit SHA.
6. Push to the configured GitHub remote branch. Never force push. If the push fails, stop and report the exact blocker.
7. Report the changed files, exact commands and results, branch, commit SHA and publish path.

`publish/` is git-ignored: do not commit publish output.

## Git safety

- Do not use `git reset --hard` or `git clean`.
- Do not commit directly to `main`; branch from `main` and merge back.
