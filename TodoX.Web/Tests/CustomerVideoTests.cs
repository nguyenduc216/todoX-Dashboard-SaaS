using Microsoft.Extensions.Options;
using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.CustomerVideos;
using TodoX.Web.Services.SharedMedia;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CustomerVideoTests
{
    private static CustomerVideo Valid() => new()
    {
        CustomerId = Guid.NewGuid(),
        ProductCode = CustomerVideoRules.Tvc,
        Title = "TVC khai trương",
        VideoUrl = "/media/customer-videos/videos/0123456789abcdef0123456789abcdef.mp4"
    };

    private static string RepoFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    [Fact]
    public void A_complete_video_is_valid()
        => Assert.Null(CustomerVideoRules.Validate(Valid(), 500));

    [Fact]
    public void A_video_needs_a_customer_kind_title_file_and_non_negative_points()
    {
        var noCustomer = Valid(); noCustomer.CustomerId = Guid.Empty;
        var badKind = Valid(); badKind.ProductCode = "other";
        var noTitle = Valid(); noTitle.Title = " ";
        var noFile = Valid(); noFile.VideoUrl = null;
        Assert.NotNull(CustomerVideoRules.Validate(noCustomer, 0));
        Assert.NotNull(CustomerVideoRules.Validate(badKind, 0));
        Assert.NotNull(CustomerVideoRules.Validate(noTitle, 0));
        Assert.NotNull(CustomerVideoRules.Validate(noFile, 0));
        Assert.NotNull(CustomerVideoRules.Validate(Valid(), -1));
    }

    [Fact]
    public void Both_kinds_have_their_names()
    {
        Assert.Equal("Video truyền thông", CustomerVideoRules.DefaultName(CustomerVideoRules.Communication));
        Assert.Equal("Video TVC doanh nghiệp", CustomerVideoRules.DefaultName(CustomerVideoRules.Tvc));
    }

    [Fact]
    public void Customer_video_files_have_their_own_media_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "todox-customer-videos-" + Guid.NewGuid().ToString("N"));
        var paths = new SharedMediaPathService(Options.Create(new SharedMediaOptions { StorageRoot = root, RequestPath = "/media" }));
        Assert.Equal(Path.Combine(root, "customer-videos", "videos"), paths.GetCustomerVideoPhysicalFolder("videos"));
        Assert.Equal("/media/customer-videos/posters/a.jpg", paths.GetCustomerVideoPublicUrl("posters", "a.jpg"));
        Assert.Throws<ArgumentException>(() => paths.GetCustomerVideoPhysicalFolder("../x"));
        Assert.Throws<ArgumentException>(() => paths.GetCustomerVideoPublicUrl("videos", "../a.mp4"));
    }

    [Fact]
    public void Customers_cannot_open_the_admin_page()
    {
        Assert.True(NavigationAccessRules.IsAdminOnlyPath("/customer-videos"));
        var customer = new CurrentUserSession { UserId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Role = TodoXUserRole.CustomerOwner, IsAuthenticated = true };
        Assert.False(NavigationAccessRules.CanAccessPath(customer, "/customer-videos"));
        Assert.True(NavigationAccessRules.CanAccessPath(customer, "/jobs/video/" + Guid.NewGuid()));
    }

    [Fact]
    public void A_delivered_video_is_a_completed_job_with_its_charged_points()
    {
        var video = Valid();
        video.Id = Guid.NewGuid();
        video.ProductName = "Video TVC doanh nghiệp";
        video.ChargedPoints = 1500;
        video.ChargeStatus = CustomerVideoRules.StatusCharged;
        var row = MyJobs.BuildCustomerVideoRow(video);
        Assert.Equal("Video TVC doanh nghiệp", row.ServiceLabel);
        Assert.Equal("Hoàn tất", row.StageLabel);
        Assert.Equal("1500", row.PointsLabel);
        Assert.Equal($"/jobs/video/{video.Id}", row.Route);

        video.ChargeStatus = CustomerVideoRules.StatusFree;
        Assert.Equal("0", MyJobs.BuildCustomerVideoRow(video).PointsLabel);
    }

    [Fact]
    public void Charging_goes_through_the_existing_wallet_once_per_video()
    {
        var repository = RepoFile("TodoX.Web", "Services", "CustomerVideos", "CustomerVideoRepository.cs");
        Assert.Contains("_wallets.ChargeAsync(", repository);
        Assert.Contains("referenceId: id, referenceType: CustomerVideoRules.ReferenceType", repository);
        Assert.Contains("_wallets.RefundAsync(", repository);
        // Customers only ever see their own videos.
        Assert.Contains("v.customer_id=@customerId", repository);
        Assert.Contains("user.CustomerId == video.CustomerId", repository);
    }

    [Fact]
    public void The_migration_is_additive_and_tested()
    {
        var sql = RepoFile("database", "manual", "customer-videos", "20261110_customer_videos.sql");
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter table billing", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter table crm", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists studio.customer_videos", sql);
        Assert.True(File.Exists(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            "database", "manual", "customer-videos", "20261110_customer_videos.test.sql")));
    }

    [Fact]
    public void Video_dialog_can_pick_files_already_on_the_server()
    {
        var dialog = RepoFile("TodoX.Web", "Components", "Dialogs", "CustomerVideoDialog.razor");
        Assert.Contains("@LibraryButton(true)", dialog);
        Assert.Contains("@LibraryButton(false)", dialog);
        Assert.Contains("Media.ListLibrary(video)", dialog);

        var root = Path.Combine(Path.GetTempPath(), "todox-customer-library-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new SharedMediaOptions { StorageRoot = root, RequestPath = "/media" });
        var paths = new SharedMediaPathService(options);
        Directory.CreateDirectory(paths.GetCustomerVideoPhysicalFolder("videos"));
        Directory.CreateDirectory(paths.GetLandingMediaPhysicalFolder("video-samples", "videos"));
        File.WriteAllBytes(Path.Combine(paths.GetCustomerVideoPhysicalFolder("videos"), "a.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(paths.GetLandingMediaPhysicalFolder("video-samples", "videos"), "b.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(paths.GetLandingMediaPhysicalFolder("video-samples", "videos"), "c.txt"), [1]);
        var auth = new AuthStateService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthStateService>.Instance);
        typeof(AuthStateService).GetProperty(nameof(AuthStateService.CurrentUser))!
            .SetValue(auth, new CurrentUserSession { UserId = Guid.NewGuid(), Role = TodoXUserRole.Admin, IsAuthenticated = true });
        try
        {
            var files = new CustomerVideoMediaService(paths, options, auth).ListLibrary(video: true);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.Url == "/media/customer-videos/videos/a.mp4" && f.Source == CustomerVideoMediaService.SourceCustomerVideos);
            Assert.Contains(files, f => f.Url == "/media/landing/video-samples/videos/b.mp4" && f.Source == CustomerVideoMediaService.SourceVideoSamples);
        }
        finally { Directory.Delete(root, true); }
    }
}
