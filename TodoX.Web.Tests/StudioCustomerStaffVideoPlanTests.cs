using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Studio;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Loại khách, nhân viên TodoX và kế hoạch làm video cho khách.</summary>
public sealed class StudioCustomerStaffVideoPlanTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Theory]
    [InlineData("vip", "vip", "VIP")]
    [InlineData("VIP", "vip", "VIP")]
    [InlineData("bam_duoi", "bam_duoi", "Bám đuổi")]
    [InlineData("thong_thuong", "thong_thuong", "Thông thường")]
    [InlineData(null, "thong_thuong", "Thông thường")]
    [InlineData("bogus", "thong_thuong", "Thông thường")]
    public void Customer_segment_has_three_types_and_defaults_to_regular(string? raw, string normalized, string label)
    {
        Assert.Equal(normalized, CustomerSegments.Normalize(raw));
        Assert.Equal(label, CustomerSegments.Label(raw));
    }

    [Fact]
    public void Videos_done_count_delivered_videos_and_released_plans_but_not_work_in_progress()
    {
        var counts = new CustomerVideoCounts(Delivered: 3, Released: 2, InProgress: 4);
        Assert.Equal(5, counts.Done);
    }

    [Fact]
    public void A_plan_needs_a_customer_and_its_content()
    {
        var plan = new VideoPlan { CustomerId = Guid.NewGuid(), Title = "Video Tết", Status = VideoPlanStatuses.Planning };
        Assert.Null(VideoPlanRules.Validate(plan));
        Assert.NotNull(VideoPlanRules.Validate(new VideoPlan { Title = "x" }));
        Assert.NotNull(VideoPlanRules.Validate(new VideoPlan { CustomerId = Guid.NewGuid(), Title = "  " }));
        Assert.NotNull(VideoPlanRules.Validate(new VideoPlan { CustomerId = Guid.NewGuid(), Title = new string('a', 301) }));
        Assert.NotNull(VideoPlanRules.Validate(new VideoPlan { CustomerId = Guid.NewGuid(), Title = "x", Status = "bogus" }));
    }

    [Fact]
    public void A_version_needs_a_link_or_a_change_note_and_only_web_links()
    {
        Assert.NotNull(VideoPlanRules.ValidateVersion(new VideoPlanVersion()));
        Assert.Null(VideoPlanRules.ValidateVersion(new VideoPlanVersion { ChangeNote = "Đổi nhạc" }));
        Assert.Null(VideoPlanRules.ValidateVersion(new VideoPlanVersion { VideoUrl = "https://drive.google.com/x" }));
        Assert.NotNull(VideoPlanRules.ValidateVersion(new VideoPlanVersion { VideoUrl = "javascript:alert(1)" }));
        Assert.NotNull(VideoPlanRules.ValidateVersion(new VideoPlanVersion { VideoUrl = "drive/x" }));
    }

    [Fact]
    public void Only_open_plans_past_their_release_date_are_overdue()
    {
        var today = new DateTime(2026, 11, 16);
        Assert.True(VideoPlanRules.IsOverdue(new VideoPlan { ReleaseDate = today.AddDays(-1), Status = VideoPlanStatuses.Producing }, today));
        Assert.False(VideoPlanRules.IsOverdue(new VideoPlan { ReleaseDate = today, Status = VideoPlanStatuses.Producing }, today));
        Assert.False(VideoPlanRules.IsOverdue(new VideoPlan { ReleaseDate = today.AddDays(-1), Status = VideoPlanStatuses.Released }, today));
        Assert.False(VideoPlanRules.IsOverdue(new VideoPlan { ReleaseDate = today.AddDays(-1), Status = VideoPlanStatuses.Cancelled }, today));
        Assert.False(VideoPlanRules.IsOverdue(new VideoPlan { Status = VideoPlanStatuses.Producing }, today));
    }

    [Fact]
    public void A_staff_login_is_neither_an_admin_nor_a_customer()
    {
        Assert.Equal("staff", AccountRepository.RoleCode(TodoXUserRole.Staff));
        var role = AccountRepository.RoleFromLoginCode("admin", "staff");
        Assert.Equal(TodoXUserRole.Staff, role);

        var session = new CurrentUserSession { IsAuthenticated = true, Role = role, Permissions = new HashSet<string> { "video_plans.view" } };
        Assert.False(AdminEndpointAuthorization.IsAdmin(session));
        Assert.False(session.IsCustomer);
        Assert.True(session.CanViewModule("video_plans"));
        Assert.False(session.CanViewModule("staff_members"));
        // Existing operators keep their mapping.
        Assert.Equal(TodoXUserRole.SystemOperator, AccountRepository.RoleFromLoginCode("admin", "support"));
    }

    [Theory]
    [InlineData("/staff")]
    [InlineData("/video-plans")]
    [InlineData("/video-plans/7d1e7c8e-0000-0000-0000-000000000000")]
    public void Customers_cannot_open_the_staff_and_plan_pages(string path)
        => Assert.True(NavigationAccessRules.IsAdminOnlyPath(path));

    [Fact]
    public void Migration_is_additive_and_ships_with_its_test()
    {
        var sql = Read("database", "manual", "studio", "20261116_customer_segments_staff_video_plans.sql");
        foreach (var table in new[] { "studio.customer_profiles", "studio.staff_members", "studio.video_plans", "studio.video_plan_versions", "studio.video_plan_notes" })
            Assert.Contains($"create table if not exists {table}", sql);
        Assert.Contains("check (segment in ('bam_duoi', 'thong_thuong', 'vip'))", sql);
        // The staff role copies the support row's shape instead of guessing the role table's columns.
        Assert.Contains("jsonb_populate_record(null::auth.roles, r)", sql);
        Assert.DoesNotContain("alter table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "studio", "20261116_customer_segments_staff_video_plans.test.sql")));
    }

    /// <summary>
    /// Bug 2026-11-16: opening "Thêm nhân viên" let the browser's password manager put the saved admin login
    /// into the page search box (the list showed "Chưa có nhân viên nào") and into the new account's username,
    /// so the second staff account failed with "Username đã tồn tại".
    /// </summary>
    [Fact]
    public void Adding_staff_is_not_hijacked_by_browser_autofill()
    {
        var dialog = Read("TodoX.Web", "Components", "Dialogs", "StaffMemberDialog.razor");
        Assert.Contains("autocomplete=\"new-password\"", dialog);
        Assert.Contains("name=\"staff-new-username\"", dialog);
        Assert.Contains("autocomplete=\"off\" name=\"staff-search\"", Read("TodoX.Web", "Components", "Pages", "StaffMembers.razor").Replace("\r\n", "\n").Replace("\n                          ", " "));

        // A duplicate phone is reported in words instead of a database error.
        var service = Read("TodoX.Web", "Services", "AccountService.cs");
        Assert.Contains("Mỗi số chỉ dùng cho một tài khoản đăng nhập", service); // admin accounts still check it
        Assert.Contains("STAFF_SAVE_FAILED", Read("TodoX.Web", "Components", "Pages", "StaffMembers.razor"));
    }

    /// <summary>The wide customer list scrolls sideways instead of wrapping names and stacking the action buttons.</summary>
    [Fact]
    public void Wide_tables_keep_one_line_per_row_and_scroll_sideways()
    {
        foreach (var page in new[] { "Customers.razor", "StaffMembers.razor", "VideoPlans.razor" })
        {
            var razor = Read("TodoX.Web", "Components", "Pages", page);
            Assert.Contains("todox-table-nowrap", razor);
            Assert.Contains("HorizontalScrollbar=\"true\"", razor);
            Assert.Contains("class=\"todox-table-actions\"", razor);
        }
        var css = Read("TodoX.Web", "wwwroot", "css", "todox-theme.css");
        Assert.Contains(".todox-table-nowrap td { white-space: nowrap; }", css);
        Assert.Contains("flex-wrap: nowrap;", css);

        // Customer list: customer column pinned left, actions pinned right, header fixed, and the table box is as
        // tall as the screen so its horizontal scrollbar is visible without scrolling to the last row.
        var customers = Read("TodoX.Web", "Components", "Pages", "Customers.razor");
        Assert.Contains("todox-table-sticky\" HorizontalScrollbar=\"true\" FixedHeader=\"true\"", customers);
        Assert.Contains("<MudTh>Khách hàng</MudTh>", customers);
        Assert.Contains("max-height: calc(100vh - 170px);", css);
        Assert.Contains(".todox-table-sticky td:first-child", css);
        Assert.Contains(".todox-table-sticky td:last-child", css);
    }

    /// <summary>User (2026-11-16): for staff only the username must be unique; phone/email may be shared.</summary>
    [Fact]
    public void Staff_accounts_only_require_a_unique_username()
    {
        var service = Read("TodoX.Web", "Services", "AccountService.cs");
        var method = service[service.IndexOf("public async Task<StaffAccountSaveResult> SaveStaffAccountAsync", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("internal static string PhoneTakenMessage", StringComparison.Ordinal)];
        Assert.Contains("UsernameExistsAsync", method);
        // Phone / email collisions never block the save: the value goes to the profile instead of the login.
        Assert.DoesNotContain("PhoneTakenMessage", method);
        Assert.DoesNotContain("Email đã tồn tại", method);
        Assert.Contains("Email = emailOnLogin ? email : string.Empty", method);
        Assert.Contains("Phone = phoneOnLogin ? phone : string.Empty", method);

        Assert.Contains("Accounts.SaveStaffAccountAsync(account)", Read("TodoX.Web", "Components", "Pages", "StaffMembers.razor"));
        Assert.Contains("COALESCE(NULLIF(s.phone, ''), u.phone, '') AS Phone", Read("TodoX.Web", "Services", "Studio", "StudioRepository.cs"));
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "studio", "20261116_staff_contact_fields.test.sql")));
    }

    /// <summary>A phone already used by another login names that account, so the admin knows where to change it.</summary>
    [Fact]
    public void A_taken_phone_says_which_account_holds_it()
    {
        var customer = AccountService.PhoneTakenMessage("0888578074", "Nguyễn Văn A", "a@x.vn", "customer");
        Assert.Contains("0888578074", customer);
        Assert.Contains("tài khoản khách hàng Nguyễn Văn A (a@x.vn)", customer);
        Assert.Contains("để trống số điện thoại", customer);

        Assert.Contains("tài khoản quản trị / nhân viên b@x.vn.", AccountService.PhoneTakenMessage("0901", null, "b@x.vn", "admin"));
        Assert.Contains("FindPhoneOwnerAsync(model.Phone", Read("TodoX.Web", "Services", "AccountService.cs"));
    }

    /// <summary>Rule (user, 2026-11-16): the browser must never auto-fill a saved login into a dashboard form.
    /// Only the login page keeps auto-fill.</summary>
    [Fact]
    public void No_password_box_outside_the_login_page_accepts_browser_autofill()
    {
        var components = Path.Combine(Root, "TodoX.Web", "Components");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories))
        {
            if (file.EndsWith(Path.Combine("Pages", "Login.razor"), StringComparison.OrdinalIgnoreCase)) continue;
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(text, @"<MudTextField\b[^>]*?/?>", System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                if (tag.Value.Contains("InputType=\"InputType.Password\"") && !tag.Value.Contains("autocomplete=\"new-password\""))
                    offenders.Add(Path.GetFileName(file));
            }
        }
        Assert.Empty(offenders);

        // Account fields beside a password box are not filled either.
        Assert.Contains("Label=\"Username\" @bind-Value=\"_model.Username\" Variant=\"Variant.Outlined\" Required=\"true\" autocomplete=\"off\"",
            Read("TodoX.Web", "Components", "Dialogs", "SystemUserDialog.razor"));
        Assert.Contains("Label=\"Email đăng nhập\" @bind-Value=\"_model.Email\" Variant=\"Variant.Outlined\" Required=\"true\" autocomplete=\"off\"",
            Read("TodoX.Web", "Components", "Dialogs", "CustomerAccountDialog.razor"));
        Assert.Contains("Label=\"Tài khoản đăng nhập\" Variant=\"Variant.Outlined\" autocomplete=\"off\"",
            Read("TodoX.Web", "Components", "Pages", "Settings.razor"));
    }

    [Fact]
    public void Pages_menu_and_customer_list_are_wired()
    {
        var layout = Read("TodoX.Web", "Components", "Layout", "MainLayout.razor");
        Assert.Contains("\"/staff\"", layout);
        Assert.Contains("\"/video-plans\"", layout);
        Assert.Contains("\"admin_or_module\"", layout);

        var customers = Read("TodoX.Web", "Components", "Pages", "Customers.razor");
        Assert.Contains("<MudTh>Loại khách</MudTh>", customers);
        Assert.Contains("<MudTh>Video đã làm</MudTh>", customers);
        // Admins keep every action; a staff account only gets what its role grants.
        Assert.Contains("@if (CanDelete)", customers);

        // A staff account is edited on its own page; the admin dialog would turn it into an operator.
        Assert.Contains("!string.Equals(r.role_code, \"staff\"", Read("TodoX.Web", "Services", "AccountRepository.cs"));
        Assert.Contains("AddScoped<TodoX.Web.Services.Studio.StudioRepository>()", Read("TodoX.Web", "Program.cs"));
        Assert.Contains("@page \"/video-plans/{PlanId:guid}\"", Read("TodoX.Web", "Components", "Pages", "VideoPlanDetail.razor"));
    }
}
