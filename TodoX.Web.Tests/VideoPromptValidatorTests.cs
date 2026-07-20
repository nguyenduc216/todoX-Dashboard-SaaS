using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class VideoPromptValidatorTests
{
    private readonly VideoPromptValidator _validator = new();

    [Fact]
    public void OmniFlashPrompt4095Characters_IsValid()
    {
        var result = _validator.Validate(new string('a', 4095), "omni-flash", null, 3);

        Assert.True(result.IsValid);
        Assert.Equal(4095, result.ActualCharacterCount);
        Assert.Equal(4096, result.MaxCharacterCount);
    }

    [Fact]
    public void OmniFlashPrompt4096Characters_IsValid()
    {
        var result = _validator.Validate(new string('a', 4096), "omni-flash", null, 3);

        Assert.True(result.IsValid);
        Assert.Equal(4096, result.ActualCharacterCount);
    }

    [Fact]
    public void OmniFlashPrompt4097Characters_Fails()
    {
        var result = _validator.Validate(new string('a', 4097), "omni-flash", null, 3);

        Assert.False(result.IsValid);
        Assert.Equal(VideoPromptValidator.TooLongErrorCode, result.ErrorCode);
        Assert.Equal(4097, result.ActualCharacterCount);
        Assert.Equal(4096, result.MaxCharacterCount);
    }

    [Fact]
    public void EmptyPrompt_Fails()
    {
        var result = _validator.Validate("   ", "omni-flash", null, 1);

        Assert.False(result.IsValid);
        Assert.Equal(VideoPromptValidator.RequiredErrorCode, result.ErrorCode);
        Assert.Equal(0, result.ActualCharacterCount);
    }

    [Fact]
    public void VietnamesePrompt_CountsUnicodeScalars()
    {
        var result = _validator.Validate("Đèn lồng bay lên", "omni-flash", null, 4);

        Assert.True(result.IsValid);
        Assert.Equal(16, result.ActualCharacterCount);
    }

    [Fact]
    public void EmojiPrompt_CountsSurrogatePairAsOneRune()
    {
        var result = _validator.Validate("Camera moves 😊", "omni-flash", null, 5);

        Assert.True(result.IsValid);
        Assert.Equal(14, result.ActualCharacterCount);
    }

    [Fact]
    public void ConfiguredLimitOverridesOmniFlashFallback()
    {
        var result = _validator.Validate("abcdef", "omni-flash", """{"max_prompt_characters":5}""", 6);

        Assert.False(result.IsValid);
        Assert.Equal(6, result.ActualCharacterCount);
        Assert.Equal(5, result.MaxCharacterCount);
    }

    [Fact]
    public void OtherModelDoesNotInheritOmniFlashLimit()
    {
        var result = _validator.Validate(new string('a', 4097), "grok-video", null, 7);

        Assert.True(result.IsValid);
        Assert.Null(result.MaxCharacterCount);
    }
}
