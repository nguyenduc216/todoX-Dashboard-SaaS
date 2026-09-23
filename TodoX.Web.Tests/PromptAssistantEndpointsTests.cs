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
        Assert.Contains("1.0 through 1.2", input, StringComparison.Ordinal);
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
}
