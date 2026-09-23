using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantVideoOutputValidatorTests
{
    [Theory]
    [InlineData(4, 1.0)]
    [InlineData(8, 1.2)]
    public void AcceptsDurationAndTtsSpeedBoundaries(int duration, double speed)
    {
        using var prompt = JsonDocument.Parse(CreatePrompt(duration, speed));

        Assert.Empty(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement));
    }

    [Theory]
    [InlineData(3, 1.0, "duration_out_of_range")]
    [InlineData(9, 1.0, "duration_out_of_range")]
    [InlineData(4, 0.99, "tts_rate_out_of_range")]
    [InlineData(4, 1.21, "tts_rate_out_of_range")]
    public void RejectsDurationOrTtsSpeedOutsideAllowedRange(int duration, double speed, string expectedCode)
    {
        using var prompt = JsonDocument.Parse(CreatePrompt(duration, speed));

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(prompt.RootElement), error => error.Code == expectedCode);
    }

    [Fact]
    public void RejectsMissingSceneAndRequiredPromptFields()
    {
        using var missingScenes = JsonDocument.Parse("{}");
        using var missingFields = JsonDocument.Parse("""
            {"scenes":[{"duration_seconds":4,"tts_rate":1.0}]}
            """);

        Assert.Contains(PromptAssistantVideoOutputValidator.Validate(missingScenes.RootElement), error => error.Path == "$.scenes");
        var errors = PromptAssistantVideoOutputValidator.Validate(missingFields.RootElement);
        Assert.Contains(errors, error => error.Path == "$.scenes[0].image_prompt");
        Assert.Contains(errors, error => error.Path == "$.scenes[0].motion_prompt");
        Assert.Contains(errors, error => error.Path == "$.scenes[0].voice");
    }

    private static string CreatePrompt(int duration, double speed)
        => $$"""
           {"scenes":[{"duration_seconds":{{duration}},"image_prompt":"A clear visual.","motion_prompt":"Move naturally.","voice":"Narration.","tts_rate":{{speed}}}]}
           """;
}
