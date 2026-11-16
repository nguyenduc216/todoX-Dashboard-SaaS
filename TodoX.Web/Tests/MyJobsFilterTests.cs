using System.Text;
using TodoX.Web.Services.CustomerJobs;
using Xunit;

namespace TodoX.Web.Tests;

// "Jobs của tôi" search: title (accents optional), service, from/to day, status.
public sealed class MyJobsFilterTests
{
    private static readonly DateTime Created = new DateTime(2026, 11, 11, 5, 32, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("di lam", true)]
    [InlineData("Đi Làm", true)]
    [InlineData("KIẾM TIỀN", true)]
    [InlineData("video mới", false)]
    public void TitleSearchIgnoresCaseAndAccents(string text, bool expected)
        => Assert.Equal(expected, new MyJobsFilter { Text = text }.Matches("Đi Làm Là Để Kiếm Tiền", "Đạo Lý Người Que", MyJobsStatus.Draft, Created));

    [Fact]
    public void ServiceStatusAndDayRangeMustAllMatch()
    {
        var local = Created.ToLocalTime().Date;
        Assert.True(new MyJobsFilter { Service = "đạo lý người que", Status = MyJobsStatus.Completed, From = local, To = local }
            .Matches("A", "Đạo Lý Người Que", MyJobsStatus.Completed, Created));
        Assert.False(new MyJobsFilter { Service = "KOC Người Mẫu Livestream" }.Matches("A", "Đạo Lý Người Que", MyJobsStatus.Completed, Created));
        Assert.False(new MyJobsFilter { Status = MyJobsStatus.Failed }.Matches("A", "B", MyJobsStatus.Completed, Created));
        Assert.False(new MyJobsFilter { From = local.AddDays(1) }.Matches("A", "B", MyJobsStatus.Completed, Created));
        Assert.False(new MyJobsFilter { To = local.AddDays(-1) }.Matches("A", "B", MyJobsStatus.Completed, Created));
        Assert.False(new MyJobsFilter().IsActive);
    }

    [Theory]
    [InlineData("Nháp", MyJobsStatus.Draft)]
    [InlineData("Bản nháp", MyJobsStatus.Draft)]
    [InlineData("Hoàn tất", MyJobsStatus.Completed)]
    [InlineData("Hoàn thành", MyJobsStatus.Completed)]
    [InlineData("Thất bại", MyJobsStatus.Failed)]
    [InlineData("Lỗi", MyJobsStatus.Failed)]
    [InlineData("Quá thời gian xử lý", MyJobsStatus.Failed)]
    [InlineData("Đã hủy", MyJobsStatus.Cancelled)]
    [InlineData("Đã dừng", MyJobsStatus.Cancelled)]
    [InlineData("Đang ghép video", MyJobsStatus.Running)]
    [InlineData("Đang tạo ảnh/video", MyJobsStatus.Running)]
    [InlineData("Video đã sẵn sàng", MyJobsStatus.Running)]
    public void EveryJobLabelFallsInOneStatus(string label, string expected) => Assert.Equal(expected, MyJobsStatus.Group(label));

    [Fact]
    public void PageShowsTheFourFiltersAndSearchesOlderPages()
    {
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Components", "Pages", "MyJobs.razor"), Encoding.UTF8);
        foreach (var label in new[] { "Tìm theo tiêu đề", "Label=\"Dịch vụ\"", "Từ ngày", "Đến ngày", "Label=\"Trạng thái\"", "Xóa bộ lọc" })
            Assert.Contains(label, page);
        Assert.Contains("<MudTable Items=\"_visible\"", page);
        Assert.Contains("FillFilteredAsync", page);
        Assert.Contains("autocomplete=\"off\"", page);
    }
}
