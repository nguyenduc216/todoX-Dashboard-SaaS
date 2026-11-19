using TodoX.Web.Services.Maps;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class MapHostingTests
{
    [Fact]
    public void MapFolderSitsBesideTheSiteUnlessConfigured()
    {
        var site = Path.Combine(Path.GetTempPath(), "todoX", "saas");
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "todoX", "maps")), MapHosting.ResolveRoot(null, site));
        Assert.Equal(Path.GetFullPath(Path.Combine(site, "data")), MapHosting.ResolveRoot("data", site));
        var rooted = Path.Combine(Path.GetTempPath(), "other-maps");
        Assert.Equal(Path.GetFullPath(rooted), MapHosting.ResolveRoot(rooted, site));
    }

    [Fact]
    public void OnlyMapFileTypesAreServed()
    {
        var types = MapHosting.ContentTypes();
        Assert.True(types.TryGetContentType("vietnam.pmtiles", out var pm));
        Assert.Equal("application/vnd.pmtiles", pm);
        Assert.True(types.TryGetContentType("0-255.pbf", out _));
        Assert.False(types.TryGetContentType("appsettings.config", out _));
        Assert.False(types.TryGetContentType("x.exe", out _));
    }
}
