using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TodoX.EditVideoTool.Licensing;

/// <summary>What the tool keeps on disk after activation.</summary>
public sealed record StoredLicense(
    string License,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset HeartbeatDueAt,
    string ServerUrl);

/// <summary>
/// Persists the license with Windows DPAPI (current user scope), so the file is useless if copied to
/// another machine or account. The signed license inside is still independently checked against the device hash.
/// </summary>
public sealed class LicenseStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("todox-editvideo/license-store/v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public LicenseStore(AgentPaths paths)
    {
        _path = paths.LicenseFile;
    }

    public bool Exists => File.Exists(_path);

    public StoredLicense? Load()
    {
        if (!File.Exists(_path) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredLicense>(plain, Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public void Save(StoredLicense license)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("TodoX EditVideoTool currently supports Windows only.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(license, Json);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        File.Move(tmp, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
