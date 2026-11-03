using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoReleaseFile
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "component"; // app | component
    public string? Component { get; set; }           // node | remotion | ffmpeg
    public string? Version { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Url { get; set; }
}

public sealed class EditVideoReleaseManifest
{
    public string Version { get; set; } = "0.0.0";
    public string MinSupported { get; set; } = "0.0.0";
    public DateTimeOffset PublishedAt { get; set; }
    public string? Notes { get; set; }
    public List<EditVideoReleaseFile> Files { get; set; } = [];
}

/// <summary>
/// Installer, components and updates are served from a folder OUTSIDE the site (so deploys never wipe it),
/// described by manifest.json. Only files listed in the manifest are downloadable.
/// </summary>
public sealed partial class EditVideoReleaseService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _root;
    private readonly IConfiguration _configuration;

    public EditVideoReleaseService(IOptions<EditVideoToolOptions> options, IConfiguration configuration)
    {
        _root = options.Value.DownloadsRoot;
        _configuration = configuration;
    }

    public EditVideoReleaseManifest? GetLatest()
    {
        var path = Path.Combine(_root, "manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<EditVideoReleaseManifest>(File.ReadAllText(path), Json);
        if (manifest is null)
        {
            return null;
        }

        var publicBase = (_configuration["TodoX:PublicBaseUrl"] ?? "https://dashboard.todox.vn").TrimEnd('/');
        foreach (var f in manifest.Files)
        {
            f.Url = $"{publicBase}/downloads/editvideo/{Uri.EscapeDataString(f.Name)}";
        }

        return manifest;
    }

    /// <summary>Full path of a downloadable file, or null when the name is not a listed, safe file name.</summary>
    public string? ResolveFile(string name)
    {
        if (!SafeName().IsMatch(name))
        {
            return null;
        }

        var manifest = GetLatest();
        if (manifest?.Files.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)) != true)
        {
            return null;
        }

        var path = Path.Combine(_root, name);
        return File.Exists(path) ? path : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,120}$")]
    private static partial Regex SafeName();
}
