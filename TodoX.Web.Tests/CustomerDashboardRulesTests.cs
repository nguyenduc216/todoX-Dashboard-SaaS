using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CustomerDashboardRulesTests
{
    [Fact]
    public void MonthRange_UsesApplicationTimezoneAndUtcExclusiveEnd()
    {
        var timezone = TimeZoneInfo.CreateCustomTimeZone(
            "test",
            TimeSpan.FromHours(2),
            "test",
            "test");

        var (start, end) = CustomerDashboardService.CurrentApplicationMonthUtcRange(
            new DateTime(2026, 8, 23, 12, 30, 0, DateTimeKind.Utc),
            timezone);

        Assert.Equal(new DateTime(2026, 7, 31, 22, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc), end);
    }

    [Theory]
    [InlineData(RenderJobTypes.Timelapse, null, null, CustomerDashboardRenderRouteKind.Timelapse, "/jobs/timelapse/")]
    [InlineData(RenderJobTypes.CoreService, TodoXServiceEngineTypes.RVideo, null, CustomerDashboardRenderRouteKind.RVideo, "/jobs/rvideo/")]
    [InlineData(RenderJobTypes.DanceSell, null, null, CustomerDashboardRenderRouteKind.RDance, "/jobs/rdance/")]
    public void WorkflowRules_ResolveRouteKindAndDetailRoute(
        string jobType,
        string? operationType,
        string? serviceCode,
        CustomerDashboardRenderRouteKind expectedKind,
        string expectedRoutePrefix)
    {
        var id = Guid.NewGuid();
        var kind = CustomerDashboardWorkflowRules.ResolveRenderRouteKind(jobType, operationType, serviceCode);

        Assert.Equal(expectedKind, kind);
        Assert.StartsWith(expectedRoutePrefix, CustomerDashboardWorkflowRules.ResolveRenderDetailRoute(kind, id));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(TodoXUserRole.CustomerOwner, false)]
    [InlineData(TodoXUserRole.CustomerUser, false)]
    [InlineData(TodoXUserRole.Admin, true)]
    [InlineData(TodoXUserRole.SystemOperator, true)]
    public void AdminAuthorization_OnlyAllowsAuthenticatedAdminRoles(TodoXUserRole? role, bool expected)
    {
        var user = role is null
            ? null
            : new CurrentUserSession { IsAuthenticated = true, Role = role.Value };

        Assert.Equal(expected, AdminEndpointAuthorization.IsAdmin(user));
    }
}
