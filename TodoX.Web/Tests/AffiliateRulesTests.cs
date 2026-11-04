using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Affiliate;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AffiliateRulesTests
{
    [Theory]
    [InlineData(1_000_000, 10, 100_000)]
    [InlineData(499_000, 15, 74_850)]
    [InlineData(333_333, 12.5, 41_667)]
    [InlineData(1_000_000, 0, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(1_000_000, 150, 1_000_000)]
    public void CalculateCommission_AppliesPercentAndRoundsToDong(decimal amount, decimal percent, decimal expected)
    {
        Assert.Equal(expected, AffiliateRules.CalculateCommission(amount, percent));
    }

    [Fact]
    public void GenerateAgentCode_UsesPrefixAndUnambiguousCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = AffiliateRules.GenerateAgentCode();
            Assert.StartsWith(AffiliateRules.AgentCodePrefix, code);
            Assert.Equal(8, code.Length);
            Assert.DoesNotContain('0', code[2..]);
            Assert.DoesNotContain('O', code[2..]);
            Assert.DoesNotContain('1', code[2..]);
            Assert.DoesNotContain('I', code[2..]);
        }
    }

    [Fact]
    public void NormalizeCode_TrimsAndUppercases()
    {
        Assert.Equal("DLABC234", AffiliateRules.NormalizeCode("  dlabc234 "));
        Assert.Equal(string.Empty, AffiliateRules.NormalizeCode(null));
    }

    [Fact]
    public void ResolveCycleStart_ChainsRenewalOntoActiveCycle()
    {
        var purchasedAt = new DateTime(2026, 11, 4, 10, 0, 0);

        // Còn hạn đến 20/11 → gia hạn nối tiếp từ 20/11.
        Assert.Equal(new DateTime(2026, 11, 20),
            AffiliateRules.ResolveCycleStart(null, new DateTime(2026, 11, 20), purchasedAt));

        // Đã hết hạn → bắt đầu từ ngày mua.
        Assert.Equal(new DateTime(2026, 11, 4),
            AffiliateRules.ResolveCycleStart(null, new DateTime(2026, 10, 1), purchasedAt));

        // Lần mua đầu tiên → ngày mua.
        Assert.Equal(new DateTime(2026, 11, 4), AffiliateRules.ResolveCycleStart(null, null, purchasedAt));

        // Admin chọn ngày cụ thể → giữ nguyên.
        Assert.Equal(new DateTime(2026, 12, 1),
            AffiliateRules.ResolveCycleStart(new DateTime(2026, 12, 1, 15, 0, 0), new DateTime(2026, 11, 20), purchasedAt));
    }

    [Fact]
    public void ResolveCycleEnd_AddsMonthsOrIsOpenEnded()
    {
        Assert.Equal(new DateTime(2027, 2, 28), AffiliateRules.ResolveCycleEnd(new DateTime(2026, 11, 28), 3));
        Assert.Null(AffiliateRules.ResolveCycleEnd(new DateTime(2026, 11, 28), 0));
    }

    [Fact]
    public void AffiliatesPage_IsAdminOnly()
    {
        Assert.True(NavigationAccessRules.IsAdminOnlyPath("/affiliates"));
        var customer = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner };
        Assert.False(NavigationAccessRules.CanAccessPath(customer, "/affiliates"));
    }

    [Fact]
    public void MyCommissionsPage_IsOpenToCustomersAndScopedToOwnCustomer()
    {
        Assert.False(NavigationAccessRules.IsAdminOnlyPath("/my-commissions"));
        var customer = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner, CustomerId = Guid.NewGuid() };
        Assert.True(NavigationAccessRules.CanAccessPath(customer, "/my-commissions"));

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var web = File.Exists(Path.Combine(root, "Components", "Pages", "MyCommissions.razor")) ? root : Path.Combine(root, "TodoX.Web");
        var page = File.ReadAllText(Path.Combine(web, "Components", "Pages", "MyCommissions.razor"));

        // Agent data must come only from the logged-in customer's own agent record.
        Assert.Contains("GetAgentByCustomerAsync(customerId)", page);
        Assert.Contains("GetCommissionsAsync(_agent.Id)", page);
        Assert.DoesNotContain("GetAgentsAsync()", page);
        Assert.DoesNotContain("SupplyParameterFromQuery", page);
    }

    [Fact]
    public void CommissionStatusLabels_CoverBankingStates()
    {
        Assert.Equal("Chưa chuyển khoản", AffiliateCommissionStatuses.Label(AffiliateCommissionStatuses.Pending));
        Assert.Equal("Đã chuyển khoản", AffiliateCommissionStatuses.Label(AffiliateCommissionStatuses.Paid));
    }
}
