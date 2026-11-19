using TodoX.Web.Components.Dialogs;
using TodoX.Web.Components.Pages;
using TodoX.Web.Services.Studio;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Công việc nhân viên: trạng thái, tổng kết đã/chưa hoàn thành, giờ Việt Nam, quyền và menu.</summary>
public sealed class StaffTaskTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static StaffTask Task(string status, DateTime? due = null) => new() { Title = "x", Status = status, CompletedAt = due };

    [Fact]
    public void Statuses_have_vietnamese_labels_and_only_new_pending_in_progress_are_open()
    {
        Assert.Equal(["new", "pending", "in_progress", "done", "cancelled"], StaffTaskStatuses.All);
        Assert.Equal(["Mới", "Chờ thực hiện", "Đang thực hiện", "Đã hoàn thành", "Hủy"], StaffTaskStatuses.All.Select(StaffTaskStatuses.Label));
        Assert.True(StaffTaskStatuses.IsOpen("new"));
        Assert.True(StaffTaskStatuses.IsOpen("pending"));
        Assert.True(StaffTaskStatuses.IsOpen("in_progress"));
        Assert.False(StaffTaskStatuses.IsOpen("done"));
        Assert.False(StaffTaskStatuses.IsOpen("cancelled"));
        Assert.Equal("Mới", StaffTaskStatuses.Label("bogus"));
    }

    [Fact]
    public void Summary_counts_done_and_not_done_and_each_status()
    {
        var summary = StaffTaskSummary.From([Task("new"), Task("pending"), Task("in_progress"), Task("in_progress"), Task("done"), Task("cancelled")]);
        Assert.Equal(4, summary.Open);
        Assert.Equal(1, summary.Done);
        Assert.Equal(6, summary.Total);
        Assert.Equal(2, summary.Count("in_progress"));
        Assert.Equal(1, summary.Count("cancelled"));
    }

    [Fact]
    public void Title_is_required_customer_is_not_and_completion_must_follow_start()
    {
        Assert.NotNull(StaffTaskRules.Validate(new StaffTask { Title = "  " }));
        Assert.Null(StaffTaskRules.Validate(new StaffTask { Title = "Quay video", CustomerId = null }));
        Assert.NotNull(StaffTaskRules.Validate(new StaffTask { Title = "x", Status = "bogus" }));
        var start = new DateTime(2026, 11, 19, 9, 0, 0);
        Assert.NotNull(StaffTaskRules.Validate(new StaffTask { Title = "x", StartAt = start, CompletedAt = start.AddHours(-1) }));
        Assert.Null(StaffTaskRules.Validate(new StaffTask { Title = "x", StartAt = start, CompletedAt = start.AddHours(2) }));
    }

    [Fact]
    public void Done_without_completion_time_is_stamped_and_late_means_open_past_due()
    {
        var now = new DateTime(2026, 11, 19, 15, 30, 0);
        Assert.Equal(now, StaffTaskRules.CompletedAtFor("done", null, now));
        Assert.Null(StaffTaskRules.CompletedAtFor("in_progress", null, now));
        var entered = now.AddDays(-1);
        Assert.Equal(entered, StaffTaskRules.CompletedAtFor("done", entered, now));

        Assert.True(StaffTaskRules.IsLate(Task("in_progress", now.AddMinutes(-1)), now));
        Assert.False(StaffTaskRules.IsLate(Task("done", now.AddMinutes(-1)), now));
        Assert.False(StaffTaskRules.IsLate(Task("pending", now.AddMinutes(1)), now));
        Assert.False(StaffTaskRules.IsLate(Task("new"), now));
    }

    [Fact]
    public void Times_are_vietnam_local_in_the_app_and_utc_in_the_database()
    {
        var local = new DateTime(2026, 11, 19, 8, 30, 0);
        var utc = VietnamTime.ToUtc(local)!.Value;
        Assert.Equal(new DateTime(2026, 11, 19, 1, 30, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(local, VietnamTime.FromUtc(utc));
        Assert.Null(VietnamTime.ToUtc(null));

        Assert.Equal(new DateTime(2026, 11, 19, 14, 5, 0), StaffTaskDialog.Combine(new DateTime(2026, 11, 19), new TimeSpan(14, 5, 0)));
        Assert.Equal(new DateTime(2026, 11, 19), StaffTaskDialog.Combine(new DateTime(2026, 11, 19), null));
        Assert.Null(StaffTaskDialog.Combine(null, new TimeSpan(9, 0, 0)));
    }

    [Fact]
    public void Products_are_library_videos_named_after_their_customer_video_or_sample()
    {
        Assert.True(StaffTaskRules.IsLibraryUrl("/media/customer-videos/videos/a.mp4"));
        Assert.True(StaffTaskRules.IsLibraryUrl("/media/landing/video-samples/videos/b.mp4"));
        Assert.False(StaffTaskRules.IsLibraryUrl("https://evil.example/a.mp4"));
        Assert.False(StaffTaskRules.IsLibraryUrl("/media/../secret.mp4"));
        Assert.False(StaffTaskRules.IsLibraryUrl(null));

        Assert.Equal("Quinter YEP", new StaffTaskProduct("/media/x/a.mp4", "Quinter YEP", "Kho mẫu video", null).DisplayTitle);
        Assert.Equal("a.mp4", new StaffTaskProduct("/media/x/a.mp4", null, null, null).DisplayTitle);

        var sql = Read("database", "manual", "studio", "20261119_staff_task_products.sql");
        Assert.Contains("references studio.staff_tasks (id) on delete cascade", sql);
        Assert.Contains("unique (task_id, video_url)", sql);
        Assert.DoesNotContain("alter table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "studio", "20261119_staff_task_products.test.sql")));

        var repo = Read("TodoX.Web", "Services", "Studio", "StaffTaskRepository.cs");
        Assert.Contains("_media.ListLibrary(video: true, take: 400, \"staff_tasks.create\", \"staff_tasks.update\")", repo);
        Assert.Contains("FROM studio.customer_videos v", repo);
        Assert.Contains("FROM landing.video_samples s", repo);
        Assert.Contains(".Where(p => StaffTaskRules.IsLibraryUrl(p.VideoUrl))", repo);

        var dialog = Read("TodoX.Web", "Components", "Dialogs", "StaffTaskDialog.razor");
        Assert.Contains("Chọn video từ thư viện", dialog);
        Assert.Contains("Products = Source.Products.ToList()", dialog);
        Assert.Contains("<MudTh>Sản phẩm</MudTh>", Read("TodoX.Web", "Components", "Pages", "StaffTasks.razor"));
    }

    [Fact]
    public void Migration_is_additive_with_permissions_and_page_is_wired()
    {
        var sql = Read("database", "manual", "studio", "20261119_staff_tasks.sql");
        Assert.Contains("create table if not exists studio.staff_tasks", sql);
        Assert.Contains("customer_id      uuid references crm.customers (id) on delete set null", sql);
        Assert.Contains("check (status in ('new', 'pending', 'in_progress', 'done', 'cancelled'))", sql);
        Assert.DoesNotContain("alter table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "studio", "20261119_staff_tasks.test.sql")));

        var repo = Read("TodoX.Web", "Services", "Studio", "StaffTaskRepository.cs");
        Assert.Contains("Require(\"staff_tasks.view\")", repo);
        Assert.Contains("Require(\"staff_tasks.delete\")", repo);
        Assert.Contains("AddScoped<TodoX.Web.Services.Studio.StaffTaskRepository>", Read("TodoX.Web", "Program.cs"));

        var layout = Read("TodoX.Web", "Components", "Layout", "MainLayout.razor");
        Assert.Contains("Item(\"staff_tasks\", \"Công việc nhân viên\", \"/staff-tasks\"", layout);
        Assert.Contains("if (!existingCodes.Contains(\"staff_tasks\"))", layout);
        Assert.Contains("\"staff_tasks\" => \"Công việc nhân viên\"", Read("TodoX.Web", "Components", "Pages", "Permissions.razor"));

        var page = Read("TodoX.Web", "Components", "Pages", "StaffTasks.razor");
        Assert.Contains("@page \"/staff-tasks\"", page);
        Assert.Contains("Chưa hoàn thành", page);
        Assert.Contains("Đã hoàn thành", page);
    }

    [Fact]
    public void NewTaskDefaultsToSignedInStaffWhenInList()
    {
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        PlanAssignee[] people = [new(me, "Tôi", true), new(other, "Khác", true)];

        Assert.Equal(me, StaffTasks.DefaultAssignee(null, me, people));
        Assert.Equal(other, StaffTasks.DefaultAssignee(other, me, people));
        Assert.Null(StaffTasks.DefaultAssignee(Guid.Empty, me, people));
        Assert.Null(StaffTasks.DefaultAssignee(null, Guid.NewGuid(), people));
        Assert.Null(StaffTasks.DefaultAssignee(null, null, people));
    }

    [Fact]
    public void EachStatusFilterHasItsOwnColor()
    {
        var keys = StaffTaskStatuses.All.Append("__late").ToList();
        var colors = keys.Select(StaffTasks.StatusHex).ToList();
        Assert.Equal(colors.Count, colors.Distinct().Count());
        Assert.Equal(StaffTasks.StatusHex(StaffTaskStatuses.Done), StaffTasks.StatusHex("done"));
        Assert.Equal("#EF4444", StaffTasks.StatusHex("__late"));
    }
}
