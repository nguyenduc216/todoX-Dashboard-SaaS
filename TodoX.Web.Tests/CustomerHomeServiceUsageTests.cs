using TodoX.Web.Models.Catalog;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Customer home: most used services first, "Hay dùng" badge, services shown as a slider with arrows.</summary>
public sealed class CustomerHomeServiceUsageTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static CatalogServiceView Service(string name, int sort) => new() { Id = Guid.NewGuid(), DisplayName = name, SortOrder = sort };

    [Fact]
    public void Most_used_services_come_first_and_recent_use_weighs_more()
    {
        var brand = Service("Xây kênh nhân hiệu", 1);
        var expert = Service("Chia sẻ chuyên gia", 2);
        var koc = Service("KOC livestream", 3);
        var unused = Service("Timelapse", 0);
        var now = DateTime.UtcNow;
        var usage = new Dictionary<Guid, ServiceUsage>
        {
            // 6 old jobs (score 6) vs 3 jobs this month (score 3*3+3 = 12): the recent one wins.
            [brand.Id] = new(brand.Id, 0, 6, now.AddDays(-60)),
            [koc.Id] = new(koc.Id, 3, 3, now.AddDays(-1)),
            [expert.Id] = new(expert.Id, 0, 1, now.AddDays(-90))
        };

        var ordered = ServiceUsageRanking.Order([brand, expert, koc, unused], usage);

        Assert.Equal(new[] { koc, brand, expert, unused }, ordered);
        Assert.Equal("3 lần", ServiceUsageRanking.Badge(koc, usage));
        Assert.Null(ServiceUsageRanking.Badge(expert, usage));
        Assert.Null(ServiceUsageRanking.Badge(unused, usage));
    }

    [Fact]
    public void Without_usage_the_catalog_order_is_kept()
    {
        var a = Service("B", 2);
        var b = Service("A", 1);
        Assert.Equal(new[] { b, a }, ServiceUsageRanking.Order([a, b], new Dictionary<Guid, ServiceUsage>()));
    }

    [Fact]
    public void Home_shows_the_services_as_a_slider_with_arrows_on_both_sides()
    {
        var page = Read("TodoX.Web", "Components", "Pages", "CustomerDashboard.razor");
        Assert.Contains("data-todox-slider", page);
        Assert.Contains("data-todox-slide=\"-1\"", page);
        Assert.Contains("data-todox-slide=\"1\"", page);
        Assert.Contains("ServiceUsageRanking.Order(services, _usage)", page);
        Assert.Contains("Badge=\"@ServiceUsageRanking.Badge(service, _usage)\"", page);
        Assert.Contains("js/todox-slider.js", Read("TodoX.Web", "Components", "App.razor"));
        Assert.Contains(".todox-slider-arrow.is-prev", Read("TodoX.Web", "wwwroot", "css", "todox-theme.css"));

        // Only parent jobs count, so one video with many scenes is one use.
        var service = Read("TodoX.Web", "Services", "CustomerDashboardService.cs");
        Assert.Contains("AND job_type = ANY(@jobTypes)", service);
    }
}
