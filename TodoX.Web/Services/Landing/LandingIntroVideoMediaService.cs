using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.SharedMedia;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Landing;

public enum IntroUploadError { TooLarge, InvalidFile, StreamInterrupted, StorageUnavailable, Other }

public sealed class IntroUploadException(IntroUploadError category, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public IntroUploadError Category { get; } = category;
}

public static class IntroUploadDiagnostics
{
    public static string SafeFileName(string? name)
    {
        var leaf = (name ?? "").Replace('\\', '/').Split('/').Last();
        var safe = new string(leaf.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').Take(80).ToArray());
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }

    public static IntroUploadError Classify(Exception ex) => ex switch
    {
        IntroUploadException upload => upload.Category,
        UnauthorizedAccessException or System.Security.SecurityException => IntroUploadError.StorageUnavailable,
        IOException => IntroUploadError.StorageUnavailable,
        OperationCanceledException => IntroUploadError.StreamInterrupted,
        _ => IntroUploadError.Other
    };

    public static string Message(IntroUploadError error, long maxBytes) => error switch
    {
        IntroUploadError.TooLarge => $"Tệp vượt quá giới hạn {maxBytes / (1024 * 1024)} MB. Vui lòng chọn tệp nhỏ hơn.",
        IntroUploadError.InvalidFile => "Tệp không hợp lệ. Kiểm tra định dạng, nội dung và dung lượng tệp.",
        IntroUploadError.StreamInterrupted => "Truyền tệp bị gián đoạn. Vui lòng thử tải lại.",
        IntroUploadError.StorageUnavailable => "Không thể ghi tệp vào SharedMedia. Vui lòng liên hệ quản trị hệ thống.",
        _ => "Không thể tải tệp lên. Vui lòng thử lại hoặc liên hệ quản trị hệ thống."
    };

    public static IntroUploadError Classify(Exception ex, bool storageAvailable)
        => ex is IntroUploadException or OperationCanceledException ? Classify(ex)
            : !storageAvailable ? IntroUploadError.StorageUnavailable : Classify(ex);
}

public sealed class LandingIntroVideoMediaService(SharedMediaPathService paths, IOptions<SharedMediaOptions> options, AuthStateService auth)
{
    public async Task<string> UploadAsync(IBrowserFile file, bool video, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        var settings = options.Value.IndustrySolutions;
        var limit = video ? settings.MaxVideoBytes : settings.MaxThumbnailBytes;
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        ValidateUpload(file.Name, file.ContentType, file.Size, video, limit);
        var folder = video ? "videos" : "posters";
        var temp = paths.GetIntroPhysicalFolder("temp");
        var destination = paths.GetIntroPhysicalFolder(folder);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(destination);
        var token = Guid.NewGuid().ToString("N");
        var source = Path.Combine(temp, token + ext);
        try
        {
            await using (var input = file.OpenReadStream(limit))
            {
                await CopyOriginalAsync(input, source, ct);
            }
            var header = new byte[16];
            await using (var stream = File.OpenRead(source)) _ = await stream.ReadAsync(header, ct);
            if (!HasValidHeader(ext, header)) throw new IntroUploadException(IntroUploadError.InvalidFile, "Nội dung file không hợp lệ.");
            var filename = token + ext;
            File.Move(source, Path.Combine(destination, filename));
            return paths.GetIntroPublicUrl(folder, filename);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
        }
    }

    public static async Task CopyOriginalAsync(Stream input, string path, CancellationToken ct = default)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        while (true)
        {
            int count;
            try { count = await input.ReadAsync(buffer, ct); }
            catch (IOException ex) { throw new IntroUploadException(IntroUploadError.StreamInterrupted, "Upload stream interrupted.", ex); }
            if (count == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }

    public static void ValidateUpload(string name, string contentType, long size, bool video, long limit)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var mime = video ? ext switch { ".mp4" => "video/mp4", ".webm" => "video/webm", _ => null }
            : ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
        if (size > limit) throw new IntroUploadException(IntroUploadError.TooLarge, "Tệp vượt quá dung lượng cho phép.");
        if (mime is null || !string.Equals(mime, contentType, StringComparison.OrdinalIgnoreCase) || size <= 0)
            throw new IntroUploadException(IntroUploadError.InvalidFile, "Định dạng hoặc dung lượng file không hợp lệ.");
    }

    public static bool HasValidHeader(string ext, byte[] header)
        => header.Length >= 16 && (ext.ToLowerInvariant() switch
            {
                ".mp4" => System.Text.Encoding.ASCII.GetString(header, 4, 4) == "ftyp",
                ".webm" => header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
                ".png" => header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47 }),
                ".jpg" or ".jpeg" => header[0] == 0xff && header[1] == 0xd8,
                ".webp" => System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP",
                _ => false
            });
}