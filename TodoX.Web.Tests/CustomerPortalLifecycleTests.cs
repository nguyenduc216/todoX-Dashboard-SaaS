using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CustomerPortalLifecycleTests
{
    [Fact]
    public void RoutesOwnsAuthBootstrapBeforePrivateRouteView()
    {
        var routes = ReadSource("TodoX.Web", "Components", "Routes.razor");
        var layout = ReadSource("TodoX.Web", "Components", "Layout", "MainLayout.razor");
        var home = ReadSource("TodoX.Web", "Components", "Pages", "Home.razor");

        Assert.Contains("@inject AccountService Accounts", routes, StringComparison.Ordinal);
        Assert.Contains("AuthState.InitializeAsync(id => Accounts.RehydrateSessionAsync(id))", routes, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/login\", replace: true)", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthState.InitializeAsync(id => Accounts.RehydrateSessionAsync(id))", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCustomersAsync()", home, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSystemUsersAsync()", home, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCustomerAccountsAsync()", home, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/jobs")]
    [InlineData("/jobs/timelapse/00000000-0000-0000-0000-000000000001")]
    [InlineData("/jobs/rvideo/00000000-0000-0000-0000-000000000001")]
    [InlineData("/jobs/rdance/00000000-0000-0000-0000-000000000001")]
    [InlineData("/ai-assets/characters")]
    public void PrivateRoutesAreHeldByCentralAuthBootstrap(string path)
    {
        var routes = ReadSource("TodoX.Web", "Components", "Routes.razor");

        Assert.Contains("else if (_initializing || !AuthState.IsInitialized)", routes, StringComparison.Ordinal);
        Assert.Contains("await BootstrapAuthAsync();", routes, StringComparison.Ordinal);
        Assert.Contains("Đang tải phiên đăng nhập", routes, StringComparison.Ordinal);
        Assert.NotEqual("/", path);
    }

    [Fact]
    public void CustomerDashboardHasSingleLoadGateTimeoutRetryAndTerminalState()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "CustomerDashboard.razor");

        Assert.Contains("private Task? _loadTask;", page, StringComparison.Ordinal);
        Assert.Contains("if (_loadTask is null && AuthState.IsInitialized", page, StringComparison.Ordinal);
        Assert.Contains("new CancellationTokenSource(TimeSpan.FromSeconds(20))", page, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(ct)", page, StringComparison.Ordinal);
        Assert.Contains("finally", page, StringComparison.Ordinal);
        Assert.Contains("_loadCancellation = null;", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"RetryAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("DashboardLoadState.PartialSuccess", page, StringComparison.Ordinal);
        Assert.Contains("AuthState.OnChange -= HandleAuthChange", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardServiceLogsAllStagesAndDoesNotBlockOnCharacters()
    {
        var source = ReadSource("TodoX.Web", "Services", "CustomerDashboardService.cs");

        Assert.Contains("CUSTOMER_DASHBOARD_LOAD_START", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_TENANT_READY", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_RENDER_COUNTS_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_DANCE_COUNTS_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_RECENT_RENDER_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_RECENT_DANCE_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_CHARACTERS_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_LOAD_DONE", source, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_DASHBOARD_CHARACTERS_FAILED", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveCharactersAsync(user, ct)", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var file = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file));
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
