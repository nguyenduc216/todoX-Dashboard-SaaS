using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders.Kie;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KiePayloadBuilderTests
{
    private readonly KiePayloadBuilder _builder = new(new StaticOptionsMonitor<KieOptions>(new KieOptions
    {
        CallbackUrl = "https://dashboard.example.com/api/providers/kie/callback",
        AllowedModes = new[] { "720p", "1080p" },
        AllowedCharacterOrientations = new[] { "image", "video" }
    }));

    [Fact]
    public void BuildMotionControlRequest_ProducesExpectedPayload()
    {
        var payload = _builder.BuildMotionControlRequest(new KieMotionControlBuildRequest
        {
            Prompt = "  The character is dancing naturally.  ",
            CharacterImageUrl = "https://cdn.example.com/character.png",
            MotionVideoUrl = "https://cdn.example.com/motion.mp4",
            Mode = "720p",
            CharacterOrientation = "image"
        });

        Assert.Equal("kling-2.6/motion-control", payload.Model);
        Assert.Equal("https://dashboard.example.com/api/providers/kie/callback", payload.CallBackUrl);
        Assert.Equal("The character is dancing naturally.", payload.Input.Prompt);
        Assert.Equal(new[] { "https://cdn.example.com/character.png" }, payload.Input.InputUrls);
        Assert.Equal(new[] { "https://cdn.example.com/motion.mp4" }, payload.Input.VideoUrls);
        Assert.Equal("720p", payload.Input.Mode);
        Assert.Equal("image", payload.Input.CharacterOrientation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildMotionControlRequest_RejectsEmptyPrompt(string prompt)
    {
        var ex = Assert.Throws<KieProviderException>(() => _builder.BuildMotionControlRequest(ValidRequest(prompt: prompt)));
        Assert.Equal(KieErrorCodes.Unknown, ex.ErrorCode);
    }

    [Theory]
    [InlineData("http://cdn.example.com/character.png")]
    [InlineData("https://localhost/character.png")]
    [InlineData("https://127.0.0.1/character.png")]
    [InlineData("https://192.168.1.20/character.png")]
    [InlineData("https://minio-console.example.com/character.png")]
    [InlineData("https://cdn.example.com/browser/object/character.png")]
    [InlineData("/uploads/character.png")]
    public void BuildMotionControlRequest_RejectsUnsafeImageUrls(string imageUrl)
    {
        var ex = Assert.Throws<KieProviderException>(() => _builder.BuildMotionControlRequest(ValidRequest(imageUrl: imageUrl)));
        Assert.Equal(KieErrorCodes.InvalidInputUrl, ex.ErrorCode);
    }

    [Fact]
    public void BuildMotionControlRequest_RejectsInvalidMode()
    {
        var ex = Assert.Throws<KieProviderException>(() => _builder.BuildMotionControlRequest(ValidRequest(mode: "4k")));
        Assert.Equal(KieErrorCodes.InvalidMode, ex.ErrorCode);
    }

    [Fact]
    public void BuildMotionControlRequest_RejectsInvalidOrientation()
    {
        var ex = Assert.Throws<KieProviderException>(() => _builder.BuildMotionControlRequest(ValidRequest(orientation: "sideways")));
        Assert.Equal(KieErrorCodes.InvalidOrientation, ex.ErrorCode);
    }

    private static KieMotionControlBuildRequest ValidRequest(
        string prompt = "Dance naturally.",
        string imageUrl = "https://cdn.example.com/character.png",
        string videoUrl = "https://cdn.example.com/motion.mp4",
        string mode = "720p",
        string orientation = "image")
        => new()
        {
            Prompt = prompt,
            CharacterImageUrl = imageUrl,
            MotionVideoUrl = videoUrl,
            Mode = mode,
            CharacterOrientation = orientation
        };
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
