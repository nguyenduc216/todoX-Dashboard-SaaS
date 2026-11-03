using System.Net;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Tests;

public sealed class LicenseGuardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private const string Server = "https://todox.test";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly TestSigner _signer = new();
    private readonly FakeDevice _device = new();
    private readonly FakeHandler _http = new();
    private readonly FakeClock _clock = new(Now);
    private readonly LicenseStore _store;
    private readonly LicenseGuard _guard;

    public LicenseGuardTests()
    {
        _store = new LicenseStore(new AgentPaths(_root));
        _guard = new LicenseGuard(_store, _signer.Verifier, _device, new LicenseApiClient(new HttpClient(_http)), _clock);
    }

    public void Dispose()
    {
        _signer.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LicensePayload Payload(string? deviceHash = null, DateTimeOffset? exp = null, DateTimeOffset? hbd = null) => new(
        Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), deviceHash ?? _device.DeviceHash, "desktop", "standard",
        Now.ToUnixTimeSeconds(), exp?.ToUnixTimeSeconds(), (hbd ?? Now.AddDays(7)).ToUnixTimeSeconds());

    private StoredLicense Stored(LicensePayload payload, DateTimeOffset? tokenExpires = null) =>
        new(_signer.Sign(payload), "tdxa_old", tokenExpires ?? Now.AddHours(20), DateTimeOffset.FromUnixTimeSeconds(payload.Hbd), Server);

    private string SessionJson(LicensePayload payload, string token = "tdxa_new") => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            license = _signer.Sign(payload),
            accessToken = token,
            accessTokenExpiresAt = Now.AddHours(24),
            heartbeatDueAt = DateTimeOffset.FromUnixTimeSeconds(payload.Hbd)
        }
    });

    [Fact]
    public void Offline_evaluation_covers_every_state()
    {
        var v = _signer.Verifier;
        var hash = _device.DeviceHash;

        Assert.Equal(LicenseState.NotActivated, LicenseGuard.Evaluate(null, v, hash, Now).State);
        Assert.Equal(LicenseState.Valid, LicenseGuard.Evaluate(Stored(Payload()), v, hash, Now).State);
        Assert.Equal(LicenseState.Invalid, LicenseGuard.Evaluate(Stored(Payload(deviceHash: new string('b', 64))), v, hash, Now).State);
        Assert.Equal(LicenseState.Expired, LicenseGuard.Evaluate(Stored(Payload(exp: Now.AddSeconds(-1))), v, hash, Now).State);
        Assert.Equal(LicenseState.HeartbeatRequired, LicenseGuard.Evaluate(Stored(Payload(hbd: Now.AddSeconds(-1))), v, hash, Now).State);

        var tampered = Stored(Payload()) with { License = "eyJ4IjoxfQ.AAAA" };
        Assert.Equal(LicenseState.Invalid, LicenseGuard.Evaluate(tampered, v, hash, Now).State);
    }

    [Fact]
    public void License_signed_by_another_key_is_rejected()
    {
        using var other = new TestSigner();
        var forged = new StoredLicense(other.Sign(Payload()), "t", Now.AddHours(1), Now.AddDays(7), Server);

        Assert.Equal(LicenseState.Invalid, LicenseGuard.Evaluate(forged, _signer.Verifier, _device.DeviceHash, Now).State);
    }

    [Fact]
    public void Store_round_trips_and_file_is_not_plain_text()
    {
        var stored = Stored(Payload());
        _store.Save(stored);

        Assert.Equal(stored, _store.Load());
        Assert.DoesNotContain("tdxa_old", File.ReadAllText(Path.Combine(_root, "license.json")));
    }

    [Fact]
    public async Task Activate_saves_license_and_sends_device_info()
    {
        _http.Replies.Enqueue((HttpStatusCode.OK, SessionJson(Payload())));

        var status = await _guard.ActivateAsync("TDXV-AAAAA-BBBBB-CCCCC-DDDDD", serverUrl: Server);

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal("tdxa_new", _store.Load()!.AccessToken);
        var (path, _, body) = Assert.Single(_http.Requests);
        Assert.Equal("/api/editvideo/v1/activate", path);
        Assert.Contains(_device.DeviceHash, body);
        Assert.Contains("TEST-PC", body);
    }

    [Fact]
    public async Task Activate_over_limit_explains_which_devices_hold_the_slot()
    {
        _http.Replies.Enqueue((HttpStatusCode.Conflict, """
            {"success":false,"code":"device_limit_reached","message":"x",
             "detail":{"deviceKind":"desktop","limit":1,"devices":[{"deviceName":"PC-VAN-PHONG"}]}}
            """));

        var status = await _guard.ActivateAsync("TDXV-AAAAA-BBBBB-CCCCC-DDDDD", serverUrl: Server);

        Assert.Equal(LicenseState.NotActivated, status.State);
        Assert.Contains("PC-VAN-PHONG", status.Message);
        Assert.Contains("1 máy tính", status.Message);
        Assert.False(_store.Exists);
    }

    [Fact]
    public async Task Activate_rejects_license_not_signed_by_todox()
    {
        using var other = new TestSigner();
        _http.Replies.Enqueue((HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            success = true,
            data = new { license = other.Sign(Payload()), accessToken = "t", accessTokenExpiresAt = Now, heartbeatDueAt = Now }
        })));

        var status = await _guard.ActivateAsync("TDXV-AAAAA-BBBBB-CCCCC-DDDDD", serverUrl: Server);

        Assert.Equal(LicenseState.Invalid, status.State);
        Assert.False(_store.Exists);
    }

    [Fact]
    public async Task Fresh_session_does_not_call_server()
    {
        _store.Save(Stored(Payload()));

        Assert.Equal(LicenseState.Valid, (await _guard.EnsureAsync()).State);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Near_expiry_session_heartbeats_and_rotates_token()
    {
        _store.Save(Stored(Payload(), tokenExpires: Now.AddMinutes(30)));
        _http.Replies.Enqueue((HttpStatusCode.OK, SessionJson(Payload(), token: "tdxa_rotated")));

        var status = await _guard.EnsureAsync();

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal("tdxa_rotated", _store.Load()!.AccessToken);
        Assert.Equal("tdxa_old", _http.Requests.Single().Auth);
    }

    [Fact]
    public async Task Offline_keeps_working_inside_grace_but_server_revocation_stops_tools()
    {
        _store.Save(Stored(Payload(), tokenExpires: Now.AddMinutes(30)));

        _http.Replies.Enqueue((HttpStatusCode.OK, null));
        Assert.Equal(LicenseState.Valid, (await _guard.EnsureAsync()).State);

        _http.Replies.Enqueue((HttpStatusCode.Forbidden, """{"success":false,"code":"license_revoked","message":"x"}"""));
        var revoked = await _guard.EnsureAsync();
        Assert.Equal(LicenseState.Invalid, revoked.State);
        Assert.Contains("thu hồi", revoked.Message);
    }

    [Fact]
    public async Task Past_heartbeat_deadline_recovers_after_successful_heartbeat()
    {
        _store.Save(Stored(Payload(hbd: Now.AddDays(-1))));
        _http.Replies.Enqueue((HttpStatusCode.OK, SessionJson(Payload())));

        Assert.Equal(LicenseState.Valid, (await _guard.EnsureAsync()).State);
    }

    [Fact]
    public async Task Deactivate_removes_local_license_only_when_server_reachable()
    {
        _store.Save(Stored(Payload()));

        _http.Replies.Enqueue((HttpStatusCode.OK, null));
        Assert.False((await _guard.DeactivateAsync()).Ok);
        Assert.True(_store.Exists);

        _http.Replies.Enqueue((HttpStatusCode.OK, """{"success":true,"data":true}"""));
        Assert.True((await _guard.DeactivateAsync()).Ok);
        Assert.False(_store.Exists);
    }

    [Fact]
    public void Device_hash_is_stable_salted_and_case_insensitive()
    {
        var a = WindowsDeviceIdentity.ComputeHash("ABC-123", "1a2b3c4d");

        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.Equal(a, WindowsDeviceIdentity.ComputeHash(" abc-123 ", "1A2B3C4D"));
        Assert.NotEqual(a, WindowsDeviceIdentity.ComputeHash("ABC-123", "ffffffff"));
    }
}
