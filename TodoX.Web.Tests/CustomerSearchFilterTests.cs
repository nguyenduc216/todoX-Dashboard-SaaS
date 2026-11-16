using TodoX.Web.Models;
using TodoX.Web.Services.Studio;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Customer list: clickable stat cards, "Khách cần làm video" and "Tìm kiếm chi tiết".</summary>
public sealed class CustomerSearchFilterTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static readonly CustomerVideoCounts NoVideo = new(0, 0, 0);
    private static readonly CustomerVideoCounts InProgressOnly = new(0, 0, 2);
    private static readonly CustomerVideoCounts Delivered = new(1, 0, 0);

    private static CustomerProfile Customer(string segment = CustomerSegments.Regular, TodoXAccountStatus status = TodoXAccountStatus.Active)
        => new()
        {
            CompanyName = "Quinter Central", CustomerCode = "CUST-0017", ContactName = "Kim Huệ",
            Email = "kimhueqt@gmail.com", Phone = "0979 799 999", Segment = segment, Status = status,
            CreatedAt = new DateTime(2026, 11, 9)
        };

    [Fact]
    public void A_customer_needs_a_video_when_chasing_and_nothing_was_delivered_or_released()
    {
        Assert.True(CustomerSearchFilter.NeedsVideo(Customer(CustomerSegments.Chasing), NoVideo));
        // A plan still in progress does not count as a video made yet.
        Assert.True(CustomerSearchFilter.NeedsVideo(Customer(CustomerSegments.Chasing), InProgressOnly));
        Assert.False(CustomerSearchFilter.NeedsVideo(Customer(CustomerSegments.Chasing), Delivered));
        Assert.False(CustomerSearchFilter.NeedsVideo(Customer(CustomerSegments.Chasing), new CustomerVideoCounts(0, 1, 0)));
        Assert.False(CustomerSearchFilter.NeedsVideo(Customer(CustomerSegments.Vip), NoVideo));
        Assert.False(CustomerSearchFilter.NeedsVideo(Customer(), NoVideo));
    }

    [Fact]
    public void Each_card_filters_the_list()
    {
        var active = Customer();
        var locked = Customer(status: TodoXAccountStatus.Locked);
        var pending = Customer(status: TodoXAccountStatus.Pending);
        Assert.True(CustomerSearchFilter.MatchesCard(CustomerCardFilter.All, locked, NoVideo));
        Assert.True(CustomerSearchFilter.MatchesCard(CustomerCardFilter.Active, active, NoVideo));
        Assert.False(CustomerSearchFilter.MatchesCard(CustomerCardFilter.Active, locked, NoVideo));
        Assert.True(CustomerSearchFilter.MatchesCard(CustomerCardFilter.Pending, pending, NoVideo));
        Assert.True(CustomerSearchFilter.MatchesCard(CustomerCardFilter.Locked, locked, NoVideo));
        Assert.True(CustomerSearchFilter.MatchesCard(CustomerCardFilter.NeedVideo, Customer(CustomerSegments.Chasing), NoVideo));
        Assert.False(CustomerSearchFilter.MatchesCard(CustomerCardFilter.NeedVideo, active, NoVideo));
    }

    [Fact]
    public void An_empty_detailed_search_matches_everyone()
    {
        var filter = new CustomerSearchFilter();
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(Customer(), NoVideo));
    }

    [Fact]
    public void Detailed_search_combines_every_criterion()
    {
        var c = Customer(CustomerSegments.Chasing);
        Assert.True(new CustomerSearchFilter { Name = "quinter", Code = "0017", Contact = "huệ", Email = "kimhue", Phone = "0979799" }.Matches(c, NoVideo));
        Assert.False(new CustomerSearchFilter { Name = "sunshine" }.Matches(c, NoVideo));
        // Phone ignores spaces / dots on both sides.
        Assert.True(new CustomerSearchFilter { Phone = "799.999" }.Matches(c, NoVideo));

        Assert.True(new CustomerSearchFilter { Segments = { CustomerSegments.Chasing, CustomerSegments.Vip } }.Matches(c, NoVideo));
        Assert.False(new CustomerSearchFilter { Segments = { CustomerSegments.Vip } }.Matches(c, NoVideo));
        Assert.False(new CustomerSearchFilter { Status = TodoXAccountStatus.Locked }.Matches(c, NoVideo));

        Assert.True(new CustomerSearchFilter { Video = CustomerVideoFilter.None }.Matches(c, InProgressOnly));
        Assert.False(new CustomerSearchFilter { Video = CustomerVideoFilter.None }.Matches(c, Delivered));
        Assert.True(new CustomerSearchFilter { Video = CustomerVideoFilter.HasVideo }.Matches(c, Delivered));
        Assert.True(new CustomerSearchFilter { Video = CustomerVideoFilter.InProgress }.Matches(c, InProgressOnly));
        Assert.False(new CustomerSearchFilter { Video = CustomerVideoFilter.InProgress }.Matches(c, Delivered));

        Assert.False(new CustomerSearchFilter { Agent = CustomerAgentFilter.IsAgent }.Matches(c, NoVideo));
        Assert.True(new CustomerSearchFilter { CreatedFrom = new DateTime(2026, 11, 9), CreatedTo = new DateTime(2026, 11, 9) }.Matches(c, NoVideo));
        Assert.False(new CustomerSearchFilter { CreatedFrom = new DateTime(2026, 11, 10) }.Matches(c, NoVideo));
    }

    [Fact]
    public void Active_criteria_are_listed_and_a_copy_is_independent()
    {
        var filter = new CustomerSearchFilter { Name = "Quinter", Segments = { CustomerSegments.Vip }, Video = CustomerVideoFilter.None };
        Assert.Equal(new[] { "Tên: Quinter", "Loại: VIP", "Chưa có video" }, filter.Describe());

        var copy = filter.Clone();
        copy.Segments.Add(CustomerSegments.Chasing);
        Assert.Single(filter.Segments);
    }

    [Fact]
    public void Page_wires_the_cards_and_the_detailed_search()
    {
        var page = Read("TodoX.Web", "Components", "Pages", "Customers.razor");
        Assert.Contains("\"Khách cần làm video\"", page);
        Assert.Contains("@onclick=\"@(() => SelectCard(card.Filter))\"", page);
        Assert.Contains("CustomerSearchFilter.MatchesCard(_card, x, Counts(x.Id))", page);
        Assert.Contains("_advanced.Matches(x, Counts(x.Id))", page);
        Assert.Contains("DialogService.ShowAsync<CustomerSearchDialog>", page);
        Assert.Contains("Tìm kiếm chi tiết", page);
        Assert.True(File.Exists(Path.Combine(Root, "TodoX.Web", "Components", "Dialogs", "CustomerSearchDialog.razor")));
    }
}
