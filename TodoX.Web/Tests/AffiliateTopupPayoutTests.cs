using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AffiliateTopupPayoutTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string RepoFile(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));
    private static string Repository => RepoFile("TodoX.Web", "Services", "Affiliate", "AffiliateRepository.cs");

    [Fact]
    public void Pending_summary_adds_package_and_topup_commissions()
    {
        var summary = new AffiliatePendingSummary(2, 300_000m, 3, 150_000m);
        Assert.Equal(5, summary.Count);
        Assert.Equal(450_000m, summary.Amount);
    }

    [Fact]
    public void Topup_commissions_come_only_from_referred_customers_topups_once_each()
    {
        var sql = Repository;
        Assert.Contains("t.transaction_type = 'topup'", sql);
        Assert.Contains("t.created_at >= r.assigned_at", sql);
        Assert.Contains("s.vnd_per_point > 0", sql);
        Assert.Contains("ON CONFLICT (transaction_id) DO NOTHING", sql);
        Assert.Contains("COALESCE(a.topup_commission_percent, s.default_topup_commission_percent)", sql);
        // Billing is only read: no write to wallet or transaction tables.
        Assert.DoesNotContain("UPDATE billing.", sql);
        Assert.DoesNotContain("INSERT INTO billing.", sql);
    }

    [Fact]
    public void A_payout_marks_both_kinds_paid_and_can_be_cancelled()
    {
        var sql = Repository;
        Assert.Contains("INSERT INTO affiliate.payouts", sql);
        Assert.Contains("UPDATE affiliate.commissions", sql);
        Assert.Contains("UPDATE affiliate.topup_commissions", sql);
        Assert.Contains("SET status='cancelled'", sql);
        Assert.DoesNotContain("MarkCommissionPendingAsync", sql);
    }

    [Fact]
    public void Agent_sees_topups_and_payouts_and_edits_bank_in_profile()
    {
        var agentPage = RepoFile("TodoX.Web", "Components", "Pages", "MyCommissions.razor");
        Assert.Contains("Lịch sử nạp tiền của khách hàng", agentPage);
        Assert.Contains("Lịch sử nhận thanh toán từ TodoX", agentPage);
        Assert.Contains("SyncTopupCommissionsAsync", agentPage);
        Assert.DoesNotContain("Email", agentPage);

        var profile = RepoFile("TodoX.Web", "Components", "Pages", "Profile.razor");
        Assert.Contains("Tài khoản ngân hàng nhận hoa hồng", profile);
        Assert.Contains("UpdateOwnBankAsync(customerId", profile);
    }

    [Fact]
    public void Admin_can_configure_rates_and_record_payouts()
    {
        var admin = RepoFile("TodoX.Web", "Components", "Pages", "Affiliates.razor");
        Assert.Contains("Nạp điểm & hoa hồng", admin);
        Assert.Contains("Thanh toán hoa hồng", admin);
        Assert.Contains("Lịch sử thanh toán hoa hồng", admin);
        Assert.Contains("_editAgent.TopupCommissionPercent", admin);
    }

    [Fact]
    public void The_migration_is_additive_and_tested()
    {
        var sql = RepoFile("database", "manual", "affiliate", "20261110_affiliate_topup_payouts.sql");
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE billing", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE crm", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(Root, "database", "manual", "affiliate", "20261110_affiliate_topup_payouts.test.sql")));
    }
}
