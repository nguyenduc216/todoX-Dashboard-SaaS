using TodoX.Web.Models;
using TodoX.Web.Models.Landing;
using TodoX.Web.Services.Landing;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LandingIntroVideoTests
{
    [Theory]
    [InlineData("AUTOPLAY_LOOP")]
    [InlineData("CLICK_TO_PLAY")]
    [InlineData("AUTOPLAY_ONCE")]
    public void PublishedModesAreValid(string mode)
    {
        LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings { PlaybackMode = mode });
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("")]
    public void UnknownModeIsRejected(string mode)
    {
        Assert.Throws<ArgumentException>(() => LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings { PlaybackMode = mode }));
    }

    [Fact]
    public void EnabledConfigurationRequiresVideo()
    {
        Assert.Throws<ArgumentException>(() => LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings { IsEnabled = true }));
    }

    [Theory]
    [InlineData("https://evil.example/video.mp4")]
    [InlineData("/media/landing/intro/videos/../secret.mp4")]
    [InlineData("/media/landing/intro/videos/test.mp4")]
    public void ExternalOrUntrustedMediaIsRejected(string url)
    {
        Assert.Throws<ArgumentException>(() => LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings { VideoUrl = url }));
    }

    [Fact]
    public void GeneratedMediaUrlIsAccepted()
    {
        LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings
        {
            IsEnabled = true,
            VideoUrl = "/media/landing/intro/videos/0123456789abcdef0123456789abcdef.mp4"
        });
    }

    [Fact]
    public void AnonymousAndViewOnlyCannotWrite()
    {
        Assert.Throws<UnauthorizedAccessException>(() => LandingIntroVideoWriteAccess.Demand(null));
        Assert.Throws<UnauthorizedAccessException>(() => LandingIntroVideoWriteAccess.Demand(new CurrentUserSession()));
        var reader = new CurrentUserSession { IsAuthenticated = true, Permissions = [LandingIndustryPermissions.View] };
        Assert.Throws<UnauthorizedAccessException>(() => LandingIntroVideoWriteAccess.Demand(reader));
        reader.Permissions.Add(LandingIndustryPermissions.Update);
        LandingIntroVideoWriteAccess.Demand(reader);
    }

    [Fact]
    public void CustomerPermissionCannotOverrideCustomerRestriction()
    {
        Assert.False(LandingIntroVideoWriteAccess.CanWrite(new CurrentUserSession
        {
            IsAuthenticated = true, Role = TodoXUserRole.CustomerUser,
            Permissions = [LandingIndustryPermissions.Update]
        }));
    }

    [Theory]
    [InlineData("a.mp4", "video/mp4", 1, true, true)]
    [InlineData("a.webm", "video/webm", 1, true, true)]
    [InlineData("a.png", "image/png", 1, false, true)]
    [InlineData("a.exe", "video/mp4", 1, true, false)]
    [InlineData("a.mp4", "image/png", 1, true, false)]
    [InlineData("a.mp4", "video/mp4", 0, true, false)]
    [InlineData("a.mp4", "video/mp4", 201, true, false)]
    public void UploadMetadataIsChecked(string name, string mime, long bytes, bool video, bool allowed)
    {
        if (allowed) LandingIntroVideoMediaService.ValidateUpload(name, mime, bytes, video, 200);
        else Assert.Throws<IntroUploadException>(() => LandingIntroVideoMediaService.ValidateUpload(name, mime, bytes, video, 200));
    }

    [Fact]
    public void CorruptHeaderIsRejected()
    {
        Assert.False(LandingIntroVideoMediaService.HasValidHeader(".mp4", new byte[16]));
        var mp4 = new byte[16];
        System.Text.Encoding.ASCII.GetBytes("ftyp").CopyTo(mp4, 4);
        Assert.True(LandingIntroVideoMediaService.HasValidHeader(".mp4", mp4));
        Assert.True(LandingIntroVideoMediaService.HasValidHeader(".webm", [0x1a, 0x45, 0xdf, 0xa3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public async Task OriginalVideoBytesAreCopiedWithoutModification()
    {
        var bytes = new byte[4096];
        Random.Shared.NextBytes(bytes);
        var path = Path.Combine(Path.GetTempPath(), $"intro-video-test-{Guid.NewGuid():N}.mp4");
        try
        {
            await LandingIntroVideoMediaService.CopyOriginalAsync(new MemoryStream(bytes), path);
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(bytes),
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FailedCopyDoesNotReplaceExistingVideo()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"intro-video-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var active = Path.Combine(folder, "active.mp4");
        var incoming = Path.Combine(folder, "incoming.mp4");
        await File.WriteAllBytesAsync(active, [1, 2, 3]);
        try
        {
            var ex = await Assert.ThrowsAsync<IntroUploadException>(() => LandingIntroVideoMediaService.CopyOriginalAsync(new ThrowingStream(), incoming));
            Assert.Equal(IntroUploadError.StreamInterrupted, ex.Category);
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(active));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("Simulated upload failure"));
    }

    [Theory]
    [InlineData("../secret.mp4", "secret.mp4")]
    [InlineData("C:\\private\\video.mp4", "video.mp4")]
    [InlineData("my\nclip.mp4", "myclip.mp4")]
    public void DiagnosticFileNameDoesNotExposePathsOrControlCharacters(string input, string expected)
        => Assert.Equal(expected, IntroUploadDiagnostics.SafeFileName(input));

    [Fact]
    public void OversizedVideoGetsSpecificCategoryAndConfiguredLimit()
    {
        const long limit = 200L * 1024 * 1024;
        var ex = Assert.Throws<IntroUploadException>(() => LandingIntroVideoMediaService.ValidateUpload("a.mp4", "video/mp4", limit + 1, true, limit));
        Assert.Equal(IntroUploadError.TooLarge, IntroUploadDiagnostics.Classify(ex));
        Assert.Contains("200 MB", IntroUploadDiagnostics.Message(ex.Category, limit));
    }

    [Fact]
    public void StorageAndStreamingFailuresHaveDistinctSafeMessages()
    {
        Assert.Equal(IntroUploadError.StorageUnavailable, IntroUploadDiagnostics.Classify(new UnauthorizedAccessException("C:\\private")));
        Assert.Equal(IntroUploadError.StorageUnavailable, IntroUploadDiagnostics.Classify(new IOException("C:\\private")));
        Assert.Equal(IntroUploadError.StreamInterrupted, IntroUploadDiagnostics.Classify(new IntroUploadException(IntroUploadError.StreamInterrupted, "C:\\private")));
        Assert.DoesNotContain("C:\\private", IntroUploadDiagnostics.Message(IntroUploadError.StorageUnavailable, 1));
        Assert.DoesNotContain("C:\\private", IntroUploadDiagnostics.Message(IntroUploadError.StreamInterrupted, 1));
    }

    [Fact]
    public void UnauthenticatedAdministratorCannotWrite()
    {
        Assert.Throws<UnauthorizedAccessException>(() => LandingIntroVideoWriteAccess.Demand(new CurrentUserSession
        {
            Role = TodoXUserRole.Admin, Permissions = [LandingIndustryPermissions.Update]
        }));
    }

    [Theory]
    [InlineData("/media/landing/intro/videos/0123456789abcdef0123456789abcdef.mp4?x=1")]
    [InlineData("/media/landing/intro/videos/0123456789abcdef0123456789abcdef.mp4/../../secret")]
    [InlineData("//evil.example/media/landing/intro/videos/0123456789abcdef0123456789abcdef.mp4")]
    public void MediaUrlCannotContainQueryOrTraversal(string url)
        => Assert.Throws<ArgumentException>(() => LandingIntroVideoRepository.Validate(new LandingIntroVideoSettings { VideoUrl = url }));
}