using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>What the customer agrees to pay; the desktop app shows it in its own confirmation window.</summary>
public sealed record EditVideoPriceQuote(
    string QuoteId, string Kind, string Model, string Summary, decimal Points, decimal Vnd, decimal Balance, int ExpiresInSeconds);

/// <summary>Who agreed, how (the app's own window, never the AI assistant) and on which machine.</summary>
public sealed record EditVideoConsentInfo(string? Method, string? WindowsUser, string? AppVersion);

/// <summary>A valid consent, written to the log once the paid task exists.</summary>
public sealed record EditVideoConsentTicket(EditVideoPriceQuote Quote, EditVideoConsentInfo Info);

/// <summary>
/// Price confirmation for every paid EditVideo command (AI image, video, music, voice). The price always comes from
/// TodoX: the first call returns a quote ("confirm_required") instead of doing anything; the desktop app shows that
/// price to the person in its own window and only on "Đồng ý" calls again with the quote id. The quote is single
/// use, expires after 10 minutes, belongs to one license and one exact request, and is void if the price changed.
/// Quotes and consents are logged in editvideo.license_events (ai_price_quote / ai_price_consent).
/// </summary>
public sealed class EditVideoConsentService
{
    public const string ConfirmRequired = "confirm_required";

    /// <summary>First desktop version that shows the confirmation window; it is always asked to confirm.</summary>
    public const string FirstConsentAppVersion = "1.1.3";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IMemoryCache _cache;
    private readonly EditVideoAiSettingsService? _settings;
    private readonly IHttpContextAccessor? _http;
    private readonly ILogger<EditVideoConsentService>? _logger;

    public EditVideoConsentService(TodoXConnectionFactory factory, TenantContext tenant, IMemoryCache cache,
        EditVideoAiSettingsService? settings = null, IHttpContextAccessor? http = null, ILogger<EditVideoConsentService>? logger = null)
    {
        _factory = factory;
        _tenant = tenant;
        _cache = cache;
        _settings = settings;
        _http = http;
        _logger = logger;
    }

    private sealed record Pending(Guid LicenseId, string Kind, string Fingerprint, EditVideoPriceQuote Quote);

    /// <summary>Stable hash of everything that decides what is made and what it costs.</summary>
    public static string Fingerprint(params object?[] parts)
    {
        var json = JsonSerializer.Serialize(parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32];
    }

    /// <summary>Apps from 1.1.3 always confirm; older apps only when the admin switched it on (they cannot show the window).</summary>
    public async Task<bool> RequiredAsync(CancellationToken ct = default)
    {
        var version = _http?.HttpContext?.Request.Headers[EditVideoAppVersionPolicy.HeaderName].FirstOrDefault();
        if (!EditVideoAppVersionPolicy.IsBelowMinimum(version, FirstConsentAppVersion) && !string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        return _settings is not null && (await _settings.GetAsync(ct)).RequireConsent;
    }

    /// <summary>
    /// Null ticket + a quote to show when the person has not agreed to this exact price yet; a ticket when the quote
    /// id matches. Not required → (ticket with no quote id, null).
    /// </summary>
    public async Task<(EditVideoConsentTicket? Ticket, EditVideoPriceQuote? Quote)> CheckAsync(
        EditVideoLicenseRow license, EditVideoDeviceRow device, string kind, string model, string summary, string fingerprint,
        decimal points, decimal vndPerPoint, decimal balance, string? confirmQuoteId, EditVideoConsentInfo? info, CancellationToken ct = default)
    {
        if (!await RequiredAsync(ct))
        {
            return (new EditVideoConsentTicket(new EditVideoPriceQuote("", kind, model, summary, points, points * vndPerPoint, balance, 0),
                info ?? new EditVideoConsentInfo("not_required", null, null)), null);
        }

        if (!string.IsNullOrWhiteSpace(confirmQuoteId)
            && _cache.TryGetValue(Key(confirmQuoteId.Trim()), out Pending? pending) && pending is not null
            && pending.LicenseId == license.Id && pending.Kind == kind && pending.Fingerprint == fingerprint && pending.Quote.Points == points)
        {
            _cache.Remove(Key(confirmQuoteId.Trim())); // single use
            return (new EditVideoConsentTicket(pending.Quote, info ?? new EditVideoConsentInfo("unknown", null, null)), null);
        }

        var quote = new EditVideoPriceQuote(Guid.NewGuid().ToString("N"), kind, model, summary, points, points * vndPerPoint, balance, (int)Lifetime.TotalSeconds);
        _cache.Set(Key(quote.QuoteId), new Pending(license.Id, kind, fingerprint, quote), Lifetime);
        await LogAsync(license.Id, device.Id, "ai_price_quote", new
        {
            quote.QuoteId, kind, model, summary, points, vnd = quote.Vnd, balance,
            reason = string.IsNullOrWhiteSpace(confirmQuoteId) ? "new_request" : "quote_expired_or_changed"
        }, ct);
        return (null, quote);
    }

    /// <summary>The person's agreement, with what it was for (logged after the paid task was created).</summary>
    public Task LogConsentAsync(EditVideoLicenseRow license, EditVideoDeviceRow device, EditVideoConsentTicket ticket, string kind, Guid jobId, CancellationToken ct = default) =>
        string.IsNullOrEmpty(ticket.Quote.QuoteId)
            ? Task.CompletedTask
            : LogAsync(license.Id, device.Id, "ai_price_consent", new
            {
                ticket.Quote.QuoteId, kind, ticket.Quote.Model, ticket.Quote.Summary, ticket.Quote.Points, vnd = ticket.Quote.Vnd,
                jobId, method = ticket.Info.Method, windowsUser = ticket.Info.WindowsUser, appVersion = ticket.Info.AppVersion
            }, ct);

    public static string Message(EditVideoPriceQuote quote) =>
        $"Cần người dùng xác nhận giá trước khi tạo: {quote.Summary} — {quote.Points:0.##} điểm (≈ {quote.Vnd:#,0}đ), số dư {quote.Balance:0.##} điểm. " +
        "TodoX EditVideo sẽ hiện cửa sổ xác nhận; nếu bản trên máy chưa có cửa sổ này, hãy cập nhật TodoX EditVideo.";

    private static string Key(string quoteId) => "editvideo-quote:" + quoteId;

    private async Task LogAsync(Guid licenseId, Guid deviceId, string type, object detail, CancellationToken ct)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO editvideo.license_events (tenant_id, license_id, device_id, event_type, ip, detail)
                VALUES (@tenantId, @licenseId, @deviceId, @type, @ip, CAST(@detail AS jsonb));
                """,
                new
                {
                    tenantId = _tenant.TenantId, licenseId, deviceId, type,
                    ip = _http?.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    detail = JsonSerializer.Serialize(detail)
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "EDITVIDEO_CONSENT_LOG_FAILED type={Type} license={License}", type, licenseId);
        }
    }
}
