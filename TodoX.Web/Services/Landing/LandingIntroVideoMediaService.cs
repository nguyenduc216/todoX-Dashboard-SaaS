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

public sealed record LandingMediaFile(string Url, string FileName, long Size, DateTime UploadedAtUtc);

public sealed class LandingIntroVideoMediaService(SharedMediaPathService paths, IOptions<SharedMediaOptions> options, AuthStateService auth)
{
    public Task<string> UploadAsync(IBrowserFile file, bool video, CancellationToken ct = default)
        => UploadAsync(file, video, "intro", ct);

    /// <param name="area">Landing media area: "intro" (homepage intro), "short-video" (/video-ngan page) or "video-samples" (/mau-video page).</param>
    public async Task<string> UploadAsync(IBrowserFile file, bool video, string area, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        var limit = MaxBytes(options.Value, area, video);
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        ValidateUpload(file.Name, file.ContentType, file.Size, video, limit);
        var folder = video ? "videos" : "posters";
        var temp = paths.GetLandingMediaPhysicalFolder(area, "temp");
        var destination = paths.GetLandingMediaPhysicalFolder(area, folder);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(destination);
        var token = Guid.NewGuid().ToString("N");
        var source = Path.Combine(temp, token + ext);
        string? optimized = null;
        try
        {
            await using (var input = file.OpenReadStream(limit))
            {
                await CopyOriginalAsync(input, source, ct);
            }
            var header = new byte[16];
            await using (var stream = File.OpenRead(source)) _ = await stream.ReadAsync(header, ct);
            // The real format comes from the file's bytes: AI/editing tools often save JPEG or WebP under a .png name.
            var detected = DetectExtension(header, video)
                ?? throw new IntroUploadException(IntroUploadError.InvalidFile, "Nội dung file không hợp lệ.");
            ext = detected;
            var transcode = options.Value.IndustrySolutions.VideoTranscode;
            if (video && transcode.Enabled)
            {
                // Camera/AI exports are often 1440p+ at ~15 Mbps, more than the landing server can stream smoothly.
                optimized = Path.Combine(temp, token + ".web.mp4");
                await LandingWebVideoTranscoder.TranscodeAsync(source, optimized, transcode, ct);
                File.Move(optimized, Path.Combine(destination, token + ".mp4"));
                return paths.GetLandingMediaPublicUrl(area, folder, token + ".mp4");
            }
            var filename = token + ext;
            File.Move(source, Path.Combine(destination, filename));
            return paths.GetLandingMediaPublicUrl(area, folder, filename);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (optimized is not null && File.Exists(optimized)) File.Delete(optimized);
        }
    }

    /// <summary>
    /// Deletes a video-sample file that a newer version replaced ("Ghi đè file cũ"). Only files of the video-samples
    /// area with a generated name are accepted; the caller checks that no sample still uses it.
    /// </summary>
    public bool DeleteSampleUpload(string url)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        var folder = LandingVideoSampleRepository.IsMediaUrl(url, "videos") ? "videos"
            : LandingVideoSampleRepository.IsMediaUrl(url, "posters") ? "posters"
            : null;
        if (folder is null) return false;
        var path = Path.Combine(paths.GetLandingMediaPhysicalFolder(LandingVideoSampleRepository.MediaArea, folder), Path.GetFileName(url));
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Files already uploaded to a landing media area, newest first, so an admin can reuse one instead of uploading again.</summary>
    public IReadOnlyList<LandingMediaFile> ListUploads(string area, bool video, int take = 60)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        var folder = video ? "videos" : "posters";
        var physical = paths.GetLandingMediaPhysicalFolder(area, folder);
        if (!Directory.Exists(physical)) return [];
        string[] allowed = video ? [".mp4", ".webm"] : [".jpg", ".jpeg", ".png", ".webp"];
        return new DirectoryInfo(physical).EnumerateFiles()
            .Where(f => allowed.Contains(f.Extension.ToLowerInvariant()) && f.Length > 0)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(take)
            .Select(f => new LandingMediaFile(paths.GetLandingMediaPublicUrl(area, folder, f.Name), f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    public static long MaxBytes(SharedMediaOptions options, string area, bool video)
        => !video ? options.IndustrySolutions.MaxThumbnailBytes
            : area == LandingVideoSampleRepository.MediaArea ? options.VideoSampleMaxVideoBytes
            : options.IndustrySolutions.MaxVideoBytes;

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
        var knownExt = video ? ext is ".mp4" or ".webm"
            : ext is ".jpg" or ".jpeg" or ".png" or ".webp";
        // The name only pre-filters; the browser's reported type is not trusted (it follows the name, and some
        // browsers send none). The bytes decide after upload (DetectExtension).
        _ = contentType;
        if (size > limit) throw new IntroUploadException(IntroUploadError.TooLarge, "Tệp vượt quá dung lượng cho phép.");
        if (!knownExt || size <= 0)
            throw new IntroUploadException(IntroUploadError.InvalidFile, "Định dạng hoặc dung lượng file không hợp lệ.");
    }

    /// <summary>The file's real extension from its first bytes, or null when it is not an allowed video/image.</summary>
    public static string? DetectExtension(byte[] header, bool video)
    {
        if (header.Length < 16) return null;
        string[] candidates = video ? [".mp4", ".webm"] : [".jpg", ".png", ".webp"];
        return candidates.FirstOrDefault(c => HasValidHeader(c, header));
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