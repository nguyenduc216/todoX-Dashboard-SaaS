using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.Landing;

public sealed class LandingIndustryMediaService
{
    private static readonly Dictionary<string, string[]> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".mp4"] = ["video/mp4"],
        [".webm"] = ["video/webm"]
    };

    private readonly SharedMediaOptions _options;
    private readonly SharedMediaPathService _paths;

    public LandingIndustryMediaService(IOptions<SharedMediaOptions> options, SharedMediaPathService paths)
    {
        _options = options.Value;
        _paths = paths;
    }

    public bool IsReady => _paths.IsConfigured && _paths.CanWrite();

    public Task<string> SaveThumbnailAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveAsync(
            file,
            _options.IndustrySolutions.ThumbnailSubfolder,
            _options.IndustrySolutions.AllowedThumbnailExtensions,
            _options.IndustrySolutions.MaxThumbnailBytes,
            ct);

    public Task<string> SaveVideoAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveAsync(
            file,
            _options.IndustrySolutions.VideoSubfolder,
            _options.IndustrySolutions.AllowedVideoExtensions,
            _options.IndustrySolutions.MaxVideoBytes,
            ct);

    public void DeleteIfSharedMedia(string? url) => TryDeleteOld(url);

    private async Task<string> SaveAsync(
        IBrowserFile file,
        string subfolder,
        IReadOnlyCollection<string> allowedExtensions,
        long maxBytes,
        CancellationToken ct)
    {
        if (!_paths.IsConfigured)
        {
            throw new InvalidOperationException("SharedMedia:StorageRoot chưa được cấu hình.");
        }

        if (file.Size <= 0 || file.Size > maxBytes)
        {
            throw new InvalidOperationException($"File vượt quá dung lượng cho phép ({maxBytes / 1024 / 1024} MB).");
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Định dạng file không được hỗ trợ.");
        }

        if (!AllowedContentTypes.TryGetValue(extension, out var contentTypes)
            || !contentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Content-Type của file không hợp lệ.");
        }

        var tempFolder = _paths.GetIndustryPhysicalFolder(_options.IndustrySolutions.TempSubfolder);
        var finalFolder = _paths.GetIndustryPhysicalFolder(subfolder);
        Directory.CreateDirectory(tempFolder);
        Directory.CreateDirectory(finalFolder);

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var tempPath = Path.Combine(tempFolder, $"{fileName}.tmp");
        var finalPath = Path.Combine(finalFolder, fileName);

        await using (var target = File.Create(tempPath))
        await using (var source = file.OpenReadStream(maxBytes))
        {
            await source.CopyToAsync(target, ct);
        }

        File.Move(tempPath, finalPath, overwrite: false);

        var publicUrl = _paths.GetIndustryPublicUrl(subfolder, fileName);
        return publicUrl;
    }

    private void TryDeleteOld(string? oldUrl)
    {
        if (string.IsNullOrWhiteSpace(oldUrl))
        {
            return;
        }

        try
        {
            var path = _paths.ResolvePublicUrlToPhysicalPath(oldUrl);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only; stale media must not break a successful save.
        }
    }
}
