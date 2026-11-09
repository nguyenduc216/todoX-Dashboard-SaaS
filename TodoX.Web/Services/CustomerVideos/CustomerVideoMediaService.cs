using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.Landing;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.CustomerVideos;

/// <summary>
/// Uploads the video / poster of a customer video into SharedMedia "customer-videos". Same checks and web
/// transcode (720p) as landing uploads; files get a random 32-hex name and stay behind the media guard.
/// </summary>
public sealed class CustomerVideoMediaService(SharedMediaPathService paths, IOptions<SharedMediaOptions> options, AuthStateService auth)
{
    public long MaxBytes(bool video) => video ? options.Value.VideoSampleMaxVideoBytes : options.Value.IndustrySolutions.MaxThumbnailBytes;

    public async Task<string> UploadAsync(IBrowserFile file, bool video, CancellationToken ct = default)
    {
        if (!AdminEndpointAuthorization.IsAdmin(auth.CurrentUser)) throw new UnauthorizedAccessException("Chỉ quản trị viên TodoX được tải video khách hàng.");
        var limit = MaxBytes(video);
        LandingIntroVideoMediaService.ValidateUpload(file.Name, file.ContentType, file.Size, video, limit);
        var folder = video ? "videos" : "posters";
        var temp = paths.GetCustomerVideoPhysicalFolder("temp");
        var destination = paths.GetCustomerVideoPhysicalFolder(folder);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(destination);
        var token = Guid.NewGuid().ToString("N");
        var source = Path.Combine(temp, token + Path.GetExtension(file.Name).ToLowerInvariant());
        string? optimized = null;
        try
        {
            await using (var input = file.OpenReadStream(limit))
            {
                await LandingIntroVideoMediaService.CopyOriginalAsync(input, source, ct);
            }
            var header = new byte[16];
            await using (var stream = File.OpenRead(source)) _ = await stream.ReadAsync(header, ct);
            var ext = LandingIntroVideoMediaService.DetectExtension(header, video)
                ?? throw new IntroUploadException(IntroUploadError.InvalidFile, "Nội dung file không hợp lệ.");
            var transcode = options.Value.IndustrySolutions.VideoTranscode;
            if (video && transcode.Enabled)
            {
                optimized = Path.Combine(temp, token + ".web.mp4");
                await LandingWebVideoTranscoder.TranscodeAsync(source, optimized, transcode, ct);
                File.Move(optimized, Path.Combine(destination, token + ".mp4"));
                return paths.GetCustomerVideoPublicUrl(folder, token + ".mp4");
            }
            File.Move(source, Path.Combine(destination, token + ext));
            return paths.GetCustomerVideoPublicUrl(folder, token + ext);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (optimized is not null && File.Exists(optimized)) File.Delete(optimized);
        }
    }
}
