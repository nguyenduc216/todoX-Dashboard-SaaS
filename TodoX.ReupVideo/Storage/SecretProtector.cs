using System.Security.Cryptography;
using System.Text;

namespace TodoX.ReupVideo.Storage;

/// <summary>Encrypts fanpage tokens with Windows DPAPI so settings.json never holds them in plain text.</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("todox-reupvideo-v1");

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return "";
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // Settings copied from another Windows account/computer cannot be decrypted: the token must be re-entered.
            return "";
        }
    }

    /// <summary>Shows only the start and end of a secret, for the UI and logs.</summary>
    public static string Mask(string? secret) =>
        string.IsNullOrEmpty(secret) ? "(chưa có)" : secret.Length <= 12 ? "••••" : $"{secret[..6]}••••{secret[^4..]}";
}
