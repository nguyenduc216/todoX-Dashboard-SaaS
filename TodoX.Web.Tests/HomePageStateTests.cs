using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class HomePageStateTests
{
    [Theory]
    [InlineData(false, null, HomeShellMode.Loading)]
    [InlineData(true, null, HomeShellMode.SignIn)]
    [InlineData(true, TodoXUserRole.CustomerUser, HomeShellMode.Customer)]
    [InlineData(true, TodoXUserRole.Admin, HomeShellMode.Admin)]
    public void Resolve_SelectsExpectedShellMode(bool initialized, TodoXUserRole? role, HomeShellMode expected)
    {
        var user = role is null
            ? null
            : new CurrentUserSession { IsAuthenticated = true, Role = role.Value };

        Assert.Equal(expected, HomePageState.Resolve(user, initialized));
    }

    [Fact]
    public void RootPathIsNotPublic()
    {
        Assert.False(NavigationAccessRules.IsPublicPath("/"));
    }
}
