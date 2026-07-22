# Phase 5.1 Test Result

Commands:

- `git diff --check`: passed.
- `dotnet restore TodoX.Dashboard.sln`: passed, all projects up-to-date.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors on final run.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build`: passed; 250 passed, 4 skipped, 0 failed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\phase5-1-prod-hardening`: passed.

Notes:

- An earlier parallel build/test attempt produced a stale test failure because test executed against the previous binary while `testhost` still held the DLL. The sequential rerun passed.
- Publish output was not staged for git.
