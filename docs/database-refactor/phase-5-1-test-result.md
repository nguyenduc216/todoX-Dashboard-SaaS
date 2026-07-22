# Phase 5.1 Test Result

Commands:

- `dotnet build TodoX.Dashboard.sln -c Release`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build --filter "FullyQualifiedName~AiCoreRuntimePhase51Tests"`: passed; 7 passed, 0 skipped, 0 failed.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build`: passed; 253 passed, 1 skipped, 0 failed.
- `git diff --check`: passed; only Git line-ending warnings were printed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\phase5-1-prod-hardening`: passed.

Notes:

- Remaining skipped test is `SceneImageRenderServiceTests.Rerender_Throws_WhenResolvedProviderIsNotRoutedImageProvider`, unrelated to the three Phase 5.1 PostgreSQL integration tests.
- Publish output directory: `artifacts\publish\phase5-1-prod-hardening`.
- Publish output was not staged for git.
