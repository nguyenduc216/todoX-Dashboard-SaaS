using System.Security.Cryptography;
using System.Text;

namespace TodoX.Web.Services.AiProviders;

public interface IProviderCredentialProtector
{
    string Normalize(string plaintext);
    string Fingerprint(string plaintext);
    string? MaskHint(string plaintext);
    Task<ProtectedProviderCredential> ProtectAsync(string plaintext, CancellationToken ct = default);
    Task<string> UnprotectAsync(ProviderSecureCredentialRecord credential, CancellationToken ct = default);
}

public sealed class ProviderCredentialProtector : IProviderCredentialProtector
{
    public const string Algorithm = "AES-256-GCM";
    private readonly IProviderCredentialKeyStore _keyStore;

    public ProviderCredentialProtector(IProviderCredentialKeyStore keyStore)
    {
        _keyStore = keyStore;
    }

    public string Normalize(string plaintext)
        => string.IsNullOrWhiteSpace(plaintext)
            ? throw new InvalidOperationException("Credential is empty.")
            : plaintext.Trim();

    public string Fingerprint(string plaintext)
    {
        var normalized = Normalize(plaintext);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string? MaskHint(string plaintext)
    {
        var normalized = Normalize(plaintext);
        if (normalized.Length < 4)
        {
            return null;
        }

        return "****" + normalized[^Math.Min(4, normalized.Length)..];
    }

    public async Task<ProtectedProviderCredential> ProtectAsync(string plaintext, CancellationToken ct = default)
    {
        var normalized = Normalize(plaintext);
        var key = await _keyStore.GetActiveKeyAsync(ct);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plaintextBytes = Encoding.UTF8.GetBytes(normalized);
        var ciphertext = new byte[plaintextBytes.Length];

        using var aes = new AesGcm(key.KeyMaterial, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return new ProtectedProviderCredential(
            ciphertext,
            nonce,
            tag,
            key.KeyVersion,
            Algorithm,
            Fingerprint(normalized),
            MaskHint(normalized));
    }

    public async Task<string> UnprotectAsync(ProviderSecureCredentialRecord credential, CancellationToken ct = default)
    {
        try
        {
            var key = await _keyStore.GetKeyByVersionAsync(credential.KeyVersion, ct);
            var plaintext = new byte[credential.Ciphertext.Length];
            using var aes = new AesGcm(key.KeyMaterial, 16);
            aes.Decrypt(credential.Nonce, credential.Ciphertext, credential.AuthTag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
        {
            throw new InvalidOperationException("Credential could not be decrypted.", ex);
        }
    }
}
