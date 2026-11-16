using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>One stat / block on the dashboard home page, shown only to accounts allowed to see it.</summary>
public sealed record HomeWidget(string Module, string Title, string Description);

/// <summary>
/// Every block on the dashboard home page has its own permission "&lt;module&gt;.view", granted per role on
/// "Phân quyền" (row "Trang chủ · &lt;title&gt;"). Admin accounts see every block; a staff account sees only
/// the blocks its role was granted.
///
/// RULE for new stats/features on the home page: add the entry here, render it behind
/// <see cref="CanSee"/>, and ship a database/manual SQL that inserts its auth.permissions row (module = the
/// entry's Module, action 'view') and grants it to the admin roles. A test fails when an entry has no SQL.
/// </summary>
public static class HomeWidgets
{
    public const string Customers = "home_customers";
    public const string Accounts = "home_accounts";
    public const string PointsSold = "home_points_sold";
    public const string RenderJobs = "home_render_jobs";
    public const string MyVideoPlans = "home_my_video_plans";
    public const string VideoPlans = "home_video_plans";
    public const string Roadmap = "home_roadmap";

    public static readonly IReadOnlyList<HomeWidget> All =
    [
        new(Customers, "Khách hàng", "Số khách hàng và số khách VIP / bám đuổi."),
        new(Accounts, "Tài khoản", "Số tài khoản quản trị và tài khoản khách hàng."),
        new(PointsSold, "Điểm đã bán", "Tổng điểm đã bán cho khách."),
        new(RenderJobs, "Render Jobs", "Số job render."),
        new(MyVideoPlans, "Kế hoạch video của tôi", "Kế hoạch video đang giao cho chính tài khoản, sắp đến ngày release."),
        new(VideoPlans, "Tổng quan kế hoạch video", "Kế hoạch đang làm, sắp release trong 7 ngày, trễ hạn."),
        new(Roadmap, "Lộ trình Sprint", "Ghi chú lộ trình phát triển nội bộ.")
    ];

    public static HomeWidget? Find(string module) => All.FirstOrDefault(x => x.Module == module);

    public static string Permission(string module) => module + ".view";

    public static bool CanSee(CurrentUserSession? user, string module)
        => user?.IsAuthenticated == true && !user.IsCustomer
           && (AdminEndpointAuthorization.IsAdmin(user) || user.Can(Permission(module)));
}
