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

    private static StaffTask Task(string status, DateTime? due = null) => new() { Title = "x", Status = status, DueAt = due };

    [Fact]
    public void Statuses_have_vietnamese_labels_and_only_new_in_progress_are_open()
    {
        Assert.Equal(["new", "in_progress", "done", "cancelled"], StaffTaskStatuses.All);
        Assert.Equal(["Mới", "Đang thực hiện", "Đã hoàn thành", "Hủy"], StaffTaskStatuses.All.Select(StaffTaskStatuses.Label));
        Assert.True(StaffTaskStatuses.IsOpen("new"));
        Assert.True(StaffTaskStatuses.IsOpen("pending"));
        Assert.True(StaffTaskStatuses.IsOpen("in_progress"));
        Assert.False(StaffTaskStatuses.IsOpen("done"));
        Assert.False(StaffTaskStatuses.IsOpen("cancelled"));
        Assert.Equal("Mới", StaffTaskStatuses.Label("bogus"));
        Assert.Equal("Mới", StaffTaskStatuses.Label("pending"));
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
        Assert.Equal(2, summary.Count("new"));
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
        // An unfinished task has no completion time; its plan is the due time.
        Assert.Null(StaffTaskRules.CompletedAtFor("in_progress", entered, now));
        Assert.Equal(entered, StaffTaskRules.CompletedAtFor("cancelled", entered, now));

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
        Assert.NotEqual(StaffTasks.StatusHex("__open"), StaffTasks.StatusHex(StaffTaskStatuses.InProgress));
    }

    [Fact]
    public void Late_reads_the_due_time_not_the_completion_time()
    {
        var now = new DateTime(2026, 11, 25, 9, 0, 0);
        Assert.False(StaffTaskRules.IsLate(new StaffTask { Title = "x", Status = "in_progress", CompletedAt = now.AddDays(-1) }, now));
        Assert.True(StaffTaskRules.IsLate(new StaffTask { Title = "x", Status = "in_progress", DueAt = now.AddDays(-1) }, now));

        var start = new DateTime(2026, 11, 25, 8, 0, 0);
        Assert.NotNull(StaffTaskRules.Validate(new StaffTask { Title = "x", StartAt = start, DueAt = start.AddHours(-1) }));
        Assert.Null(StaffTaskRules.Validate(new StaffTask { Title = "x", StartAt = start, DueAt = start.AddHours(3) }));
    }

    [Fact]
    public void Comments_are_validated_and_status_changes_are_written_to_the_history()
    {
        Assert.NotNull(StaffTaskRules.ValidateComment("   "));
        Assert.NotNull(StaffTaskRules.ValidateComment(new string('a', StaffTaskComment.MaxLength + 1)));
        Assert.Null(StaffTaskRules.ValidateComment("Đã quay xong, đang dựng"));
        Assert.Equal("Đổi trạng thái: Mới → Đang thực hiện", StaffTaskRules.StatusChangeText("new", "in_progress"));

        var repo = Read("TodoX.Web", "Services", "Studio", "StaffTaskRepository.cs");
        Assert.Contains("Require(\"staff_tasks.comment\")", repo);
        Assert.Contains("StaffTaskComment.KindStatus", repo);
        Assert.Contains("StaffTaskComment.KindCreated", repo);
        Assert.Contains("if (isNew && task.AssignerUserId is null) task.AssignerUserId = user.UserId;", repo);
        Assert.Contains("t.due_at AS DueAt", repo);

        var sql = Read("database", "manual", "studio", "20261125_staff_task_assigner_due_comments.sql");
        Assert.Contains("add column if not exists assigner_user_id", sql);
        Assert.Contains("add column if not exists due_at", sql);
        Assert.Contains("create table if not exists studio.staff_task_comments", sql);
        Assert.Contains("'staff_tasks', 'comment'", sql);
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "studio", "20261125_staff_task_assigner_due_comments.test.sql")));

        var dialog = Read("TodoX.Web", "Components", "Dialogs", "StaffTaskDialog.razor");
        Assert.Contains("Label=\"Người giao việc\"", dialog);
        Assert.Contains("Label=\"Dự kiến hoàn thành\"", dialog);
        Assert.Contains("<StaffTaskHistory", dialog);
        var page = Read("TodoX.Web", "Components", "Pages", "StaffTasks.razor");
        Assert.Contains("<MudTh>Người giao</MudTh>", page);
        Assert.Contains("<MudTh>Dự kiến hoàn thành</MudTh>", page);
        Assert.Contains("StaffTaskHistoryDialog", page);
        Assert.Contains("\"comment\" => \"Phản hồi\"", Read("TodoX.Web", "Components", "Pages", "Permissions.razor"));
    }

    [Fact]
    public void New_tasks_default_to_eight_am_start_and_five_pm_due()
    {
        Assert.Equal(new TimeSpan(8, 0, 0), StaffTaskDialog.DefaultStartTime);
        Assert.Equal(new TimeSpan(17, 0, 0), StaffTaskDialog.DefaultDueTime);
        var dialog = Read("TodoX.Web", "Components", "Dialogs", "StaffTaskDialog.razor");
        Assert.Contains("_startTime = _model.StartAt?.TimeOfDay ?? DefaultStartTime;", dialog);
        Assert.Contains("_dueTime = _model.DueAt?.TimeOfDay ?? DefaultDueTime;", dialog);
    }

    [Fact]
    public void Late_is_automatic_and_counted_only_once()
    {
        var now = new DateTime(2026, 11, 19, 15, 0, 0);
        var summary = StaffTaskSummary.From(
            [Task("in_progress", now.AddHours(-1)), Task("in_progress", now.AddHours(1)), Task("new", now.AddDays(-1)), Task("done", now.AddDays(-1)), Task("cancelled", now.AddDays(-1))],
            now);
        Assert.Equal(2, summary.Late);
        Assert.Equal(1, summary.Count("in_progress"));
        Assert.Equal(0, summary.Count("new"));
        Assert.Equal(3, summary.Open);
        Assert.Equal(1, summary.Done);
        Assert.Equal(5, summary.Total);
    }
}
