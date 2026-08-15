using System.Text.Json;

namespace TodoX.Web.Services.Platform;

/// <summary>
/// Stable channel identifiers used by every TodoX client. Business logic must not branch on
/// presentation-specific details; the channel is recorded for audit, idempotency and analytics only.
/// </summary>
public static class CoreChannelCodes
{
    public const string Dashboard = "dashboard";
    public const string Zalo = "zalo";
    public const string Telegram = "telegram";
    public const string Partner = "partner";
    public const string Api = "api";
    public const string System = "system";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Dashboard,
        Zalo,
        Telegram,
        Partner,
        Api,
        System
    };

    public static string Normalize(string? value)
    {
        var channel = string.IsNullOrWhiteSpace(value) ? System : value.Trim().ToLowerInvariant();
        if (!Allowed.Contains(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported TodoX channel '{value}'.");
        }

        return channel;
    }
}

/// <summary>
/// Identity and caller metadata resolved by the transport layer (Dashboard session, Zalo login,
/// Telegram binding, partner API key, etc.). Core services consume this context and never parse
/// transport-specific credentials themselves.
/// </summary>
public sealed record CoreRequestContext(
    Guid? CustomerId,
    Guid? UserId,
    string Channel,
    string? ClientId = null,
    string? ExternalRequestId = null)
{
    public string NormalizedChannel => CoreChannelCodes.Normalize(Channel);
}

/// <summary>
/// Transport-neutral request for creating any TodoX service job.
/// The service catalog owns the service definition; clients submit only the service code and input.
/// </summary>
public sealed class CoreCreateJobRequest
{
    public string ServiceCode { get; init; } = string.Empty;
    public JsonElement Input { get; init; }
    public JsonElement? Prompt { get; init; }
    public JsonElement? References { get; init; }
    public string? IdempotencyKey { get; init; }
    public int Priority { get; init; } = 100;
}

public sealed record CoreJobView(
    Guid JobId,
    Guid? ServiceId,
    string ServiceCode,
    string Status,
    string Channel,
    int ProgressPercent,
    decimal PointCostEstimate,
    decimal PointCostCharged,
    string PointStatus,
    JsonElement Output,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? CompletedAt);

/// <summary>
/// A stable catalog projection shared by Dashboard, Zalo Mini App, Telegram and partner clients.
/// FormSchema is generated from catalog data; consumers must not hard-code service-specific forms.
/// </summary>
public sealed record CoreServiceView(
    Guid Id,
    string ServiceCode,
    string Name,
    string ServiceType,
    string? Description,
    string? WorkflowCode,
    string? ThumbnailUrl,
    JsonElement FormSchema,
    bool Enabled,
    int SortOrder);

/// <summary>
/// Boundary between the TodoX core and service-specific execution runtimes. Timelapse/RVideo/RDance
/// adapters implement this interface later without leaking workflow details into Dashboard or API code.
/// </summary>
public interface ICoreJobExecutionAdapter
{
    string ServiceCode { get; }

    Task DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default);
}

public sealed record CoreJobDispatchContext(
    Guid CoreJobId,
    Guid ServiceId,
    string ServiceCode,
    CoreRequestContext RequestContext,
    JsonElement Input,
    JsonElement? Prompt,
    JsonElement? References);
