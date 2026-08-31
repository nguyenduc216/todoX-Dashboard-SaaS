using System.Reflection;
using System.Text;
using Xunit;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Tests;

public sealed class MediaFileServiceAudioRegressionTests
{
    [Fact]
    public void AudioMpegContentTypeIsAccepted()
    {
        var mime = InvokePrivateStatic<string>("ResolveDownloadedBinaryMime", "audio/mpeg", "audio/mpeg", "scene-audio.mp3");
        var payload = Encoding.ASCII.GetBytes("ID3\0\0\0\0\0\0\0\0");

        Assert.Equal("audio/mpeg", mime);
        Assert.True(InvokePrivateStatic<bool>("LooksLikeAudio", payload, mime));
    }

    [Fact]
    public void OctetStreamMp3PayloadIsAcceptedButHtmlAndJsonAreRejected()
    {
        var mime = InvokePrivateStatic<string>("ResolveDownloadedBinaryMime", "audio/mpeg", "application/octet-stream", "scene-audio.mp3");
        var mp3Payload = Encoding.ASCII.GetBytes("ID3\0\0\0\0\0\0\0\0");
        var htmlPayload = Encoding.UTF8.GetBytes("<html><body>error</body></html>");
        var jsonPayload = Encoding.UTF8.GetBytes("{\"error\":\"nope\"}");

        Assert.Equal("audio/mpeg", mime);
        Assert.True(InvokePrivateStatic<bool>("LooksLikeAudio", mp3Payload, mime));
        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", htmlPayload, mime));
        Assert.False(InvokePrivateStatic<bool>("LooksLikeAudio", jsonPayload, mime));
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
