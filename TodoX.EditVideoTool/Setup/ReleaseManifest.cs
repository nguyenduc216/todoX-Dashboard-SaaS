using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Setup;

public sealed record ReleaseFile(string Name, string Kind, string? Component, string? Version, string Sha256, long Size, string? Url);

public sealed record ReleaseManifest(string Version, string MinSupported, DateTimeOffset PublishedAt, string? Notes, IReadOnlyList<ReleaseFile> Files)
{
    public ReleaseFile? App => Files.FirstOrDefault(f => f.Kind == "app");
    public IEnumerable<ReleaseFile> Components => Files.Where(f => f.Kind == "component" && f.Component is not null);
}

/// <summary>Downloads and installs files listed in TodoX's release manifest, verifying each SHA-256.</summary>
public sealed class ComponentInstaller
{
    private readonly TodoXServerClient _server;
    private readonly AgentPaths _paths;

    public ComponentInstaller(TodoXServerClient server, AgentPaths paths)
    {
        _server = server;
        _paths = paths;
    }

    public string UpdatesDir => Path.Combine(_paths.Root, "updates");

    public async Task<ReleaseManifest?> LatestAsync(CancellationToken ct)
    {
        var r = await _server.LatestReleaseAsync(ct);
        return r.Ok ? r.Value.Deserialize<ReleaseManifest>(TodoXServerClient.Json) : null;
    }

    public string? InstalledVersion(string component)
    {
        var marker = Path.Combine(_paths.ComponentsDir, component, ".todox-version");
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    /// <summary>Downloads to updates/, verifies, then returns the local path.</summary>
    public async Task<string> DownloadVerifiedAsync(ReleaseFile file, IProgress<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(UpdatesDir);
        var target = Path.Combine(UpdatesDir, file.Name);
        if (!File.Exists(target) || !Sha256Matches(target, file.Sha256))
        {
            var last = -10.0;
            await _server.DownloadAsync(file.Url!, target, new Progress<double>(p =>
            {
                if (p - last >= 10)
                {
                    last = p;
                    status?.Report($"  Tải {file.Name}: {p:0}%");
                }
            }), ct);
        }

        if (!Sha256Matches(target, file.Sha256))
        {
            File.Delete(target);
            throw new InvalidOperationException($"File {file.Name} tải về bị lỗi (sai checksum). Thử lại.");
        }

        return target;
    }

    /// <summary>Extracts a component zip next to the current copy, then swaps folders so a failure leaves the old one.</summary>
    public async Task InstallComponentAsync(ReleaseFile file, IProgress<string>? status, CancellationToken ct)
    {
        var zip = await DownloadVerifiedAsync(file, status, ct);
        var dest = Path.Combine(_paths.ComponentsDir, file.Component!);
        var staging = dest + ".new";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        status?.Report($"  Giải nén {file.Component}...");
        ZipFile.ExtractToDirectory(zip, staging);
        await File.WriteAllTextAsync(Path.Combine(staging, ".todox-version"), file.Version ?? file.Sha256, ct);

        var old = dest + ".old";
        if (Directory.Exists(old))
        {
            Directory.Delete(old, recursive: true);
        }

        if (Directory.Exists(dest))
        {
            Directory.Move(dest, old);
        }

        Directory.Move(staging, dest);
        if (Directory.Exists(old))
        {
            try { Directory.Delete(old, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        File.Delete(zip);
    }

    public static bool Sha256Matches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase);
    }
}
