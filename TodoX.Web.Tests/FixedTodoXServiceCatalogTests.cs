using TodoX.Web.Models.Catalog;
using Xunit;

namespace TodoX.Web.Tests;

public class FixedTodoXServiceCatalogTests
{
    [Fact]
    public void FixedServices_HaveUniqueImmutableCodes()
    {
        var codes = FixedTodoXServiceCatalog.Services.Select(x => x.ServiceCode).ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(["TIMELAPSE", "RVIDEO", "RDANCE"], codes);
    }

    [Fact]
    public void FixedServices_AreOrderedForCustomerSelectors()
    {
        Assert.Equal([10, 20, 30], FixedTodoXServiceCatalog.Services.Select(x => x.SortOrder).ToArray());
    }

    [Theory]
    [InlineData("TIMELAPSE", "timelapse")]
    [InlineData("RVIDEO", "rvideo")]
    [InlineData("RDANCE", "rdance")]
    public void FixedServices_MapToExpectedServiceType(string serviceCode, string serviceType)
    {
        Assert.Equal(serviceType, FixedTodoXServiceCatalog.ResolveServiceType(serviceCode));
    }

    [Fact]
    public void FixedServices_UseEnabledStatus()
    {
        Assert.All(FixedTodoXServiceCatalog.Services, x => Assert.Equal("enabled", x.Status));
    }
}
