using TodoX.Web.Services.Maps;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RoutingServiceTests
{
    [Fact]
    public void Url_asks_graphhopper_for_a_car_route_as_plain_coordinates()
    {
        var url = RoutingService.Url("http://localhost:8989", 106.765, 10.817, 106.7, 10.776);
        Assert.Equal("http://localhost:8989/route?point=10.817,106.765&point=10.776,106.7&profile=car&points_encoded=false&instructions=false&calc_points=true", url);
    }

    [Fact]
    public void Parse_reads_line_length_and_time()
    {
        var route = RoutingService.Parse("""{"paths":[{"distance":11700.5,"time":1380000,"points":{"type":"LineString","coordinates":[[106.765,10.817],[106.7,10.776]]}}]}""");
        Assert.NotNull(route);
        Assert.Equal(2, route!.Coords.Count);
        Assert.Equal(106.765, route.Coords[0][0]);
        Assert.Equal(11700.5, route.DistanceMeters);
        Assert.Equal(1380, route.Seconds);
        Assert.Null(RoutingService.Parse("""{"paths":[]}"""));
    }

    [Fact]
    public void Only_points_in_vietnam_are_routed()
    {
        Assert.True(RoutingService.InVietnam(106.7, 10.8));
        Assert.True(RoutingService.InVietnam(112.3, 16.5)); // Hoàng Sa
        Assert.False(RoutingService.InVietnam(100.5, 13.7)); // Bangkok
    }
}
