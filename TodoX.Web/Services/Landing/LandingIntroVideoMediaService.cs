using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.SharedMedia;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Landing;

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
                await CopyOriginalAsync(input, source, ct);
            var header = new byte[16];
            await using (var stream = File.OpenRead(source)) _ = await stream.ReadAsync(header, ct);
            if (!HasValidHeader(ext, header)) throw new InvalidOperationException("Nội dung file không hợp lệ.");
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
        await input.CopyToAsync(output, ct);
    }

    public static void ValidateUpload(string name, string contentType, long size, bool video, long limit)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var mime = video ? ext switch { ".mp4" => "video/mp4", ".webm" => "video/webm", _ => null }
            : ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
        if (mime is null || !string.Equals(mime, contentType, StringComparison.OrdinalIgnoreCase)
            || size <= 0 || size > limit)
            throw new InvalidOperationException("Định dạng hoặc dung lượng file không hợp lệ.");
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