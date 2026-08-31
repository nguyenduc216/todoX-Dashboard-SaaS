using System.Reflection;
using System.Text;
using Xunit;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Tests;

public sealed class MediaFileServiceAudioRegressionTests
{
    [Fact]
    public void OctetStreamMp3PayloadIsAccepted()
    {
        var mime = InvokePrivateStatic<string>("ResolveDownloadedBinaryMime", "audio/mpeg", "application/octet-stream", "scene-audio.mp3");
        var mp3Payload = Encoding.ASCII.GetBytes("ID3\0\0\0\0\0\0\0\0");

        Assert.Equal("audio/mpeg", mime);
        Assert.True(InvokePrivateStatic<bool>("LooksLikeAudio", mp3Payload, mime));
    }

    [Fact]
    public void WavPayloadIsAccepted()
    {
        var mime = InvokePrivateStatic<string>("ResolveDownloadedBinaryMime", "audio/wav", "audio/wav", "scene-audio.wav");
        var wavPayload = Encoding.ASCII.GetBytes("RIFF\x24\0\0\0WAVEfmt ");

        Assert.Equal("audio/wav", mime);
        Assert.True(InvokePrivateStatic<bool>("LooksLikeAudio", wavPayload, mime));
    }

    [Theory]
    [InlineData("audio/mp4")]
    [InlineData("audio/m4a")]
    public void M4AAndAudioMp4PayloadsAreAccepted(string expectedMime)
    {
        var mime = InvokePrivateStatic<string>("ResolveDownloadedBinaryMime", expectedMime, expectedMime, "scene-audio.m4a");
        var m4aPayload = new byte[]
        {
            0x00, 0x00, 0x00, 0x20,
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'M', (byte)'4', (byte)'A', (byte)' ',
            0x00, 0x00, 0x00, 0x00,
            (byte)'i', (byte)'s', (byte)'o', (byte)'m'
        };

        Assert.Equal(expectedMime, mime);
        Assert.True(InvokePrivateStatic<bool>("LooksLikeAudio", m4aPayload, mime));
    }

    [Fact]
    public void HtmlAndJsonPayloadsAreRejectedAsAudio()
    {
        var htmlPayload = Encoding.UTF8.GetBytes("<html><body>error</body></html>");
        var jsonPayload = Encoding.UTF8.GetBytes("{\"error\":\"nope\"}");

        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", htmlPayload, "audio/mpeg"));
        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", jsonPayload, "audio/mpeg"));
        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", htmlPayload, "audio/wav"));
        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", jsonPayload, "audio/mp4"));
    }

    [Fact]
    public void SceneAudioRetryReusesExistingProviderTaskWithoutResubmitting()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioRenderHandler.cs");
        var guardStart = source.IndexOf("if (string.IsNullOrWhiteSpace(requestId))", StringComparison.Ordinal);
        var reuseStart = source.IndexOf("else", guardStart, StringComparison.Ordinal);
        var statusStart = source.IndexOf("var status = await _vbee.GetStatusAsync", reuseStart, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(reuseStart > guardStart);
        Assert.True(statusStart > reuseStart);

        var reuseBranch = source[reuseStart..statusStart];

        Assert.Contains("requestId = NormalizeRequestId(submitted.RequestId);", source);
        Assert.Contains("await _versions.MarkSceneAudioVersionSubmittedAsync(version.Id, \"vbee\"", source);
        Assert.Contains("await _vbee.GetStatusAsync(requestId, options, ct);", source);
        Assert.DoesNotContain("SubmitAsync", reuseBranch);
    }

    [Fact]
    public void SceneAudioProviderSuccessDownloadsAndCompletesVersion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioRenderHandler.cs");

        Assert.Contains("DownloadAndSaveBinaryAtObjectKeyAsync", source);
        Assert.Contains("CompleteSceneAudioVersionAsync", source);
        Assert.Contains("saved.PublicUrl ?? saved.FileUrl", source);
        Assert.Contains("ProviderTaskId: requestId", source);
        Assert.Contains("MimeType: saved.MimeType", source);
    }

    [Fact]
    public void VbeeBinaryDownloadUsesRawUrlWithoutBearerOrAcceptAndFollowsRedirects()
    {
        var source = ReadRepoFile("Services", "Media", "MediaFileService.cs");
        var start = source.IndexOf("private async Task<MediaFileDto> DownloadBinaryToObjectKeyAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<MediaFileDto> SaveDownloadedBinaryStreamAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var download = source[start..end];
        Assert.Contains("var requestUri = fileUrl;", download);
        Assert.DoesNotContain("Uri.EscapeDataString", download, StringComparison.Ordinal);
        Assert.DoesNotContain("request.Headers.Authorization", download, StringComparison.Ordinal);
        Assert.DoesNotContain("request.Headers.Accept", download, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.RedirectKeepVerb", download, StringComparison.Ordinal);
        Assert.Contains("currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);", download, StringComparison.Ordinal);
        Assert.Contains("CreateClient(\"MediaBinaryDownload\")", download, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaBinaryDownloadClientFollowsN8nFileDownloadTransportContract()
    {
        var source = ReadRepoFile("Program.cs");

        Assert.Contains("AddHttpClient(\"MediaBinaryDownload\"", source, StringComparison.Ordinal);
        Assert.Contains("AllowAutoRedirect = false", source, StringComparison.Ordinal);
        Assert.Contains("AutomaticDecompression = System.Net.DecompressionMethods.All", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VbeeBinaryDownloadDiagnosticsExcludeSecretsAndNarration()
    {
        var source = ReadRepoFile("Services", "Media", "MediaFileService.cs");
        var start = source.IndexOf("MEDIA_BINARY_URL_DOWNLOAD_FAILED", StringComparison.Ordinal);
        var end = source.IndexOf("throw new InvalidOperationException", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var diagnostic = source[start..end];
        Assert.Contains("initialHost", diagnostic, StringComparison.Ordinal);
        Assert.Contains("finalHost", diagnostic, StringComparison.Ordinal);
        Assert.Contains("httpStatus", diagnostic, StringComparison.Ordinal);
        Assert.Contains("locationHost", diagnostic, StringComparison.Ordinal);
        Assert.Contains("contentType", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("token", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("narration", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var parameterTypes = args.Select(arg => arg?.GetType() ?? typeof(object)).ToArray();
        var method = typeof(MediaFileService).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
