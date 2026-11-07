using TodoX.Web.Components.Pages;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Email;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SignupAndEmailTests
{
    private static CustomerRegistration Valid() => new()
    {
        ContactName = "Nguyễn An",
        Email = "an@example.com",
        Phone = "0912 345 678",
        Password = "matkhau123",
        ConfirmPassword = "matkhau123"
    };

    [Fact]
    public void Registration_needs_name_valid_email_and_an_eight_character_password()
    {
        Assert.Null(AccountService.ValidateRegistration(Valid()));
        Assert.Null(AccountService.ValidateRegistration(new CustomerRegistration
        {
            ContactName = "An", Email = "an@example.com", Password = "12345678", ConfirmPassword = "12345678"
        }));

        var noName = Valid(); noName.ContactName = " ";
        var badEmail = Valid(); badEmail.Email = "an@";
        var namedEmail = Valid(); namedEmail.Email = "An <an@example.com>";
        var badPhone = Valid(); badPhone.Phone = "abc";
        var shortPassword = Valid(); shortPassword.Password = shortPassword.ConfirmPassword = "1234567";
        var mismatch = Valid(); mismatch.ConfirmPassword = "khac12345";

        Assert.Equal("Nhập họ tên.", AccountService.ValidateRegistration(noName));
        Assert.Equal("Email không hợp lệ.", AccountService.ValidateRegistration(badEmail));
        Assert.Equal("Email không hợp lệ.", AccountService.ValidateRegistration(namedEmail));
        Assert.Equal("Số điện thoại không hợp lệ.", AccountService.ValidateRegistration(badPhone));
        Assert.Equal("Mật khẩu cần ít nhất 8 ký tự.", AccountService.ValidateRegistration(shortPassword));
        Assert.Equal("Mật khẩu xác nhận không khớp.", AccountService.ValidateRegistration(mismatch));
    }

    [Theory]
    [InlineData("/my-editvideo", "/my-editvideo")]
    [InlineData("/my-editvideo?x=1", "/my-editvideo?x=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    public void Login_only_returns_to_local_paths(string? returnUrl, string expected) =>
        Assert.Equal(expected, Login.LocalReturnUrl(returnUrl));

    [Fact]
    public void Smtp_settings_are_checked_before_saving()
    {
        var ok = SmtpSettings.Empty with { Host = "smtp.gmail.com", FromEmail = "no-reply@todox.vn" };

        Assert.True(ok.IsConfigured);
        Assert.False(SmtpSettings.Empty.IsConfigured);
        Assert.Null(EmailService.Validate(ok));
        Assert.Equal("Nhập máy chủ SMTP.", EmailService.Validate(ok with { Host = "" }));
        Assert.Equal("Cổng SMTP không hợp lệ.", EmailService.Validate(ok with { Port = 70000 }));
        Assert.Equal("Kiểu bảo mật không hợp lệ.", EmailService.Validate(ok with { Security = "tls13" }));
        Assert.Equal("Email người gửi không hợp lệ.", EmailService.Validate(ok with { FromEmail = "todox" }));
        Assert.Equal("Email nhận thông báo không hợp lệ.", EmailService.Validate(ok with { NotifyEmail = "x@" }));
    }

    [Fact]
    public void Signup_mails_escape_what_the_customer_typed()
    {
        var welcome = SignupEmails.Welcome("<script>An</script>", "an@example.com");
        var notice = SignupEmails.Notice("An & co", "an@example.com", "0912", "TodoX EditVideo");

        Assert.Contains("&lt;script&gt;An&lt;/script&gt;", welcome);
        Assert.DoesNotContain("<script>", welcome);
        Assert.Contains("https://dashboard.todox.vn/my-editvideo", welcome);
        Assert.Contains("An &amp; co", notice);
        Assert.Contains("TodoX EditVideo", notice);
    }
}
