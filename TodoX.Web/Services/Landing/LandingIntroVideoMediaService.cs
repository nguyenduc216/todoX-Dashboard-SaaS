using System.Diagnostics;
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
        var (source, output) = GetTemporaryPaths(temp, token, ext);
        var transcode = settings.VideoTranscode;
        var useFfmpeg = video && transcode.Enabled;
        try
        {
            await using (var stream = File.Create(source))
            await using (var input = file.OpenReadStream(limit))
                await input.CopyToAsync(stream, ct);
            var header = new byte[16];
            await using (var stream = File.OpenRead(source)) _ = await stream.ReadAsync(header, ct);
            if (!HasValidHeader(ext, header)) throw new InvalidOperationException("Nội dung file không hợp lệ.");
            if (useFfmpeg)
            {
                var psi = new ProcessStartInfo(transcode.FfmpegPath) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-preset", transcode.Preset, "-crf", transcode.Crf.ToString(), "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-c:a", "aac", "-b:a", $"{transcode.AudioBitrateKbps}k", output }) psi.ArgumentList.Add(arg);
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg unavailable.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, transcode.TimeoutSeconds)));
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw;
                }
                _ = await errors;
                if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                    throw new InvalidOperationException("Không thể xử lý video.");
            }
            var finalExtension = useFfmpeg ? ".mp4" : ext;
            var filename = token + finalExtension;
            File.Move(useFfmpeg ? output : source, Path.Combine(destination, filename));
            return paths.GetIntroPublicUrl(folder, filename);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    public static (string Source, string Output) GetTemporaryPaths(string folder, string token, string extension)
        => (Path.Combine(folder, token + extension), Path.Combine(folder, token + "-encoded.mp4"));

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