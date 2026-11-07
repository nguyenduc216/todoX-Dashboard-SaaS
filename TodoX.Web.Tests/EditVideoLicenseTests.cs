using System.Security.Cryptography;
using TodoX.Web.Services.EditVideoTool;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class EditVideoLicenseTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);
    private static readonly string HashC = new('c', 64);

    private static EditVideoLicenseRow License(int desktop = 1, int mobile = 1, string status = "active", DateTimeOffset? expires = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        PlanCode = "standard",
        MaxDesktopDevices = desktop,
        MaxMobileDevices = mobile,
        Status = status,
        ExpiresAt = expires
    };

    private static EditVideoDeviceRow Device(string hash, string kind) => new()
    {
        Id = Guid.NewGuid(),
        DeviceHash = hash,
        DeviceKind = kind,
        Status = EditVideoDeviceStatuses.Active
    };

    [Fact]
    public void Generated_key_is_a_long_token_that_normalizes_to_itself()
    {
        var key = EditVideoLicenseKeys.Generate();

        Assert.Matches("^tdxv_[0-9a-z]{48}$", key);
        Assert.Equal(key, EditVideoLicenseKeys.Normalize(key));
        Assert.Equal(key, EditVideoLicenseKeys.Normalize("  " + key.ToUpperInvariant() + " "));
        Assert.Equal(EditVideoLicenseKeys.Hash(key), EditVideoLicenseKeys.Hash(EditVideoLicenseKeys.Normalize(key)!));
        Assert.Equal(key[..11], EditVideoLicenseKeys.DisplayPrefix(key));
        Assert.NotEqual(key, EditVideoLicenseKeys.Generate());
        Assert.Null(EditVideoLicenseKeys.Normalize("tdxv_short"));
        Assert.Null(EditVideoLicenseKeys.Normalize("tdxv_" + new string('u', 48)));
    }

    [Fact]
    public void Legacy_short_keys_still_work()
    {
        Assert.Equal("TDXV-GP1DJ-M5YV6-BG9E6-GTSQG", EditVideoLicenseKeys.Normalize("tdxv gp1dj m5yv6 bg9e6 gtsqg"));
        Assert.Equal("TDXV-GP1DJ", EditVideoLicenseKeys.DisplayPrefix("TDXV-GP1DJ-M5YV6-BG9E6-GTSQG"));
    }

    [Theory]
    [InlineData(402, null, null, true)]
    [InlineData(400, "INSUFFICIENT_BALANCE", null, true)]
    [InlineData(400, null, "Tài khoản không đủ số dư", true)]
    [InlineData(400, "QUOTA_EXCEEDED", null, true)]
    [InlineData(400, "INVALID_VOICE", "voice not found", false)]
    [InlineData(500, null, "internal error", false)]
    public void Vbee_out_of_credit_is_recognised(int http, string? code, string? message, bool expected) =>
        Assert.Equal(expected, EditVideoVoiceService.LooksLikeProviderOutOfCredit((System.Net.HttpStatusCode)http, code, message));

    [Fact]
    public void Plan_service_signs_plans_and_rejects_bad_input()
    {
        using var signer = new EditVideoLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var service = new EditVideoPlanService(signer);

        var ok = service.Create(new TodoX.EditVideo.Planning.PlanRequest([new("c0", 12, 1920, 1080, true, true)], Title: "Hi"));
        Assert.True(ok.Ok);
        Assert.NotNull(signer.Verify(ok.Value!.Document)); // signature checks out
        var parts = ok.Value.Document.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.Contains("\"c0\"", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[0].Replace('-', '+').Replace('_', '/').PadRight(parts[0].Length + (4 - parts[0].Length % 4) % 4, '='))));

        Assert.False(service.Create(null).Ok);
        Assert.False(service.Create(new TodoX.EditVideo.Planning.PlanRequest([])).Ok);
        Assert.False(service.Create(new TodoX.EditVideo.Planning.PlanRequest([new("x", 0.2, 10, 10, true, false)])).Ok);
    }

    [Fact]
    public void Plan_edits_apply_only_to_plans_this_server_signed()
    {
        using var signer = new EditVideoLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var service = new EditVideoPlanService(signer);
        TodoX.EditVideo.Planning.ClipInput[] clips = [new("c0", 12, 1920, 1080, true, true), new("c1", 8, 1920, 1080, true, true)];
        var created = service.Create(new TodoX.EditVideo.Planning.PlanRequest(clips));

        var edited = service.Edit(new TodoX.EditVideo.Planning.PlanEditRequest(created.Value!.Document, clips,
            new TodoX.EditVideo.Planning.PlanEdit("arrange", Scenes: [new("c1", 0, 2)])));

        Assert.True(edited.Ok);
        Assert.Equal("c1", Assert.Single(edited.Value!.Plan.Clips).Src);
        Assert.NotNull(signer.VerifyBody(edited.Value.Document));

        using var other = new EditVideoLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var foreign = new EditVideoPlanService(other).Create(new TodoX.EditVideo.Planning.PlanRequest(clips)).Value!.Document;
        Assert.False(service.Edit(new TodoX.EditVideo.Planning.PlanEditRequest(foreign, clips, new TodoX.EditVideo.Planning.PlanEdit("look", StyleId: "clean"))).Ok);
        Assert.Equal("invalid_request", service.Edit(new TodoX.EditVideo.Planning.PlanEditRequest(created.Value.Document, clips,
            new TodoX.EditVideo.Planning.PlanEdit("cut", Scene: 99))).ErrorCode);
    }

    [Fact]
    public void Hand_typed_key_with_lowercase_spaces_and_lookalikes_still_matches()
    {
        Assert.Equal("TDXV-01234-56789-ABCDE-FGH1K", EditVideoLicenseKeys.Normalize(" tdxv 0l234-56789 abcde fghik "));
        Assert.Equal("TDXV-00000-11111-22222-33333", EditVideoLicenseKeys.Normalize("TDXVOOOOO11111222223333 3"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TDXV-12345")]
    [InlineData("ABCD-12345-12345-12345-12345")]
    [InlineData("TDXV-1234U-12345-12345-12345")]
    public void Malformed_keys_are_rejected(string? raw) => Assert.Null(EditVideoLicenseKeys.Normalize(raw));

    [Fact]
    public void Device_hash_must_be_lowercase_sha256_hex()
    {
        Assert.True(EditVideoLicenseKeys.IsValidDeviceHash(HashA));
        Assert.False(EditVideoLicenseKeys.IsValidDeviceHash(HashA.ToUpperInvariant()));
        Assert.False(EditVideoLicenseKeys.IsValidDeviceHash("abc"));
    }

    [Fact]
    public void Default_plan_allows_one_desktop_and_one_mobile()
    {
        var license = License();

        Assert.Equal(EditVideoActivationDecision.ActivateNew,
            EditVideoDevicePolicy.Decide(license, [], HashA, EditVideoDeviceKinds.Desktop));

        var oneDesktop = new[] { Device(HashA, EditVideoDeviceKinds.Desktop) };
        Assert.Equal(EditVideoActivationDecision.LimitReached,
            EditVideoDevicePolicy.Decide(license, oneDesktop, HashB, EditVideoDeviceKinds.Desktop));
        Assert.Equal(EditVideoActivationDecision.ActivateNew,
            EditVideoDevicePolicy.Decide(license, oneDesktop, HashB, EditVideoDeviceKinds.Mobile));

        var full = new[] { Device(HashA, EditVideoDeviceKinds.Desktop), Device(HashB, EditVideoDeviceKinds.Mobile) };
        Assert.Equal(EditVideoActivationDecision.LimitReached,
            EditVideoDevicePolicy.Decide(license, full, HashC, EditVideoDeviceKinds.Mobile));
    }

    [Fact]
    public void Same_device_reactivates_even_when_slots_are_full()
    {
        var full = new[] { Device(HashA, EditVideoDeviceKinds.Desktop), Device(HashB, EditVideoDeviceKinds.Mobile) };

        Assert.Equal(EditVideoActivationDecision.Reactivate,
            EditVideoDevicePolicy.Decide(License(), full, HashA, EditVideoDeviceKinds.Desktop));
    }

    [Fact]
    public void Zero_mobile_slots_blocks_mobile_activation()
    {
        Assert.Equal(EditVideoActivationDecision.LimitReached,
            EditVideoDevicePolicy.Decide(License(desktop: 1, mobile: 0), [], HashA, EditVideoDeviceKinds.Mobile));
    }

    [Fact]
    public void License_blocker_reports_status_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Null(EditVideoDevicePolicy.LicenseBlocker(License(), now));
        Assert.Null(EditVideoDevicePolicy.LicenseBlocker(License(expires: now.AddDays(1)), now));
        Assert.Equal(EditVideoErrorCodes.LicenseExpired, EditVideoDevicePolicy.LicenseBlocker(License(expires: now.AddSeconds(-1)), now));
        Assert.Equal(EditVideoErrorCodes.LicenseSuspended, EditVideoDevicePolicy.LicenseBlocker(License(status: "suspended"), now));
        Assert.Equal(EditVideoErrorCodes.LicenseRevoked, EditVideoDevicePolicy.LicenseBlocker(License(status: "revoked"), now));
    }

    [Fact]
    public void Signed_license_verifies_and_tampering_is_detected()
    {
        using var signer = new EditVideoLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var payload = new EditVideoLicensePayload(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), HashA, "desktop", "standard", 1, null, 2);

        var document = signer.Sign(payload);

        Assert.Equal(payload, signer.Verify(document));

        var parts = document.Split('.');
        var forged = signer.Sign(payload with { Dh = HashB }).Split('.')[0] + "." + parts[1];
        Assert.Null(signer.Verify(forged));
        Assert.Null(signer.Verify("not-a-license"));
        Assert.Contains("BEGIN PUBLIC KEY", signer.PublicKeyPem);
    }

    [Theory]
    [InlineData(EditVideoErrorCodes.DeviceLimitReached, 409)]
    [InlineData(EditVideoErrorCodes.InvalidToken, 401)]
    [InlineData(EditVideoErrorCodes.DeviceMismatch, 401)]
    [InlineData(EditVideoErrorCodes.InvalidLicense, 404)]
    [InlineData(EditVideoErrorCodes.LicenseExpired, 403)]
    [InlineData(EditVideoErrorCodes.SigningUnavailable, 503)]
    [InlineData("insufficient_points", 402)]
    [InlineData("voice_service_unavailable", 503)]
    public void Error_codes_map_to_http_status(string code, int status) =>
        Assert.Equal(status, EditVideoEndpoints.StatusFor(code));
}
