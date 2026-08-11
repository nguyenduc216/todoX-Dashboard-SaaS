using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.SharedMedia;

public sealed class SharedMediaPathService
{
    private readonly SharedMediaOptions _options;

    public SharedMediaPathService(IOptions<SharedMediaOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.StorageRoot);

    public string? StorageRoot
        => string.IsNullOrWhiteSpace(_options.StorageRoot)
            ? null
            : Path.GetFullPath(_options.StorageRoot);

    public bool CanWrite()
    {
        var root = StorageRoot;
        if (root is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetIndustryPhysicalFolder(string subfolder)
    {
        var root = StorageRoot ?? throw new InvalidOperationException("SharedMedia:StorageRoot chưa được cấu hình.");
        return CombineInsideRoot(root, _options.IndustrySolutions.RootSubfolder, subfolder);
    }

    public string GetIndustryPublicUrl(string subfolder, string fileName)
    {
        var requestPath = NormalizeRequestPath(_options.RequestPath);
        var rootSubfolder = NormalizeUrlSegment(_options.IndustrySolutions.RootSubfolder);
        var child = NormalizeUrlSegment(subfolder);
        return $"{requestPath}/{rootSubfolder}/{child}/{Uri.EscapeDataString(fileName)}";
    }

    public string ResolvePublicUrlToPhysicalPath(string publicUrl)
    {
        var requestPath = NormalizeRequestPath(_options.RequestPath);
        if (!publicUrl.StartsWith(requestPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Media URL không thuộc SharedMedia request path.");
        }

        var relative = publicUrl[(requestPath.Length + 1)..]
            .Replace('/', Path.DirectorySeparatorChar);

        var root = StorageRoot ?? throw new InvalidOperationException("SharedMedia:StorageRoot chưa được cấu hình.");
        return CombineInsideRoot(root, relative);
    }

    private static string CombineInsideRoot(string root, params string[] parts)
    {
        var combined = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn media nằm ngoài SharedMedia:StorageRoot.");
        }

        return combined;
    }

    private static string NormalizeRequestPath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/media" : value.Trim();
        return "/" + path.Trim('/');
    }

    private static string NormalizeUrlSegment(string value)
        => value.Replace('\\', '/').Trim('/');
}
