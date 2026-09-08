using System.Text;
using TodoX.Web.Models;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AdminJobMonitorTests
{
    [Theory]
    [InlineData(TodoXUserRole.Admin, true)]
    [InlineData(TodoXUserRole.SystemOperator, true)]
    [InlineData(TodoXUserRole.CustomerOwner, false)]
    [InlineData(TodoXUserRole.CustomerUser, false)]
    public void AdminAuthorization_MatchesJobMonitorAccess(TodoXUserRole role, bool expected)
    {
        var user = new CurrentUserSession
        {
            IsAuthenticated = true,
            Role = role
        };

        Assert.Equal(expected, AdminEndpointAuthorization.IsAdmin(user));
    }

    [Fact]
    public void JobMonitorPage_IsReadOnlyAndUsesRequiredTabsAndAdminGuard()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "AdminJobMonitor.razor");

        Assert.Contains("@page \"/admin/job-monitor\"", page, StringComparison.Ordinal);
        Assert.Contains("AdminEndpointAuthorization.IsAdmin(AuthState.CurrentUser)", page, StringComparison.Ordinal);
        Assert.Contains("Tổng job", page, StringComparison.Ordinal);
        Assert.Contains("Giám sát job hệ thống", page, StringComparison.Ordinal);
        Assert.Contains("Đang xử lý", page, StringComparison.Ordinal);
        Assert.Contains("Hoàn thành", page, StringComparison.Ordinal);
        Assert.Contains("Thất bại", page, StringComparison.Ordinal);
        Assert.Contains("Timeline", page, StringComparison.Ordinal);
        Assert.Contains("Input / Output", page, StringComparison.Ordinal);
        Assert.Contains("Logs", page, StringComparison.Ordinal);
        Assert.Contains("Chỉ xem - Quản trị viên", page, StringComparison.Ordinal);
        Assert.Contains("Đăng nhập nhanh", page, StringComparison.Ordinal);
        Assert.Contains("InputLinks", page, StringComparison.Ordinal);
        Assert.Contains("Provider", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rerender", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JobMonitorService_ProvidesServerSidePagingFiltersDetailsAndAudit()
    {
        var service = ReadSource("TodoX.Web", "Services", "AdminJobMonitorService.cs");

        Assert.Contains("PageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 100)", service, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", service, StringComparison.Ordinal);
        Assert.Contains("r.id::text ILIKE @search", service, StringComparison.Ordinal);
        Assert.Contains("statusFilter", service, StringComparison.Ordinal);
        Assert.Contains("fromUtc", service, StringComparison.Ordinal);
        Assert.Contains("toUtc", service, StringComparison.Ordinal);
        Assert.Contains("render.render_job_events", service, StringComparison.Ordinal);
        Assert.Contains("RecordImpersonationAuditAsync", service, StringComparison.Ordinal);
        Assert.Contains("admin_job_monitor", service, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationRules_ClassifyJobMonitorAsAdminOnly()
    {
        Assert.True(NavigationAccessRules.IsAdminOnlyPath("/admin/job-monitor"));
        Assert.False(NavigationAccessRules.CanAccessPath(
            new CurrentUserSession
            {
                IsAuthenticated = true,
                Role = TodoXUserRole.CustomerOwner
            },
            "/admin/job-monitor"));
    }

    [Theory]
    [InlineData(TodoXUserRole.CustomerOwner)]
    [InlineData(TodoXUserRole.CustomerUser)]
    public void NavigationRules_RejectAuthenticatedNonAdminsForJobMonitor(TodoXUserRole role)
    {
        Assert.False(NavigationAccessRules.CanAccessPath(
            new CurrentUserSession
            {
                IsAuthenticated = true,
                Role = role
            },
            "/admin/job-monitor"));
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine([root, .. parts])));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(directory.FullName, "TodoX.Web")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
