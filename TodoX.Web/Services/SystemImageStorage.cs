using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace TodoX.Web.Services;

public sealed class SystemImageStorage
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly IWebHostEnvironment _env;
    private readonly NavigationManager _nav;

    public SystemImageStorage(IWebHostEnvironment env, NavigationManager nav)
    {
        _env = env;
        _nav = nav;
    }

    public Task<string> SaveMrTodoXAvatarAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveImageAsync(file, "mr-todox", ct);

    public Task<string> SaveServiceThumbnailAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveImageAsync(file, "service-thumbnail", ct);

    public Task<string> SaveServiceReferenceImageAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveImageAsync(file, "service-reference", ct);

    private async Task<string> SaveImageAsync(IBrowserFile file, string prefix, CancellationToken ct)
    {
        if (file is null)
        {
            throw new InvalidOperationException("Không có file upload.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("Chỉ hỗ trợ ảnh JPG, PNG hoặc WEBP.");
        }

        if (file.Size > MaxBytes)
        {
            throw new InvalidOperationException("Ảnh không được vượt quá 10MB.");
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = file.ContentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
        }

        var safeFileName = $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var relativeFolder = Path.Combine("uploads", "system");
        var absoluteFolder = Path.Combine(_env.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var absolutePath = Path.Combine(absoluteFolder, safeFileName);
        await using var input = file.OpenReadStream(MaxBytes, ct);
        await using var output = File.Create(absolutePath);
        await input.CopyToAsync(output, ct);

        var relativeUrl = $"/uploads/system/{safeFileName}";
        return new Uri(new Uri(_nav.BaseUri), relativeUrl.TrimStart('/')).ToString();
    }
}
