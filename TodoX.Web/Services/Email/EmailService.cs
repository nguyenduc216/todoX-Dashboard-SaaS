using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Email;

/// <summary>Outgoing mail (SMTP) settings, kept in system.app_settings under the "email" group.</summary>
public sealed record SmtpSettings(
    string Host,
    int Port,
    string Security,
    string? Username,
    string? Password,
    string FromEmail,
    string? FromName,
    string? NotifyEmail,
    bool WelcomeOnRegister)
{
    public static readonly string[] SecurityModes = ["auto", "ssl", "starttls", "none"];

    public static SmtpSettings Empty => new("", 587, "auto", null, null, "", "TodoX", null, true);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(FromEmail);
}

/// <summary>
/// Reads/saves the SMTP settings and sends mail with them. The password is a secret: it is never handed back
/// to the UI (only whether one is stored), and saving with a blank password keeps the stored one.
/// </summary>
public sealed class EmailService
{
    public const string KeyHost = "email.smtp.host";
    public const string KeyPort = "email.smtp.port";
    public const string KeySecurity = "email.smtp.security";
    public const string KeyUsername = "email.smtp.username";
    public const string KeyPassword = "email.smtp.password";
    public const string KeyFromEmail = "email.from_email";
    public const string KeyFromName = "email.from_name";
    public const string KeyNotifyEmail = "email.notify_email";
    public const string KeyWelcomeOnRegister = "email.welcome_on_register";

    private readonly TodoXConnectionFactory _factory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(TodoXConnectionFactory factory, ILogger<EmailService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<SmtpSettings> GetAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = (await conn.QueryAsync<(string Key, string? Value)>(
            "SELECT setting_key, setting_value FROM system.app_settings WHERE setting_group = 'email' AND is_active;"))
            .ToDictionary(r => r.Key, r => r.Value);
        string? Get(string key) => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var empty = SmtpSettings.Empty;
        return new SmtpSettings(
            Get(KeyHost) ?? empty.Host,
            int.TryParse(Get(KeyPort), out var port) ? port : empty.Port,
            Get(KeySecurity) ?? empty.Security,
            Get(KeyUsername),
            Get(KeyPassword),
            Get(KeyFromEmail) ?? empty.FromEmail,
            Get(KeyFromName) ?? empty.FromName,
            Get(KeyNotifyEmail),
            Get(KeyWelcomeOnRegister) is not { } w || w == "true");
    }

    /// <summary>Saves the settings; a null or blank password keeps the stored one.</summary>
    public async Task<(bool Success, string Message)> SaveAsync(SmtpSettings s)
    {
        var error = Validate(s);
        if (error is not null)
        {
            return (false, error);
        }

        using var conn = await _factory.OpenAsync();
        async Task Set(string key, string? value, string type, bool secret, string description) =>
            await conn.ExecuteAsync(
                """
                INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_secret, is_active, created_at)
                VALUES (gen_random_uuid(), @key, 'email', @type, @value, @description, @secret, true, now())
                ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, setting_type = EXCLUDED.setting_type,
                    setting_group = 'email', is_secret = EXCLUDED.is_secret, is_active = true, updated_at = now();
                """, new { key, value = value?.Trim() ?? "", type, secret, description });

        await Set(KeyHost, s.Host, "string", false, "Máy chủ SMTP");
        await Set(KeyPort, s.Port.ToString(), "number", false, "Cổng SMTP");
        await Set(KeySecurity, s.Security, "string", false, "Bảo mật SMTP: auto | ssl | starttls | none");
        await Set(KeyUsername, s.Username, "string", false, "Tài khoản đăng nhập SMTP");
        if (!string.IsNullOrWhiteSpace(s.Password))
        {
            await Set(KeyPassword, s.Password, "secret", true, "Mật khẩu SMTP");
        }

        await Set(KeyFromEmail, s.FromEmail, "string", false, "Email người gửi");
        await Set(KeyFromName, s.FromName, "string", false, "Tên người gửi");
        await Set(KeyNotifyEmail, s.NotifyEmail, "string", false, "Email nhận thông báo khi có khách đăng ký");
        await Set(KeyWelcomeOnRegister, s.WelcomeOnRegister ? "true" : "false", "boolean", false, "Gửi email chào mừng khi khách đăng ký");
        return (true, "Đã lưu cấu hình email.");
    }

    public static string? Validate(SmtpSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            return "Nhập máy chủ SMTP.";
        }

        if (s.Port is < 1 or > 65535)
        {
            return "Cổng SMTP không hợp lệ.";
        }

        if (!SmtpSettings.SecurityModes.Contains(s.Security))
        {
            return "Kiểu bảo mật không hợp lệ.";
        }

        if (!IsEmail(s.FromEmail))
        {
            return "Email người gửi không hợp lệ.";
        }

        if (!string.IsNullOrWhiteSpace(s.NotifyEmail) && !IsEmail(s.NotifyEmail))
        {
            return "Email nhận thông báo không hợp lệ.";
        }

        return null;
    }

    /// <summary>A bare address with a domain ("a@b.vn"), not a display-name form.</summary>
    public static bool IsEmail(string? value) =>
        System.Net.Mail.MailAddress.TryCreate(value?.Trim(), out var mail) && mail.Address == value!.Trim() && mail.Host.Contains('.');

    /// <summary>Sends one HTML mail with the stored settings.</summary>
    public async Task<(bool Success, string Message)> SendAsync(string to, string subject, string html, CancellationToken ct = default) =>
        await SendAsync(await GetAsync(), to, subject, html, ct);

    public async Task<(bool Success, string Message)> SendAsync(SmtpSettings s, string to, string subject, string html, CancellationToken ct = default)
    {
        if (!s.IsConfigured)
        {
            return (false, "Chưa cấu hình email (SMTP).");
        }

        if (!IsEmail(to))
        {
            return (false, "Email người nhận không hợp lệ.");
        }

        var recipient = new MailboxAddress("", to!.Trim());

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(s.FromName) ? "TodoX" : s.FromName, s.FromEmail));
        message.To.Add(recipient);
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        try
        {
            using var client = new SmtpClient { Timeout = 20000 };
            await client.ConnectAsync(s.Host, s.Port, SocketOptions(s.Security), ct);
            if (!string.IsNullOrWhiteSpace(s.Username))
            {
                await client.AuthenticateAsync(s.Username, s.Password ?? "", ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return (true, $"Đã gửi email tới {recipient.Address}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "SMTP send to {To} via {Host}:{Port} failed", recipient.Address, s.Host, s.Port);
            return (false, $"Gửi email lỗi: {ex.Message}");
        }
    }

    private static SecureSocketOptions SocketOptions(string security) => security switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        "none" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
