using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Thêm khách hàng: tài khoản đăng nhập đặt theo tên thương hiệu, ví điểm tạo ngay để hiện trong "Quản lý điểm".</summary>
public sealed class CustomerLoginNamesTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Theory]
    [InlineData("Kesa Spa", "kesaspa")]
    [InlineData("Chè Yến Nha Trang", "cheyennhatrang")]
    [InlineData("Đà Lạt Hồng", "dalathong")]
    [InlineData("F20", "f20")]
    [InlineData("MSB Nha Trang!", "msbnhatrang")]
    public void Username_follows_brand_name(string brand, string expected)
        => Assert.Equal(expected, CustomerLoginNames.FromBrand(brand));

    [Fact]
    public void Empty_brand_falls_back_to_contact_then_default_and_is_capped()
    {
        Assert.Equal("anhthien", CustomerLoginNames.FromBrand("  ", "Anh Thiện"));
        Assert.Equal("khachhang", CustomerLoginNames.FromBrand("***", null));
        Assert.Equal(CustomerLoginNames.MaxLength, CustomerLoginNames.FromBrand(new string('a', 80)).Length);
    }

    [Fact]
    public async Task Taken_name_gets_a_number()
    {
        var taken = new HashSet<string> { "kesaspa", "kesaspa2" };
        Assert.Equal("kesaspa3", await CustomerLoginNames.FirstFreeAsync("kesaspa", u => Task.FromResult(taken.Contains(u))));
        Assert.Equal("tara", await CustomerLoginNames.FirstFreeAsync("tara", _ => Task.FromResult(false)));
        var longName = new string('a', CustomerLoginNames.MaxLength);
        var next = await CustomerLoginNames.FirstFreeAsync(longName, u => Task.FromResult(u == longName));
        Assert.Equal(CustomerLoginNames.MaxLength, next.Length);
        Assert.EndsWith("2", next);
    }

    [Fact]
    public void Generated_password_is_long_enough_and_random()
    {
        var a = CustomerLoginNames.NewPassword();
        Assert.True(a.Length >= 8);
        Assert.NotEqual(a, CustomerLoginNames.NewPassword());
    }

    [Fact]
    public void New_customer_gets_login_and_wallet()
    {
        var dialog = Read("TodoX.Web", "Components", "Dialogs", "CustomerDialog.razor");
        Assert.Contains("CreateLogin = true", dialog);
        Assert.Contains("autocomplete=\"new-password\"", dialog);
        Assert.Contains("InsertCustomerWithLoginAsync(model)", Read("TodoX.Web", "Services", "AccountService.cs"));
        Assert.Contains("await EnsureWalletsAsync();", Read("TodoX.Web", "Components", "Pages", "Customers.razor"));
        Assert.Contains("WalletService.SeedCustomerWalletsAsync()", Read("TodoX.Web", "Components", "Pages", "Wallets.razor"));
    }
}
