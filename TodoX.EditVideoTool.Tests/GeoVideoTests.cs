using TodoX.EditVideoTool.Mcp;

namespace TodoX.EditVideoTool.Tests;

/// <summary>Real-estate location video: input checks and the maths behind plots, distances and times.</summary>
public sealed class GeoVideoTests
{
    private static readonly double[] Site = [106.765, 10.817];

    [Fact]
    public void A_rectangle_plot_is_laid_out_in_metres_around_the_site()
    {
        var ring = VideoTools.PlotRing(Site, new GeoPlotInput("A", "Đất ở", WidthMeters: 100, DepthMeters: 50, EastMeters: 200));
        Assert.Equal(4, ring.Length);
        var centre = new[] { ring.Average(c => c[0]), ring.Average(c => c[1]) };
        Assert.InRange(VideoTools.Metres(Site, centre), 195, 205);
        Assert.InRange(VideoTools.Metres(ring[0], ring[1]), 99, 101);
        Assert.InRange(VideoTools.Metres(ring[1], ring[2]), 49, 51);
    }

    [Fact]
    public void Corners_are_used_as_given()
    {
        var corners = new[] { new[] { 106.764, 10.816 }, new[] { 106.766, 10.816 }, new[] { 106.766, 10.818 } };
        Assert.Equal(corners, VideoTools.PlotRing(Site, new GeoPlotInput("B", "Cây xanh", Corners: corners)));
    }

    [Fact]
    public void Wrong_coordinates_are_explained()
    {
        Assert.Contains("không nằm trong Việt Nam", VideoTools.GeoProblem([10.817, 106.765], null, null, null)); // lat/lon swapped
        Assert.Contains("30 km", VideoTools.GeoProblem(Site, null, [new GeoPoiInput("school", "Xa", 105.8, 21.0)], null));
        Assert.Contains("Corners", VideoTools.GeoProblem(Site, [new GeoPlotInput("C", "Thương mại")], null, null));
        Assert.Null(VideoTools.GeoProblem(Site, [new GeoPlotInput("A", "Đất ở", WidthMeters: 20, DepthMeters: 30)],
            [new GeoPoiInput("metro", "Ga", 106.77, 10.82)], [new GeoDestinationInput("Quận 1", 106.70, 10.776)]));
    }

    [Fact]
    public void Distances_and_times_read_naturally_in_vietnamese()
    {
        Assert.Equal("650 m", VideoTools.Distance(648));
        Assert.Equal("1,2 km", VideoTools.Distance(1234));
        Assert.Equal("12 km", VideoTools.Km(11700));
        Assert.Equal("≈ 18–26 phút", VideoTools.Minutes(15 * 60));
    }

    [Fact]
    public void Length_follows_the_scenes_present() => Assert.Equal((230 + 150 + 3 * 45 + 150 + 6 * 14 + 230 + 180) / 30.0, VideoTools.GeoSeconds(3, 6, 1));
}
