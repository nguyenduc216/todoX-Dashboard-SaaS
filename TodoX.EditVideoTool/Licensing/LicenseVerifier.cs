using System.Security.Cryptography;
using System.Text.Json;

namespace TodoX.EditVideoTool.Licensing;

/// <summary>Mirror of the server's EditVideoLicensePayload (short field names on purpose).</summary>
public sealed record LicensePayload(string Lid, string Cid, string Dh, string Dk, string Plan, long Iat, long? Exp, long Hbd);

/// <summary>Checks the ES256 signature of a license document against the TodoX public key compiled into the exe.</summary>
public sealed class LicenseVerifier
{
    /// <summary>
    /// Public half of TodoX.Web/keys/editvideo-license-signing.pem. Rotating the server key requires shipping
    /// a new exe, so the private key must be backed up and kept stable.
    /// </summary>
    public const string ProductionPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbjGuXZXor9DPWsn28NZPoXhSyueD
        jFrMxycxu/42AsnKPADMy/rmnjFFskoFPpzOcG8/DUZPZixZiQlsdY6kmw==
        -----END PUBLIC KEY-----
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _publicKeyPem;

    public LicenseVerifier(string? publicKeyPem = null)
    {
        _publicKeyPem = publicKeyPem ?? ProductionPublicKeyPem;
    }

    public static LicenseVerifier Default()
    {
#if DEBUG
        // Local development against a dashboard that auto-generated its own key.
        var overridePem = Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_PUBLIC_KEY");
        if (!string.IsNullOrWhiteSpace(overridePem))
        {
            return new LicenseVerifier(overridePem.Replace("\\n", "\n"));
        }
#endif
        return new LicenseVerifier();
    }

    public LicensePayload? Verify(string? document)
    {
        try
        {
            return VerifyBody(document) is { } body ? JsonSerializer.Deserialize<LicensePayload>(body, Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns the signed bytes of any TodoX "body.signature" document (license, edit plan), or null.</summary>
    public byte[]? VerifyBody(string? document)
    {
        var parts = document?.Split('.');
        if (parts is not { Length: 2 })
        {
            return null;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(_publicKeyPem);
            var body = FromBase64Url(parts[0]);
            return key.VerifyData(body, FromBase64Url(parts[1]), HashAlgorithmName.SHA256) ? body : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
