namespace TodoX.EditVideoTool.Licensing;

public enum LicenseState
{
    NotActivated,
    Valid,
    /// <summary>Offline too long; must reach the server once before tools work again.</summary>
    HeartbeatRequired,
    Expired,
    /// <summary>Signature broken or license belongs to another machine.</summary>
    Invalid
}

public sealed record LicenseStatus(LicenseState State, string Message, LicensePayload? Payload = null, StoredLicense? Stored = null)
{
    public bool IsUsable => State == LicenseState.Valid;

    public string Code => State switch
    {
        LicenseState.NotActivated => "not_activated",
        LicenseState.Valid => "valid",
        LicenseState.HeartbeatRequired => "heartbeat_required",
        LicenseState.Expired => "expired",
        _ => "invalid"
    };
}

/// <summary>
/// Decides whether the tools may run. Offline check first (signature, device, expiry, heartbeat deadline),
/// then a best-effort heartbeat to refresh the session and pick up suspensions/revocations.
/// </summary>
public sealed class LicenseGuard
{
    private static readonly TimeSpan RefreshWhenTokenExpiresWithin = TimeSpan.FromHours(2);

    private readonly LicenseStore _store;
    private readonly LicenseVerifier _verifier;
    private readonly IDeviceIdentity _device;
    private readonly LicenseApiClient _api;
    private readonly TimeProvider _clock;

    public LicenseGuard(LicenseStore store, LicenseVerifier verifier, IDeviceIdentity device, LicenseApiClient api, TimeProvider? clock = null)
    {
        _store = store;
        _verifier = verifier;
        _device = device;
        _api = api;
        _clock = clock ?? TimeProvider.System;
    }

    public static string AppVersion => Status.StatusService.Version;

    public LicenseStatus Evaluate() => Evaluate(_store.Load(), _verifier, _device.DeviceHash, _clock.GetUtcNow());

    internal static LicenseStatus Evaluate(StoredLicense? stored, LicenseVerifier verifier, string deviceHash, DateTimeOffset now)
    {
        if (stored is null)
        {
            return new LicenseStatus(LicenseState.NotActivated, "Chưa kích hoạt. Chạy: todox-editvideo activate <LICENSE-KEY>");
        }

        var payload = verifier.Verify(stored.License);
        if (payload is null)
        {
            return new LicenseStatus(LicenseState.Invalid, "License không hợp lệ (sai chữ ký). Hãy kích hoạt lại.", null, stored);
        }

        if (!string.Equals(payload.Dh, deviceHash, StringComparison.Ordinal))
        {
            return new LicenseStatus(LicenseState.Invalid, "License này thuộc máy khác. Hãy kích hoạt trên máy này.", payload, stored);
        }

        if (payload.Exp is { } exp && DateTimeOffset.FromUnixTimeSeconds(exp) <= now)
        {
            return new LicenseStatus(LicenseState.Expired, "License đã hết hạn. Liên hệ TodoX để gia hạn.", payload, stored);
        }

        if (DateTimeOffset.FromUnixTimeSeconds(payload.Hbd) <= now)
        {
            return new LicenseStatus(LicenseState.HeartbeatRequired, "Máy đã offline quá lâu. Kết nối Internet để TodoX xác nhận lại license.", payload, stored);
        }

        return new LicenseStatus(LicenseState.Valid, "License hợp lệ.", payload, stored);
    }

    /// <summary>Evaluate, and when the session is near expiry or the offline window is used up, try a heartbeat.</summary>
    public async Task<LicenseStatus> EnsureAsync(CancellationToken ct = default)
    {
        var status = Evaluate();
        var stored = status.Stored;
        var now = _clock.GetUtcNow();
        var wantsRefresh = status.State == LicenseState.HeartbeatRequired
            || (status.State == LicenseState.Valid && stored!.AccessTokenExpiresAt - now <= RefreshWhenTokenExpiresWithin);
        if (!wantsRefresh)
        {
            return status;
        }

        var result = await _api.HeartbeatAsync(stored!.ServerUrl, stored.AccessToken, _device, AppVersion, ct);
        if (result.Ok)
        {
            _store.Save(new StoredLicense(result.Value!.License, result.Value.AccessToken, result.Value.AccessTokenExpiresAt, result.Value.HeartbeatDueAt, stored.ServerUrl));
            return Evaluate();
        }

        // Network trouble keeps the offline grace; a definite "no" from the server stops the tools.
        return result.ErrorCode == LicenseApiClient.NetworkError
            ? status
            : new LicenseStatus(LicenseState.Invalid, LicenseMessages.ForServerError(result.ErrorCode, result.Message), status.Payload, stored);
    }

    public async Task<LicenseStatus> ActivateAsync(string licenseKey, string deviceKind = "desktop", string? serverUrl = null, CancellationToken ct = default)
    {
        var server = serverUrl ?? LicenseApiClient.DefaultServerUrl;
        var result = await _api.ActivateAsync(server, licenseKey, _device, deviceKind, AppVersion, ct);
        if (!result.Ok)
        {
            return new LicenseStatus(LicenseState.NotActivated, LicenseMessages.ForServerError(result.ErrorCode, result.Message, result.Detail));
        }

        var session = result.Value!;
        if (_verifier.Verify(session.License) is null)
        {
            return new LicenseStatus(LicenseState.Invalid, "Máy chủ trả về license không xác thực được. Hãy cập nhật TodoX EditVideoTool.");
        }

        _store.Save(new StoredLicense(session.License, session.AccessToken, session.AccessTokenExpiresAt, session.HeartbeatDueAt, server));
        return Evaluate();
    }

    /// <summary>
    /// Removes the license file from this Windows account only (uninstall). The machine keeps its slot on the
    /// server, so reinstalling on the same machine reactivates with the same key; moving to another machine is
    /// done by TodoX staff in the dashboard.
    /// </summary>
    public void ForgetLocal() => _store.Delete();
}
