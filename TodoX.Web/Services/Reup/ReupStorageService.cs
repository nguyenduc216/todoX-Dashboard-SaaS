using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class ReupStorageService
{
    private readonly ReupCampaignOptions _options;

    public ReupStorageService(IOptions<ReupCampaignOptions> options)
    {
        _options = options.Value;
    }

    public string CacheRoot =>
        _options.cache_video
        ?? _options.CacheVideoPath
        ?? Path.Combine(AppContext.BaseDirectory, "cache_video");

    public string GetVideoPath(Guid customerId, Guid referenceVideoId)
        => Path.Combine(CacheRoot, customerId.ToString("N"), $"{referenceVideoId:N}.mp4");

    public void EnsureReady()
    {
        var root = CacheRoot;
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            _ = File.ReadAllText(probe);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"TodoX không có quyền đọc/ghi thư mục cache_video '{root}'. Vui lòng kiểm tra đường dẫn và quyền Windows/IIS. Chi tiết: {ex.Message}", ex);
        }
    }

    public void EnsureCustomerFolder(Guid customerId)
    {
        EnsureReady();
        Directory.CreateDirectory(Path.Combine(CacheRoot, customerId.ToString("N")));
    }
}
