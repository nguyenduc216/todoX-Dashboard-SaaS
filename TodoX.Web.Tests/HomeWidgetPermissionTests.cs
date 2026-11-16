using TodoX.Web.Models;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>The dashboard home page shows each account only the stats it is allowed to see.</summary>
public sealed class HomeWidgetPermissionTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static CurrentUserSession Session(TodoXUserRole role, params string[] permissions)
        => new() { IsAuthenticated = true, Role = role, Permissions = permissions.ToHashSet() };

    [Fact]
    public void Admins_see_every_block()
    {
        foreach (var widget in HomeWidgets.All)
        {
            Assert.True(HomeWidgets.CanSee(Session(TodoXUserRole.Admin), widget.Module));
            Assert.True(HomeWidgets.CanSee(Session(TodoXUserRole.SystemOperator), widget.Module));
        }
    }

    [Fact]
    public void A_staff_account_sees_only_the_blocks_its_role_was_granted()
    {
        var staff = Session(TodoXUserRole.Staff, "home_my_video_plans.view", "video_plans.view");
        Assert.True(HomeWidgets.CanSee(staff, HomeWidgets.MyVideoPlans));
        Assert.False(HomeWidgets.CanSee(staff, HomeWidgets.PointsSold));
        Assert.False(HomeWidgets.CanSee(staff, HomeWidgets.Accounts));
        // Seeing the plan pages does not by itself put plan stats on the home page.
        Assert.False(HomeWidgets.CanSee(staff, HomeWidgets.VideoPlans));
        Assert.False(HomeWidgets.CanSee(Session(TodoXUserRole.Staff), HomeWidgets.Customers));
    }

    [Fact]
    public void Customers_and_signed_out_visitors_see_none()
    {
        foreach (var widget in HomeWidgets.All)
        {
            Assert.False(HomeWidgets.CanSee(Session(TodoXUserRole.CustomerOwner, HomeWidgets.Permission(widget.Module)), widget.Module));
            Assert.False(HomeWidgets.CanSee(null, widget.Module));
            Assert.False(HomeWidgets.CanSee(new CurrentUserSession(), widget.Module));
        }
    }

    [Fact]
    public void Every_home_block_is_rendered_behind_its_permission()
    {
        var page = Read("TodoX.Web", "Components", "Pages", "AdminDashboard.razor");
        foreach (var name in new[] { "Customers", "Accounts", "PointsSold", "RenderJobs", "MyVideoPlans", "VideoPlans", "Roadmap" })
            Assert.Contains($"See(HomeWidgets.{name})", page);
        Assert.Equal(7, HomeWidgets.All.Count);
        Assert.Equal(HomeWidgets.All.Count, HomeWidgets.All.Select(x => x.Module).Distinct().Count());
    }

    /// <summary>Rule: a new stat on the home page ships with its own permission row, granted per role.</summary>
    [Fact]
    public void Every_home_block_has_a_permission_row_in_a_sql_script()
    {
        var sql = string.Join("\n", Directory.EnumerateFiles(Path.Combine(Root, "database", "manual"), "*.sql", SearchOption.AllDirectories)
            .Where(x => !x.EndsWith(".test.sql", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        foreach (var widget in HomeWidgets.All)
            Assert.True(sql.Contains($"'{widget.Module}'"), $"No SQL creates the permission for home block {widget.Module}.");
    }
}
