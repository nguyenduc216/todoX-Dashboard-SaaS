using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePhase2ATests
{
    [Theory]
    [InlineData("TIMELAPSE", CustomerServiceDestination.TimelapseCreator, "/jobs/timelapse/new")]
    [InlineData("RVIDEO", CustomerServiceDestination.ComingSoon, null)]
    [InlineData("RDANCE", CustomerServiceDestination.ComingSoon, null)]
    public void CustomerServiceRouting_UsesStableServiceCodes(
        string serviceCode,
        CustomerServiceDestination expectedDestination,
        string? expectedRoute)
    {
        var route = CustomerServiceRouting.Resolve(serviceCode);

        Assert.Equal(expectedDestination, route.Destination);
        Assert.Equal(expectedRoute, route.Route);
    }

    [Theory]
    [InlineData(3, new[] { 0, 35, 70, 100 })]
    [InlineData(4, new[] { 0, 25, 50, 75, 100 })]
    [InlineData(5, new[] { 0, 20, 40, 60, 80, 100 })]
    [InlineData(6, new[] { 0, 25, 40, 55, 70, 85, 100 })]
    public void ProgressMappings_AreFixedBySceneCount(int sceneCount, int[] expected)
    {
        Assert.Equal(expected, TimelapseRequestRules.GetProgressMapping(sceneCount));
    }

    [Fact]
    public void TimelapseRequest_RejectsUnsupportedInputs()
    {
        var errors = TimelapseRequestRules.Validate(
            new TimelapseCreateRequest
            {
                ProfileCode = string.Empty,
                SceneCount = 7,
                VideoMode = "provider_mode",
                Ratio = "1_1"
            },
            hasOriginalImage: false);

        Assert.Equal(5, errors.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TimelapseRequest_AcceptsOnlyConfiguredSceneCounts(int sceneCount)
    {
        var errors = TimelapseRequestRules.Validate(
            new TimelapseCreateRequest
            {
                ProfileCode = "from_database",
                SceneCount = sceneCount,
                VideoMode = TimelapseRequestRules.FastMode,
                Ratio = TimelapseRequestRules.LandscapeRatio
            },
            hasOriginalImage: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void TimelapseJobAccess_DeniesAnotherCustomersJob()
    {
        var ownerUserId = Guid.NewGuid();
        var ownerCustomerId = Guid.NewGuid();
        var otherCustomer = new CurrentUserSession
        {
            UserId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerOwner
        };

        Assert.False(TimelapseJobAccess.CanRead(ownerUserId, ownerCustomerId, otherCustomer));
    }

    [Fact]
    public void TimelapseJobAccess_AllowsTheOwningCustomerUser()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var currentUser = new CurrentUserSession
        {
            UserId = userId,
            CustomerId = customerId,
            IsAuthenticated = true,
            Role = TodoXUserRole.CustomerUser
        };

        Assert.True(TimelapseJobAccess.CanRead(userId, customerId, currentUser));
    }

    [Fact]
    public void FixedCatalog_RemainsTheTimelapseIdentitySource()
    {
        Assert.Equal("timelapse", FixedTodoXServiceCatalog.ResolveServiceType(FixedTodoXServiceCatalog.Timelapse));
    }
}
