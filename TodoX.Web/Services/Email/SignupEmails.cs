using System.Net;

namespace TodoX.Web.Services.Email;

/// <summary>Mails sent when a customer registers: a welcome to the customer and a notice to TodoX.</summary>
public sealed class SignupEmails
{
    public const string DashboardUrl = "https://dashboard.todox.vn";

    private readonly EmailService _email;
    private readonly ILogger<SignupEmails> _logger;

    public SignupEmails(EmailService email, ILogger<SignupEmails> logger)
    {
        _email = email;
        _logger = logger;
    }

    /// <summary>Sends both mails when SMTP is set up. Never throws: a mail problem must not undo a registration.</summary>
    public async Task SendAsync(string name, string email, string? phone, string? source)
    {
        try
        {
            var settings = await _email.GetAsync();
            if (!settings.IsConfigured)
            {
                return;
            }

            if (settings.WelcomeOnRegister)
            {
                await _email.SendAsync(settings, email, "Chào mừng bạn đến với TodoX", Welcome(name, email));
            }

            if (!string.IsNullOrWhiteSpace(settings.NotifyEmail))
            {
                await _email.SendAsync(settings, settings.NotifyEmail, $"Khách mới đăng ký: {name}", Notice(name, email, phone, source));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signup emails for {Email} failed", email);
        }
    }

    public static string Welcome(string name, string email) => Layout($"""
        <p>Chào {Html(name)},</p>
        <p>Tài khoản TodoX của bạn đã được tạo với email <b>{Html(email)}</b>.</p>
        <p>Để dùng TodoX EditVideo, đăng nhập dashboard, chọn gói trong mục <b>TodoX EditVideo của tôi</b>, rồi dán mã license vào ứng dụng trên máy.</p>
        <p style="margin:24px 0"><a href="{DashboardUrl}/my-editvideo" style="background:#FFBE2E;color:#0B0D10;padding:10px 20px;border-radius:6px;text-decoration:none;font-weight:bold">Mở tài khoản TodoX</a></p>
        <p style="color:#666;font-size:13px">Nếu bạn không đăng ký tài khoản này, hãy bỏ qua email.</p>
        """);

    public static string Notice(string name, string email, string? phone, string? source) => Layout($"""
        <p>Có khách mới đăng ký tài khoản TodoX.</p>
        <table style="font-size:14px">
          <tr><td style="color:#666;padding-right:12px">Tên</td><td>{Html(name)}</td></tr>
          <tr><td style="color:#666;padding-right:12px">Email</td><td>{Html(email)}</td></tr>
          <tr><td style="color:#666;padding-right:12px">Điện thoại</td><td>{Html(phone)}</td></tr>
          <tr><td style="color:#666;padding-right:12px">Nguồn</td><td>{Html(source ?? "dashboard")}</td></tr>
        </table>
        """);

    private static string Layout(string body) => $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:0 auto;color:#1a1a1a">
          <div style="background:#0B0D10;color:#FFBE2E;padding:14px 20px;font-weight:bold;letter-spacing:1px">TODO EXCELLENCE</div>
          <div style="padding:20px;border:1px solid #eee;border-top:none;font-size:15px;line-height:1.6">{body}</div>
        </div>
        """;

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");
}
