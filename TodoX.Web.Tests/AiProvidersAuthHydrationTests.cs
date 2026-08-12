using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProvidersAuthHydrationTests
{
    [Fact]
    public void PageWaitsForAuthHydrationBeforeShowingAuthorizationDecision()
    {
        var page = ReadPage();

        Assert.Contains("@if (!AuthState.IsInitialized)", page, StringComparison.Ordinal);
        Assert.Contains("Đang xác thực...", page, StringComparison.Ordinal);
        Assert.Contains("else if (!IsAdmin)", page, StringComparison.Ordinal);
        Assert.Contains("Bạn cần quyền quản trị TodoX để mở trang này.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageSubscribesAndUnsubscribesFromAuthStateChanges()
    {
        var page = ReadPage();

        Assert.Contains("@implements IDisposable", page, StringComparison.Ordinal);
        Assert.Contains("AuthState.OnChange += HandleAuthStateChanged;", page, StringComparison.Ordinal);
        Assert.Contains("AuthState.OnChange -= HandleAuthStateChanged;", page, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync(async () =>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageInitializesOnlyAfterHydrationAndUsesSingleGuardedLoad()
    {
        var page = ReadPage();

        Assert.Contains("await EnsurePageInitializedAsync();", page, StringComparison.Ordinal);
        Assert.Contains("if (!AuthState.IsInitialized || !IsAdmin || _pageInitialized || _initializingPage)", page, StringComparison.Ordinal);
        Assert.Contains("_initializingPage = true;", page, StringComparison.Ordinal);
        Assert.Contains("await ReloadProviders();", page, StringComparison.Ordinal);
        Assert.Contains("_pageInitialized = true;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("await ReloadModelsAsync();\r\n    }\r\n\r\n    private void HandleAuthStateChanged", page, StringComparison.Ordinal);
        Assert.DoesNotContain("await ReloadModelsAsync();\n    }\n\n    private void HandleAuthStateChanged", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PagePreservesAdminSystemOperatorAndRootRoleRule()
    {
        var page = ReadPage();

        Assert.Contains(
            "AuthState.CurrentUser?.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator || AuthState.CurrentUser?.IsRoot == true",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AuthChangeKeepsRealUnauthorizedUsersDenied()
    {
        var page = ReadPage();

        Assert.Contains("if (!IsAdmin)", page, StringComparison.Ordinal);
        Assert.Contains("_pageInitialized = false;", page, StringComparison.Ordinal);
        Assert.Contains("StateHasChanged();", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizationDenialLogsSafeServerSideDiagnosticsOnly()
    {
        var page = ReadPage();

        Assert.Contains("@inject ILogger<AiProviders> Logger", page, StringComparison.Ordinal);
        Assert.Contains("AI_PROVIDERS_AUTH_DENIED", page, StringComparison.Ordinal);
        Assert.Contains("userId={UserId} role={Role} isRoot={IsRoot} isAuthenticated={IsAuthenticated} isInitialized={IsInitialized}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudText>AI_PROVIDERS_AUTH_DENIED", page, StringComparison.Ordinal);
    }

    private static string ReadPage()
    {
        var file = Path.Combine(FindRepoRoot(), "TodoX.Web", "Components", "Pages", "AiProviders.razor");
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
