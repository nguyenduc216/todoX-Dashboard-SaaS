namespace TodoX.Web.Services.EditVideoTool;

public static class EditVideoDeviceKinds
{
    public const string Desktop = "desktop";
    public const string Mobile = "mobile";

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Desktop => Desktop,
        Mobile => Mobile,
        _ => null
    };
}

public static class EditVideoLicenseStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";
}

public static class EditVideoDeviceStatuses
{
    public const string Active = "active";
    public const string Released = "released";
    public const string Revoked = "revoked";
}

public static class EditVideoEventTypes
{
    public const string Issued = "issued";
    public const string Activated = "activated";
    public const string Reactivated = "reactivated";
    public const string Released = "released";
    public const string RevokedDevice = "revoked_device";
    public const string LimitExceeded = "limit_exceeded";
    public const string DeviceMismatch = "device_mismatch";
    public const string StatusChanged = "status_changed";
    public const string LimitsChanged = "limits_changed";
    public const string SelfReleaseRefused = "self_release_refused";
}

/// <summary>Stable error codes returned to the local tool; the exe maps them to Vietnamese messages.</summary>
public static class EditVideoErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidLicense = "invalid_license";
    public const string LicenseSuspended = "license_suspended";
    public const string LicenseRevoked = "license_revoked";
    public const string LicenseExpired = "license_expired";
    public const string DeviceLimitReached = "device_limit_reached";
    public const string InvalidToken = "invalid_token";
    public const string DeviceMismatch = "device_mismatch";
    public const string SigningUnavailable = "signing_unavailable";
    /// <summary>Moving a license to another machine is done by TodoX staff in the dashboard, never by the device.</summary>
    public const string SelfReleaseDisabled = "self_release_disabled";
}

public sealed class EditVideoLicenseRow
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string KeyPrefix { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public int MaxDesktopDevices { get; init; }
    public int MaxMobileDevices { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int ActiveDesktopDevices { get; init; }
    public int ActiveMobileDevices { get; init; }
    public int LimitExceededLast30Days { get; init; }
}

public sealed class EditVideoDeviceRow
{
    public Guid Id { get; init; }
    public Guid LicenseId { get; init; }
    public string DeviceHash { get; init; } = string.Empty;
    public string DeviceKind { get; init; } = string.Empty;
    public string? DeviceName { get; init; }
    public string? Os { get; init; }
    public string? AppVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset ActivatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public string? LastIp { get; init; }
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
    public string? ReleasedReason { get; init; }
}

public sealed class EditVideoEventRow
{
    public Guid Id { get; init; }
    public Guid? LicenseId { get; init; }
    public Guid? DeviceId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? Ip { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record EditVideoIssueLicenseRequest(
    Guid CustomerId,
    string PlanCode,
    int MaxDesktopDevices,
    int MaxMobileDevices,
    DateTimeOffset? ExpiresAt,
    string? Note);

public sealed record EditVideoIssuedLicense(Guid LicenseId, string LicenseKey, string KeyPrefix);

/// <summary>
/// Thrown when a customer already holds a license that is not revoked: one account gets one key. To replace a
/// key, revoke the old one first.
/// </summary>
public sealed class EditVideoLicenseExistsException(string keyPrefix)
    : InvalidOperationException($"Customer already has license {keyPrefix}; revoke it before issuing a new one.")
{
    public string KeyPrefix { get; } = keyPrefix;
}

public sealed class EditVideoActivateRequest
{
    public string? LicenseKey { get; init; }
    public string? DeviceHash { get; init; }
    public string? DeviceKind { get; init; }
    public string? DeviceName { get; init; }
    public string? Os { get; init; }
    public string? AppVersion { get; init; }
}

public sealed class EditVideoHeartbeatRequest
{
    public string? DeviceHash { get; init; }
    public string? AppVersion { get; init; }
}

public sealed record EditVideoDeviceSession(
    string License,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset HeartbeatDueAt);

public sealed record EditVideoResult<T>(T? Value, string? ErrorCode, string? Message, object? Detail = null)
{
    public bool Ok => ErrorCode is null;

    public static EditVideoResult<T> Success(T value) => new(value, null, null);
    public static EditVideoResult<T> Fail(string code, string message, object? detail = null) => new(default, code, message, detail);
}
