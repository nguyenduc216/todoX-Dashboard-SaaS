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
    string? ExternalRequestId = null,
    bool IsTrustedInternal = false)
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
    Guid? CustomerId,
    Guid? UserId,
    string Status,
    string Channel,
    string? OperationType,
    string? LogicalRequestId,
    string? CurrentStep,
    int ProgressPercent,
    decimal PointCostEstimate,
    decimal PointCostCharged,
    string PointStatus,
    Guid? RetryOfJobId,
    JsonElement Input,
    JsonElement Output,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? CompletedAt,
    CoreExecutionCorrelation? Execution = null);

public sealed record CoreJobListRequest(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? ServiceCode = null);

public sealed record CoreJobListResult(
    IReadOnlyList<CoreJobView> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record CoreRetryJobRequest(string? IdempotencyKey = null);

public sealed record CoreCancelJobRequest(string? Reason = null);

public sealed record CoreServicePriceView(
    string AssetType,
    string QualityTier,
    int? DurationSeconds,
    decimal SellPoints,
    string? DisplayLabel);

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
    IReadOnlyList<CoreServicePriceView> Prices,
    bool Enabled,
    int SortOrder);

public static class CoreJobAccess
{
    public static void EnsureAuthenticated(CoreRequestContext context)
    {
        var channel = context.NormalizedChannel;
        if (context.IsTrustedInternal && channel == CoreChannelCodes.System)
        {
            return;
        }

        if (context.CustomerId is null)
        {
            throw new UnauthorizedAccessException("A resolved customer identity is required.");
        }
    }

    public static bool CanAccess(CoreRequestContext context, Guid? jobCustomerId)
    {
        var channel = context.NormalizedChannel;
        if (context.IsTrustedInternal && channel == CoreChannelCodes.System)
        {
            return true;
        }

        return context.CustomerId is Guid customerId
            && jobCustomerId is Guid ownerCustomerId
            && customerId == ownerCustomerId;
    }
}

/// <summary>
/// Boundary between the TodoX core and service-specific execution runtimes. Timelapse/RVideo/RDance
/// adapters implement this interface later without leaking workflow details into Dashboard or API code.
/// </summary>
public interface ICoreJobExecutionAdapter
{
    string ServiceCode { get; }

    Task<CoreExecutionResult> DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default);
}

public enum CoreExecutionDisposition
{
    Completed,
    Deferred
}

public sealed record CoreExecutionResult(
    CoreExecutionDisposition Disposition,
    JsonElement? Output = null,
    string? ExecutionSystem = null,
    string? ExternalExecutionId = null,
    string? Adapter = null,
    string? Message = null,
    JsonElement? Metadata = null)
{
    public static CoreExecutionResult Completed(
        JsonElement? output = null,
        string? message = null)
        => new(CoreExecutionDisposition.Completed, Output: output, Message: message);

    public static CoreExecutionResult Deferred(
        string executionSystem,
        string externalExecutionId,
        string? adapter = null,
        string? message = null,
        JsonElement? metadata = null)
        => new(
            CoreExecutionDisposition.Deferred,
            ExecutionSystem: executionSystem,
            ExternalExecutionId: externalExecutionId,
            Adapter: adapter,
            Message: message,
            Metadata: metadata);
}

public enum CoreFailureBillingPolicy
{
    ReleaseReservation,
    KeepCharge,
    RefundCharge
}

public sealed record CoreJobDispatchContext(
    Guid CoreJobId,
    Guid ServiceId,
    string ServiceCode,
    CoreRequestContext RequestContext,
    JsonElement Input,
    JsonElement? Prompt,
    JsonElement? References);
