using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TodoX.Web.Services.DanceSell;

public interface IRDanceDownloadTicketService
{
    string CreateTicket(Guid jobId, Guid? customerId, Guid userId, string type, TimeSpan ttl);
    RDanceDownloadTicket ValidateTicket(string token);
}

public sealed class RDanceDownloadTicketService : IRDanceDownloadTicketService
{
    private const string Purpose = "TodoX.RDance.DownloadTicket.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector _protector;

    public RDanceDownloadTicketService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    public string CreateTicket(Guid jobId, Guid? customerId, Guid userId, string type, TimeSpan ttl)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var ticket = new RDanceDownloadTicketPayload
        {
            JobId = jobId,
            CustomerId = customerId,
            UserId = userId,
            Type = type.Trim(),
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = issuedAt.Add(ttl)
        };

        return _protector.Protect(JsonSerializer.Serialize(ticket, JsonOptions), ttl);
    }

    public RDanceDownloadTicket ValidateTicket(string token)
    {
        string json;
        try
        {
            json = _protector.Unprotect(token);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("DANCE_SELL_DOWNLOAD_TICKET_INVALID", ex);
        }

        var payload = JsonSerializer.Deserialize<RDanceDownloadTicketPayload>(json, JsonOptions)
            ?? throw new InvalidOperationException("DANCE_SELL_DOWNLOAD_TICKET_INVALID");

        if (string.IsNullOrWhiteSpace(payload.Type)
            || payload.JobId == Guid.Empty
            || payload.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("DANCE_SELL_DOWNLOAD_TICKET_INVALID");
        }

        return new RDanceDownloadTicket(
            payload.JobId,
            payload.CustomerId,
            payload.UserId,
            payload.Type,
            payload.IssuedAtUtc,
            payload.ExpiresAtUtc);
    }

    private sealed class RDanceDownloadTicketPayload
    {
        public Guid JobId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid UserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public DateTimeOffset IssuedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}

public sealed record RDanceDownloadTicket(
    Guid JobId,
    Guid? CustomerId,
    Guid UserId,
    string Type,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);
