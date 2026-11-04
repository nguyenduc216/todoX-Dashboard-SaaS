using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// License + device-slot management for TodoX.EditVideoTool. Customers buy a key; each key binds a limited
/// number of devices per kind (default 1 desktop + 1 mobile). The local tool trades the key for a short-lived
/// access token and an ES256-signed license document it can verify offline.
/// </summary>
public sealed class EditVideoLicenseService
{
    private const string LicenseColumns = """
        l.id AS Id, l.tenant_id AS TenantId, l.customer_id AS CustomerId, c.company_name AS CustomerName,
        l.key_prefix AS KeyPrefix, l.plan_code AS PlanCode,
        l.max_desktop_devices AS MaxDesktopDevices, l.max_mobile_devices AS MaxMobileDevices,
        l.status AS Status, l.expires_at AS ExpiresAt, l.note AS Note, l.created_at AS CreatedAt
        """;

    private const string DeviceColumns = """
        d.id AS Id, d.license_id AS LicenseId, d.device_hash AS DeviceHash, d.device_kind AS DeviceKind,
        d.device_name AS DeviceName, d.os AS Os, d.app_version AS AppVersion, d.status AS Status,
        d.activated_at AS ActivatedAt, d.last_seen_at AS LastSeenAt, d.last_ip AS LastIp,
        d.access_token_expires_at AS AccessTokenExpiresAt, d.released_at AS ReleasedAt, d.released_reason AS ReleasedReason
        """;

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly EditVideoLicenseSigner _signer;
    private readonly EditVideoToolOptions _options;
    private readonly TimeProvider _clock;

    public EditVideoLicenseService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        EditVideoLicenseSigner signer,
        IOptions<EditVideoToolOptions> options,
        TimeProvider? clock = null)
    {
        _factory = factory;
        _tenant = tenant;
        _signer = signer;
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    // ---------------------------------------------------------------- device API (called by the exe)

    public async Task<EditVideoResult<EditVideoDeviceSession>> ActivateAsync(EditVideoActivateRequest request, string? ip, CancellationToken ct = default)
    {
        var key = EditVideoLicenseKeys.Normalize(request.LicenseKey);
        var kind = EditVideoDeviceKinds.Normalize(request.DeviceKind);
        var deviceHash = request.DeviceHash?.Trim().ToLowerInvariant();
        if (key is null || kind is null || !EditVideoLicenseKeys.IsValidDeviceHash(deviceHash))
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(EditVideoErrorCodes.InvalidRequest, "License key, device hash or device kind is invalid.");
        }

        if (!_signer.IsAvailable)
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(EditVideoErrorCodes.SigningUnavailable, "License service is not configured.");
        }

        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        // Row lock serialises concurrent activations of the same key so two machines cannot both take the last slot.
        var license = await conn.QuerySingleOrDefaultAsync<EditVideoLicenseRow>(
            $"""
            SELECT {LicenseColumns}
              FROM editvideo.licenses l
              LEFT JOIN crm.customers c ON c.id = l.customer_id
             WHERE l.license_key_hash = @hash AND l.tenant_id = @tenantId
               FOR UPDATE OF l;
            """,
            new { hash = EditVideoLicenseKeys.Hash(key), tenantId = _tenant.TenantId }, tx);

        if (license is null)
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(EditVideoErrorCodes.InvalidLicense, "License key not found.");
        }

        var blocker = EditVideoDevicePolicy.LicenseBlocker(license, now);
        if (blocker is not null)
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(blocker, "License cannot be used.");
        }

        var active = (await conn.QueryAsync<EditVideoDeviceRow>(
            $"SELECT {DeviceColumns} FROM editvideo.devices d WHERE d.license_id = @licenseId AND d.status = 'active' ORDER BY d.activated_at;",
            new { licenseId = license.Id }, tx)).ToList();

        var decision = EditVideoDevicePolicy.Decide(license, active, deviceHash!, kind);
        if (decision == EditVideoActivationDecision.LimitReached)
        {
            await InsertEventAsync(conn, tx, license.Id, null, EditVideoEventTypes.LimitExceeded, ip, new
            {
                deviceKind = kind,
                deviceName = Trim(request.DeviceName, 120),
                os = Trim(request.Os, 120),
                deviceHashPrefix = deviceHash![..12]
            });
            tx.Commit();

            var holders = active.Where(d => d.DeviceKind == kind)
                .Select(d => new { d.DeviceName, d.Os, d.LastSeenAt })
                .ToList();
            return EditVideoResult<EditVideoDeviceSession>.Fail(
                EditVideoErrorCodes.DeviceLimitReached,
                "All device slots of this kind are in use. Release a device in the TodoX dashboard first.",
                new { deviceKind = kind, limit = kind == EditVideoDeviceKinds.Mobile ? license.MaxMobileDevices : license.MaxDesktopDevices, devices = holders });
        }

        var (token, tokenHash, tokenExpires) = NewAccessToken(now);
        Guid deviceId;
        if (decision == EditVideoActivationDecision.Reactivate)
        {
            deviceId = active.First(d => d.DeviceHash == deviceHash && d.DeviceKind == kind).Id;
            await conn.ExecuteAsync(
                """
                UPDATE editvideo.devices
                   SET access_token_hash = @tokenHash, access_token_expires_at = @tokenExpires,
                       device_name = COALESCE(@name, device_name), os = COALESCE(@os, os),
                       app_version = COALESCE(@version, app_version), last_seen_at = @now, last_ip = @ip
                 WHERE id = @deviceId;
                """,
                new { deviceId, tokenHash, tokenExpires, name = Trim(request.DeviceName, 120), os = Trim(request.Os, 120), version = Trim(request.AppVersion, 40), now, ip }, tx);
            await InsertEventAsync(conn, tx, license.Id, deviceId, EditVideoEventTypes.Reactivated, ip, null);
        }
        else
        {
            deviceId = await conn.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO editvideo.devices
                    (tenant_id, license_id, device_hash, device_kind, device_name, os, app_version,
                     access_token_hash, access_token_expires_at, activated_at, last_seen_at, last_ip)
                VALUES
                    (@tenantId, @licenseId, @deviceHash, @kind, @name, @os, @version,
                     @tokenHash, @tokenExpires, @now, @now, @ip)
                RETURNING id;
                """,
                new
                {
                    tenantId = _tenant.TenantId, licenseId = license.Id, deviceHash, kind,
                    name = Trim(request.DeviceName, 120), os = Trim(request.Os, 120), version = Trim(request.AppVersion, 40),
                    tokenHash, tokenExpires, now, ip
                }, tx);
            await InsertEventAsync(conn, tx, license.Id, deviceId, EditVideoEventTypes.Activated, ip, new { deviceKind = kind });
        }

        tx.Commit();
        return EditVideoResult<EditVideoDeviceSession>.Success(BuildSession(license, deviceHash!, kind, token, tokenExpires, now));
    }

    public async Task<EditVideoResult<EditVideoDeviceSession>> HeartbeatAsync(string? accessToken, EditVideoHeartbeatRequest request, string? ip, CancellationToken ct = default)
    {
        if (!_signer.IsAvailable)
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(EditVideoErrorCodes.SigningUnavailable, "License service is not configured.");
        }

        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        var auth = await AuthenticateAsync(conn, accessToken, request.DeviceHash, ip, now);
        if (!auth.Ok)
        {
            return EditVideoResult<EditVideoDeviceSession>.Fail(auth.ErrorCode!, auth.Message!);
        }

        var (license, device) = auth.Value!;
        var (token, tokenHash, tokenExpires) = NewAccessToken(now);
        await conn.ExecuteAsync(
            """
            UPDATE editvideo.devices
               SET access_token_hash = @tokenHash, access_token_expires_at = @tokenExpires,
                   app_version = COALESCE(@version, app_version), last_seen_at = @now, last_ip = @ip
             WHERE id = @deviceId;
            """,
            new { deviceId = device.Id, tokenHash, tokenExpires, version = Trim(request.AppVersion, 40), now, ip });

        return EditVideoResult<EditVideoDeviceSession>.Success(BuildSession(license, device.DeviceHash, device.DeviceKind, token, tokenExpires, now));
    }

    public async Task<EditVideoResult<bool>> DeactivateAsync(string? accessToken, string? deviceHash, string? ip, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        var auth = await AuthenticateAsync(conn, accessToken, deviceHash, ip, now, allowInactiveLicense: true);
        if (!auth.Ok)
        {
            return EditVideoResult<bool>.Fail(auth.ErrorCode!, auth.Message!);
        }

        var (license, device) = auth.Value!;
        await ReleaseDeviceAsync(conn, null, device.Id, EditVideoDeviceStatuses.Released, "self", now);
        await InsertEventAsync(conn, null, license.Id, device.Id, EditVideoEventTypes.Released, ip, new { by = "device" });
        return EditVideoResult<bool>.Success(true);
    }

    public async Task<EditVideoResult<object>> GetMeAsync(string? accessToken, string? deviceHash, string? ip, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        var auth = await AuthenticateAsync(conn, accessToken, deviceHash, ip, now);
        if (!auth.Ok)
        {
            return EditVideoResult<object>.Fail(auth.ErrorCode!, auth.Message!);
        }

        var (license, device) = auth.Value!;
        var counts = await CountActiveAsync(conn, license.Id);
        return EditVideoResult<object>.Success(new
        {
            licenseId = license.Id,
            customerName = license.CustomerName,
            plan = license.PlanCode,
            expiresAt = license.ExpiresAt,
            device = new { device.Id, device.DeviceKind, device.DeviceName },
            slots = new
            {
                desktop = new { used = counts.Desktop, limit = license.MaxDesktopDevices },
                mobile = new { used = counts.Mobile, limit = license.MaxMobileDevices }
            }
        });
    }

    // ---------------------------------------------------------------- dashboard (admin) operations

    public async Task<EditVideoIssuedLicense> IssueAsync(EditVideoIssueLicenseRequest request, Guid? actorUserId, CancellationToken ct = default)
    {
        ValidateLimits(request.MaxDesktopDevices, request.MaxMobileDevices);
        var key = EditVideoLicenseKeys.Generate();
        var prefix = EditVideoLicenseKeys.DisplayPrefix(key);

        using var conn = await _factory.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO editvideo.licenses
                (tenant_id, customer_id, license_key_hash, key_prefix, plan_code,
                 max_desktop_devices, max_mobile_devices, expires_at, note, created_by)
            VALUES
                (@tenantId, @customerId, @hash, @prefix, @plan, @maxDesktop, @maxMobile, @expiresAt, @note, @actorUserId)
            RETURNING id;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                request.CustomerId,
                hash = EditVideoLicenseKeys.Hash(key),
                prefix,
                plan = string.IsNullOrWhiteSpace(request.PlanCode) ? "standard" : request.PlanCode.Trim(),
                maxDesktop = request.MaxDesktopDevices,
                maxMobile = request.MaxMobileDevices,
                request.ExpiresAt,
                note = Trim(request.Note, 500),
                actorUserId
            });
        await InsertEventAsync(conn, null, id, null, EditVideoEventTypes.Issued, null, new { request.PlanCode }, actorUserId);
        return new EditVideoIssuedLicense(id, key, prefix);
    }

    public async Task<IReadOnlyList<EditVideoLicenseRow>> ListForCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoLicenseRow>(
            $"""
            SELECT {LicenseColumns},
                   (SELECT count(*) FROM editvideo.devices d WHERE d.license_id = l.id AND d.status = 'active' AND d.device_kind = 'desktop')::int AS ActiveDesktopDevices,
                   (SELECT count(*) FROM editvideo.devices d WHERE d.license_id = l.id AND d.status = 'active' AND d.device_kind = 'mobile')::int AS ActiveMobileDevices,
                   (SELECT count(*) FROM editvideo.license_events e WHERE e.license_id = l.id AND e.event_type = 'limit_exceeded'
                        AND e.created_at > now() - interval '30 days')::int AS LimitExceededLast30Days
              FROM editvideo.licenses l
              LEFT JOIN crm.customers c ON c.id = l.customer_id
             WHERE l.tenant_id = @tenantId AND l.customer_id = @customerId
             ORDER BY l.created_at DESC;
            """,
            new { tenantId = _tenant.TenantId, customerId });
        return rows.ToList();
    }

    /// <summary>Per-customer counters for the customer list: license count and over-limit attempts in 30 days.</summary>
    public async Task<IReadOnlyDictionary<Guid, (int Licenses, int Alerts)>> GetCustomerSummaryAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<(Guid CustomerId, int Licenses, int Alerts)>(
            """
            SELECT l.customer_id,
                   count(DISTINCT l.id)::int,
                   count(e.id)::int
              FROM editvideo.licenses l
              LEFT JOIN editvideo.license_events e
                     ON e.license_id = l.id AND e.event_type = 'limit_exceeded' AND e.created_at > now() - interval '30 days'
             WHERE l.tenant_id = @tenantId AND l.status <> 'revoked'
             GROUP BY l.customer_id;
            """,
            new { tenantId = _tenant.TenantId });
        return rows.ToDictionary(r => r.CustomerId, r => (r.Licenses, r.Alerts));
    }

    public async Task<IReadOnlyList<EditVideoDeviceRow>> ListDevicesAsync(Guid licenseId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoDeviceRow>(
            $"""
            SELECT {DeviceColumns}
              FROM editvideo.devices d
             WHERE d.license_id = @licenseId AND d.tenant_id = @tenantId
             ORDER BY (d.status = 'active') DESC, d.last_seen_at DESC
             LIMIT 50;
            """,
            new { licenseId, tenantId = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<EditVideoEventRow>> ListEventsAsync(Guid licenseId, int limit = 30, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<EditVideoEventRow>(
            """
            SELECT id AS Id, license_id AS LicenseId, device_id AS DeviceId, event_type AS EventType,
                   ip AS Ip, detail::text AS Detail, created_at AS CreatedAt
              FROM editvideo.license_events
             WHERE license_id = @licenseId AND tenant_id = @tenantId
             ORDER BY created_at DESC
             LIMIT @limit;
            """,
            new { licenseId, tenantId = _tenant.TenantId, limit = Math.Clamp(limit, 1, 200) });
        return rows.ToList();
    }

    public async Task SetStatusAsync(Guid licenseId, string status, Guid? actorUserId, CancellationToken ct = default)
    {
        if (status is not (EditVideoLicenseStatuses.Active or EditVideoLicenseStatuses.Suspended or EditVideoLicenseStatuses.Revoked))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            """
            UPDATE editvideo.licenses
               SET status = @status, updated_at = @now,
                   revoked_at = CASE WHEN @status = 'revoked' THEN @now ELSE revoked_at END
             WHERE id = @licenseId AND tenant_id = @tenantId;
            """,
            new { licenseId, status, now, tenantId = _tenant.TenantId }, tx);

        if (status == EditVideoLicenseStatuses.Revoked)
        {
            // A revoked key frees nothing for reuse; kill every live session immediately.
            await conn.ExecuteAsync(
                """
                UPDATE editvideo.devices
                   SET status = 'revoked', access_token_hash = NULL, released_at = @now, released_reason = 'license_revoked'
                 WHERE license_id = @licenseId AND status = 'active';
                """,
                new { licenseId, now }, tx);
        }

        await InsertEventAsync(conn, tx, licenseId, null, EditVideoEventTypes.StatusChanged, null, new { status }, actorUserId);
        tx.Commit();
    }

    /// <summary>Changes slot limits. Refuses to go below the number of devices currently holding a slot.</summary>
    public async Task<(bool Ok, string Message)> SetLimitsAsync(Guid licenseId, int maxDesktop, int maxMobile, Guid? actorUserId, CancellationToken ct = default)
    {
        ValidateLimits(maxDesktop, maxMobile);
        using var conn = await _factory.OpenAsync(ct);
        var counts = await CountActiveAsync(conn, licenseId);
        if (counts.Desktop > maxDesktop || counts.Mobile > maxMobile)
        {
            return (false, $"Đang có {counts.Desktop} máy tính và {counts.Mobile} điện thoại hoạt động. Gỡ bớt thiết bị trước khi giảm giới hạn.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE editvideo.licenses
               SET max_desktop_devices = @maxDesktop, max_mobile_devices = @maxMobile, updated_at = now()
             WHERE id = @licenseId AND tenant_id = @tenantId;
            """,
            new { licenseId, maxDesktop, maxMobile, tenantId = _tenant.TenantId });
        await InsertEventAsync(conn, null, licenseId, null, EditVideoEventTypes.LimitsChanged, null, new { maxDesktop, maxMobile }, actorUserId);
        return (true, "Đã cập nhật giới hạn thiết bị.");
    }

    /// <summary>
    /// Frees a device slot from the dashboard (e.g. customer changed computer). When <paramref name="onlyForCustomerId"/>
    /// is set (customer self-service) the device must belong to that customer's license.
    /// </summary>
    public async Task RevokeDeviceAsync(Guid deviceId, Guid? actorUserId, CancellationToken ct = default, Guid? onlyForCustomerId = null)
    {
        var now = _clock.GetUtcNow();
        using var conn = await _factory.OpenAsync(ct);
        var licenseId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT d.license_id FROM editvideo.devices d
              JOIN editvideo.licenses l ON l.id = d.license_id
             WHERE d.id = @deviceId AND d.tenant_id = @tenantId
               AND (@customerId::uuid IS NULL OR l.customer_id = @customerId);
            """,
            new { deviceId, tenantId = _tenant.TenantId, customerId = onlyForCustomerId });
        if (licenseId is null)
        {
            return;
        }

        await ReleaseDeviceAsync(conn, null, deviceId, EditVideoDeviceStatuses.Revoked, "dashboard", now);
        await InsertEventAsync(conn, null, licenseId, deviceId, EditVideoEventTypes.RevokedDevice, null, new { by = "dashboard" }, actorUserId);
    }

    public string? PublicKeyPem => _signer.IsAvailable ? _signer.PublicKeyPem : null;

    /// <summary>Device authentication shared by every device-facing endpoint (plan, voice, logs ...).</summary>
    public async Task<EditVideoResult<(EditVideoLicenseRow License, EditVideoDeviceRow Device)>> AuthenticateDeviceAsync(
        string? accessToken, string? deviceHash, string? ip, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await AuthenticateAsync(conn, accessToken, deviceHash, ip, _clock.GetUtcNow());
    }

    // ---------------------------------------------------------------- helpers

    private async Task<EditVideoResult<(EditVideoLicenseRow License, EditVideoDeviceRow Device)>> AuthenticateAsync(
        IDbConnection conn, string? accessToken, string? deviceHash, string? ip, DateTimeOffset now, bool allowInactiveLicense = false)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Fail(EditVideoErrorCodes.InvalidToken, "Missing access token.");
        }

        var device = await conn.QuerySingleOrDefaultAsync<EditVideoDeviceRow>(
            $"SELECT {DeviceColumns} FROM editvideo.devices d WHERE d.access_token_hash = @hash AND d.status = 'active';",
            new { hash = HashToken(accessToken.Trim()) });

        // Expired tokens are still accepted for the offline grace window so a machine that slept for a few days
        // can heartbeat without re-entering the key. Beyond that the customer must activate again.
        if (device is null || device.AccessTokenExpiresAt is not { } expires || expires.AddDays(_options.OfflineGraceDays) <= now)
        {
            return Fail(EditVideoErrorCodes.InvalidToken, "Access token is invalid or expired.");
        }

        var normalizedHash = deviceHash?.Trim().ToLowerInvariant();
        if (!string.Equals(normalizedHash, device.DeviceHash, StringComparison.Ordinal))
        {
            // Token copied to another machine: log it and refuse, but keep the real device's slot.
            await InsertEventAsync(conn, null, device.LicenseId, device.Id, EditVideoEventTypes.DeviceMismatch, ip,
                new { presentedHashPrefix = normalizedHash is { Length: >= 12 } ? normalizedHash[..12] : normalizedHash });
            return Fail(EditVideoErrorCodes.DeviceMismatch, "Token does not belong to this device.");
        }

        var license = await conn.QuerySingleAsync<EditVideoLicenseRow>(
            $"SELECT {LicenseColumns} FROM editvideo.licenses l LEFT JOIN crm.customers c ON c.id = l.customer_id WHERE l.id = @id;",
            new { id = device.LicenseId });

        if (!allowInactiveLicense && EditVideoDevicePolicy.LicenseBlocker(license, now) is { } blocker)
        {
            return Fail(blocker, "License cannot be used.");
        }

        return EditVideoResult<(EditVideoLicenseRow, EditVideoDeviceRow)>.Success((license, device));

        static EditVideoResult<(EditVideoLicenseRow, EditVideoDeviceRow)> Fail(string code, string message) =>
            EditVideoResult<(EditVideoLicenseRow, EditVideoDeviceRow)>.Fail(code, message);
    }

    private EditVideoDeviceSession BuildSession(EditVideoLicenseRow license, string deviceHash, string kind, string token, DateTimeOffset tokenExpires, DateTimeOffset now)
    {
        var heartbeatDue = now.AddDays(_options.OfflineGraceDays);
        var document = _signer.Sign(new EditVideoLicensePayload(
            license.Id.ToString(),
            license.CustomerId.ToString(),
            deviceHash,
            kind,
            license.PlanCode,
            now.ToUnixTimeSeconds(),
            license.ExpiresAt?.ToUnixTimeSeconds(),
            heartbeatDue.ToUnixTimeSeconds()));
        return new EditVideoDeviceSession(document, token, tokenExpires, heartbeatDue);
    }

    private (string Token, string Hash, DateTimeOffset Expires) NewAccessToken(DateTimeOffset now)
    {
        var token = "tdxa_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (token, HashToken(token), now.AddHours(Math.Max(1, _options.AccessTokenHours)));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<(int Desktop, int Mobile)> CountActiveAsync(IDbConnection conn, Guid licenseId)
    {
        var row = await conn.QuerySingleAsync<(int Desktop, int Mobile)>(
            """
            SELECT count(*) FILTER (WHERE device_kind = 'desktop')::int,
                   count(*) FILTER (WHERE device_kind = 'mobile')::int
              FROM editvideo.devices
             WHERE license_id = @licenseId AND status = 'active';
            """,
            new { licenseId });
        return row;
    }

    private static Task ReleaseDeviceAsync(IDbConnection conn, IDbTransaction? tx, Guid deviceId, string status, string reason, DateTimeOffset now) =>
        conn.ExecuteAsync(
            """
            UPDATE editvideo.devices
               SET status = @status, access_token_hash = NULL, released_at = @now, released_reason = @reason
             WHERE id = @deviceId AND status = 'active';
            """,
            new { deviceId, status, reason, now }, tx);

    private Task InsertEventAsync(IDbConnection conn, IDbTransaction? tx, Guid? licenseId, Guid? deviceId, string type, string? ip, object? detail, Guid? actorUserId = null) =>
        conn.ExecuteAsync(
            """
            INSERT INTO editvideo.license_events (tenant_id, license_id, device_id, event_type, ip, detail, actor_user_id)
            VALUES (@tenantId, @licenseId, @deviceId, @type, @ip, CAST(@detail AS jsonb), @actorUserId);
            """,
            new
            {
                tenantId = _tenant.TenantId,
                licenseId,
                deviceId,
                type,
                ip,
                detail = detail is null ? null : JsonSerializer.Serialize(detail),
                actorUserId
            }, tx);

    private static void ValidateLimits(int maxDesktop, int maxMobile)
    {
        if (maxDesktop is < 0 or > 50 || maxMobile is < 0 or > 50 || maxDesktop + maxMobile == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDesktop), "Device limits must be 0-50 and allow at least one device.");
        }
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
