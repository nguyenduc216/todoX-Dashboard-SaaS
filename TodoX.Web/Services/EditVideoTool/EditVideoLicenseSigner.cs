using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoToolOptions
{
    public const string SectionName = "EditVideoTool";

    /// <summary>PEM file with the ECDSA P-256 private key. Relative paths resolve against the content root.</summary>
    public string LicenseSigningKeyPath { get; set; } = "keys/editvideo-license-signing.pem";

    /// <summary>Only for local development: create the key file when it is missing.</summary>
    public bool AutoGenerateSigningKey { get; set; }

    public int AccessTokenHours { get; set; } = 24;

    /// <summary>How long the local tool keeps working offline before it must heartbeat again.</summary>
    public int OfflineGraceDays { get; set; } = 7;

    /// <summary>Folder with manifest.json, the installer and component zips; kept outside the site folder.</summary>
    public string DownloadsRoot { get; set; } = "C:/TodoXDeploy/downloads/editvideo";
}

/// <summary>
/// Signed license payload the exe verifies offline with the embedded public key.
/// Field names are short on purpose: the file sits on the customer's disk and is read by the tool, not people.
/// </summary>
public sealed record EditVideoLicensePayload(
    string Lid,
    string Cid,
    string Dh,
    string Dk,
    string Plan,
    long Iat,
    long? Exp,
    long Hbd);

/// <summary>Signs license documents as base64url(payload) + "." + base64url(ES256 signature).</summary>
public sealed class EditVideoLicenseSigner : IDisposable
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly ECDsa? _key;
    private readonly string? _unavailableReason;

    public EditVideoLicenseSigner(IOptions<EditVideoToolOptions> options, IWebHostEnvironment env, ILogger<EditVideoLicenseSigner> logger)
    {
        var opts = options.Value;
        var path = Path.IsPathRooted(opts.LicenseSigningKeyPath)
            ? opts.LicenseSigningKeyPath
            : Path.Combine(env.ContentRootPath, opts.LicenseSigningKeyPath);

        try
        {
            if (!File.Exists(path))
            {
                if (!opts.AutoGenerateSigningKey)
                {
                    _unavailableReason = $"License signing key not found at '{path}'.";
                    logger.LogWarning("EDITVIDEO_SIGNING_KEY_MISSING path={Path}", path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                File.WriteAllText(path, generated.ExportPkcs8PrivateKeyPem());
                logger.LogWarning("EDITVIDEO_SIGNING_KEY_GENERATED path={Path}", path);
            }

            _key = ECDsa.Create();
            _key.ImportFromPem(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            _key?.Dispose();
            _key = null;
            _unavailableReason = "License signing key could not be loaded.";
            logger.LogError(ex, "EDITVIDEO_SIGNING_KEY_LOAD_FAILED path={Path}", path);
        }
    }

    internal EditVideoLicenseSigner(ECDsa key)
    {
        _key = key;
    }

    public bool IsAvailable => _key is not null;

    public string? UnavailableReason => _unavailableReason;

    public string PublicKeyPem => _key?.ExportSubjectPublicKeyInfoPem()
        ?? throw new InvalidOperationException(_unavailableReason ?? "Signing key unavailable.");

    public string Sign(EditVideoLicensePayload payload) => SignDocument(JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJson));

    /// <summary>Signs arbitrary bytes (e.g. an edit plan) in the same "body.signature" format the exe verifies.</summary>
    public string SignDocument(byte[] body)
    {
        if (_key is null)
        {
            throw new InvalidOperationException(_unavailableReason ?? "Signing key unavailable.");
        }

        var signature = _key.SignData(body, HashAlgorithmName.SHA256);
        return $"{Base64Url(body)}.{Base64Url(signature)}";
    }

    /// <summary>Server-side mirror of the exe's check; used by tests and diagnostics.</summary>
    public EditVideoLicensePayload? Verify(string document)
    {
        if (_key is null)
        {
            return null;
        }

        var parts = document.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var body = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);
            return _key.VerifyData(body, signature, HashAlgorithmName.SHA256)
                ? JsonSerializer.Deserialize<EditVideoLicensePayload>(body, PayloadJson)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public void Dispose() => _key?.Dispose();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
