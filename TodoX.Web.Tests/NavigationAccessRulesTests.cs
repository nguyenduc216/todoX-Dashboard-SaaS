using TodoX.Web.Models;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class NavigationAccessRulesTests
{
    [Theory]
    [InlineData("/customers")]
    [InlineData("/render-jobs")]
    [InlineData("/admin/ai-providers")]
    [InlineData("/landing/contacts")]
    [InlineData("/settings/prompt-templates")]
    public void CustomerCannotAccessAdminOnlyPaths(string path)
    {
        var user = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner };

        Assert.False(NavigationAccessRules.CanAccessPath(user, path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/create")]
    [InlineData("/jobs")]
    [InlineData("/ai-assets/characters")]
    [InlineData("/reference-videos")]
    public void CustomerCanAccessCustomerPaths(string path)
    {
        var user = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner };

        Assert.True(NavigationAccessRules.CanAccessPath(user, path));
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/terms")]
    [InlineData("/access-denied")]
    public void PublicPathsRemainAccessible(string path)
    {
        Assert.True(NavigationAccessRules.CanAccessPath(null, path));
    }
}
