namespace TodoX.Web.Services.EditVideoTool;

public enum EditVideoActivationDecision
{
    /// <summary>This device already holds an active slot; just refresh its session.</summary>
    Reactivate,
    /// <summary>A free slot of the requested kind exists.</summary>
    ActivateNew,
    /// <summary>All slots of this kind are taken by other devices.</summary>
    LimitReached
}

/// <summary>
/// Pure slot rules, kept free of IO so they can be unit-tested. Slots are counted per device kind:
/// the default plan allows one desktop (Claude Code / Codex) and one mobile device.
/// </summary>
public static class EditVideoDevicePolicy
{
    public static EditVideoActivationDecision Decide(
        EditVideoLicenseRow license,
        IReadOnlyCollection<EditVideoDeviceRow> activeDevices,
        string deviceHash,
        string deviceKind)
    {
        if (activeDevices.Any(d => d.DeviceHash == deviceHash && d.DeviceKind == deviceKind))
        {
            return EditVideoActivationDecision.Reactivate;
        }

        var limit = deviceKind == EditVideoDeviceKinds.Mobile ? license.MaxMobileDevices : license.MaxDesktopDevices;
        var used = activeDevices.Count(d => d.DeviceKind == deviceKind);
        return used < limit ? EditVideoActivationDecision.ActivateNew : EditVideoActivationDecision.LimitReached;
    }

    /// <summary>Returns the error code when the license itself cannot be used, otherwise null.</summary>
    public static string? LicenseBlocker(EditVideoLicenseRow license, DateTimeOffset now)
    {
        if (license.Status == EditVideoLicenseStatuses.Revoked)
        {
            return EditVideoErrorCodes.LicenseRevoked;
        }

        if (license.Status != EditVideoLicenseStatuses.Active)
        {
            return EditVideoErrorCodes.LicenseSuspended;
        }

        return license.ExpiresAt is { } expires && expires <= now ? EditVideoErrorCodes.LicenseExpired : null;
    }
}
