namespace TodoX.Web.Services.Media;

public enum LocalMediaPathSource
{
    SourceFilePath,
    StorageKey,
    PublicUrl
}

public sealed class LocalMediaPathResolver
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    public LocalMediaPathResolver(IWebHostEnvironment env, IConfiguration configuration)
    {
        _env = env;
        _configuration = configuration;
    }

    public bool TryResolveExistingFile(string? value, LocalMediaPathSource source, out string path)
        => TryResolveExistingFile(
            value,
            source,
            _env.ContentRootPath,
            _configuration["Storage:LocalUploadRoot"] ?? "wwwroot/uploads",
            _configuration["Storage:PublicUploadBase"] ?? "/uploads",
            out path);

    public static bool TryResolveExistingFile(
        string? value,
        LocalMediaPathSource source,
        string contentRootPath,
        string uploadRoot,
        string publicUploadBase,
        out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (source == LocalMediaPathSource.SourceFilePath && Path.IsPathRooted(candidate))
        {
            if (File.Exists(candidate))
            {
                path = Path.GetFullPath(candidate);
                return true;
            }

            return false;
        }

        if (source != LocalMediaPathSource.PublicUrl && Path.IsPathRooted(candidate)
            || !TryNormalizeObjectKey(candidate, source, publicUploadBase, out var objectKey))
        {
            return false;
        }

        var absoluteRoot = Path.GetFullPath(Path.Combine(contentRootPath, uploadRoot));
        var absolutePath = Path.GetFullPath(Path.Combine(
            absoluteRoot,
            objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(absolutePath, absoluteRoot) || !File.Exists(absolutePath))
        {
            return false;
        }

        path = absolutePath;
        return true;
    }

    public static bool IsLocalPublicUrl(string? value, string publicUploadBase)
        => TryNormalizeObjectKey(value, LocalMediaPathSource.PublicUrl, publicUploadBase, out _);

    private static bool TryNormalizeObjectKey(
        string? value,
        LocalMediaPathSource source,
        string publicUploadBase,
        out string objectKey)
    {
        objectKey = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().Replace('\\', '/');
        if (source == LocalMediaPathSource.PublicUrl)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
            {
                candidate = absoluteUri.AbsolutePath;
            }

            var basePath = (publicUploadBase ?? "/uploads").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(basePath)
                || !candidate.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            candidate = candidate[(basePath.Length + 1)..];
        }

        candidate = candidate.TrimStart('/');
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(candidate))
        {
            return false;
        }

        objectKey = candidate;
        return true;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
