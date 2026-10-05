using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantEndpointsTests
{
    [Fact]
    public void BuildAgentInputProvidedContentPreservesSourceAndLeavesScenePlanningToAgent()
    {
        const string source = "  First source idea, then its consequence.\nKeep this wording.  ";
        var request = new PromptAssistantGenerateRequest(Guid.NewGuid(), source);

        var input = PromptAssistantEndpoints.BuildAgentInput(request);

        Assert.Contains(source, input, StringComparison.Ordinal);
        Assert.Contains("Do not rewrite, paraphrase, omit, or add source narration.", input, StringComparison.Ordinal);
        Assert.Contains("punctuation", input, StringComparison.Ordinal);
        Assert.Contains("semantic boundaries", input, StringComparison.Ordinal);
        Assert.Contains("Decide the scene count yourself", input, StringComparison.Ordinal);
        Assert.Contains("integer from 4 through 8", input, StringComparison.Ordinal);
        Assert.Contains("positive JSON number", input, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 through 1.2", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Scene count: 7", input, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAgentInputCreativeModeUsesOnlyTheRequestedTargetDuration()
    {
        var request = new PromptAssistantGenerateRequest(
            Guid.NewGuid(), "A video about patience", Duration: 30, CreativeMode: true);

        var input = PromptAssistantEndpoints.BuildAgentInput(request);

        Assert.Contains("CONTENT MODE: CREATIVE", input, StringComparison.Ordinal);
        Assert.Contains("target video duration of 30 seconds", input, StringComparison.Ordinal);
        Assert.Contains("You may create a hook", input, StringComparison.Ordinal);
        Assert.Contains("Choose the scene count yourself", input, StringComparison.Ordinal);
        Assert.Contains("positive JSON number", input, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 through 1.2", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Scene count: 7", input, StringComparison.Ordinal);
    }

    [Fact]
    public void ToResponseReturnsPromptJsonAndMetricsWithoutRawProviderData()
    {
        var generationId = Guid.NewGuid();
        var result = new ServicePromptGenerationResult
        {
            GenerationId = generationId,
            GeneratedJson = "{\"title\":\"demo\",\"scenes\":[]}",
            ValidationPassed = true,
            PromptTokens = 2,
            CompletionTokens = 3,
            TotalTokens = 5,
            Credit = 0.1m,
            FirstEventMs = 10,
            FirstContentMs = 20,
            TotalDurationMs = 30,
            ErrorMessage = "provider secret should never be returned"
        };

        var response = PromptAssistantEndpoints.ToResponse(result);

        Assert.True(response.Success);
        Assert.Equal(generationId, response.GenerationId);
        Assert.Equal("PASS", response.Status);
        Assert.Equal("demo", response.PromptJson?.GetProperty("title").GetString());
        Assert.Equal(30, response.Metrics?.TotalDurationMs);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public void GenerateEndpointRejectsCreativeModeWithoutTargetDuration()
    {
        var request = new PromptAssistantGenerateRequest(Guid.NewGuid(), "A video idea", CreativeMode: true);

        var error = PromptAssistantEndpoints.ValidateRequest(request);

        Assert.Equal("duration is required in creative mode.", error);
    }

    [Fact]
    public void ToResponseReturnsGenericFailureAndDoesNotExposeInternalError()
    {
        var result = new ServicePromptGenerationResult
        {
            GenerationId = Guid.NewGuid(),
            ValidationPassed = false,
            ErrorCode = "gommo_authentication_failed",
            ErrorMessage = "secret-token provider response details"
        };

        var response = PromptAssistantEndpoints.ToResponse(result);

        Assert.False(response.Success);
        Assert.Equal("FAIL", response.Status);
        Assert.Equal("gommo_authentication_failed", response.ErrorCode);
        Assert.Equal("Prompt Assistant generate failed.", response.ErrorMessage);
        Assert.DoesNotContain("secret-token", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("generated_json_malformed")]
    [InlineData("generated_output_validation_failed")]
    public void ToResponsePreservesGeneratedJsonFailureTaxonomy(string errorCode)
    {
        var result = new ServicePromptGenerationResult
        {
            GenerationId = Guid.NewGuid(),
            ValidationPassed = false,
            ErrorCode = errorCode
        };

        var response = PromptAssistantEndpoints.ToResponse(result);

        Assert.Equal(errorCode, response.ErrorCode);
    }

    // ------------------------------------------------------------------
    // ExtractUserInput — inverse of BuildAgentInput, used to restore the
    // editable "Nội dung video mong muốn" field when reopening old jobs.
    // ------------------------------------------------------------------

    public static TheoryData<string> AgentInputs => new()
    {
        PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
            Guid.NewGuid(), "Video về kiên nhẫn")),
        PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
            Guid.NewGuid(), "Nội dung gốc\ngiữ nguyên thứ tự.", Duration: 30, CreativeMode: true))
    };

    [Theory]
    [MemberData(nameof(AgentInputs))]
    public void ExtractUserInputRecoversOriginalUserTextFromPersistedAgentInput(string agentInput)
    {
        var expected = agentInput.Contains("USER IDEA:", StringComparison.Ordinal)
            ? "A video idea about patience"
            : "Nội dung gốc\ngiữ nguyên thứ tự.";
        // Rebuild with the exact expected user text so both current formats round-trip.
        var input = agentInput.Contains("USER IDEA:", StringComparison.Ordinal)
            ? PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
                Guid.NewGuid(), expected, Duration: 30, CreativeMode: true))
            : PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
                Guid.NewGuid(), expected));

        var extracted = PromptAssistantEndpoints.ExtractUserInput(input);

        Assert.Equal(expected, extracted);
    }

    [Fact]
    public void ExtractUserInputHandlesLegacyDurationSuffixFormat()
    {
        const string userText = "Ý tưởng video cũ";
        var agentInput = $"{userText}\n\nVideo duration: 30 seconds. Return only final TodoX prompt JSON.";

        var extracted = PromptAssistantEndpoints.ExtractUserInput(agentInput);

        Assert.Equal(userText, extracted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Random text that is not an agent input")]
    public void ExtractUserInputReturnsNullForUnrecognizedInput(string? agentInput)
    {
        Assert.Null(PromptAssistantEndpoints.ExtractUserInput(agentInput));
    }

    [Fact]
    public void ExtractUserInputDoesNotMatchMarkerInsideUserText()
    {
        // A marker embedded mid-line (part of user text) must not be treated as the section header.
        var agentInput = PromptAssistantEndpoints.BuildAgentInput(new PromptAssistantGenerateRequest(
            Guid.NewGuid(), "Some idea USER IDEA: not a header"));

        var extracted = PromptAssistantEndpoints.ExtractUserInput(agentInput);

        Assert.Equal("Some idea USER IDEA: not a header", extracted);
    }
}

public sealed class QuickPromptTranscriptMergerTests
{
    // RVID-UI-002 4.G — append semantics (never overwrite existing content).

    [Fact]
    public void AppendTranscriptEmptyExistingReceivesTranscriptAsIs()
    {
        Assert.Equal(
            "Hãy tạo video giải thích thủ tục chuyển tiền quốc tế",
            QuickPromptTranscriptMerger.AppendTranscript(null, "Hãy tạo video giải thích thủ tục chuyển tiền quốc tế"));
        Assert.Equal(
            "Hãy tạo video",
            QuickPromptTranscriptMerger.AppendTranscript("   ", "Hãy tạo video"));
    }

    [Fact]
    public void AppendTranscriptAppendsWithSingleSpaceSeparator()
    {
        var result = QuickPromptTranscriptMerger.AppendTranscript(
            "Tôi muốn tạo video về chuyển tiền quốc tế.",
            "Hãy tập trung vào những lỗi khách hàng thường gặp");

        Assert.Equal(
            "Tôi muốn tạo video về chuyển tiền quốc tế. Hãy tập trung vào những lỗi khách hàng thường gặp",
            result);
    }

    [Fact]
    public void AppendTranscriptDoesNotDoubleSeparatorWhenExistingEndsWithWhitespace()
    {
        var result = QuickPromptTranscriptMerger.AppendTranscript("Video dành cho MSB.  ", "Thêm nội dung");

        Assert.Equal("Video dành cho MSB. Thêm nội dung", result);
    }

    [Fact]
    public void AppendTranscriptWhitespaceOnlyTranscriptKeepsExistingText()
    {
        Assert.Equal(
            "Nội dung gốc",
            QuickPromptTranscriptMerger.AppendTranscript("Nội dung gốc", "   "));
    }
}

