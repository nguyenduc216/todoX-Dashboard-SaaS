using System.Text.Json;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantEndpointsTests
{
    [Fact]
    public void BuildAgentInputUsesPhase21DefaultsAndIncludesVideoConstraints()
    {
        var request = new PromptAssistantGenerateRequest(Guid.NewGuid(), "  Create a school uniform video.  ");

        var input = PromptAssistantEndpoints.BuildAgentInput(request);

        Assert.Contains("Create a school uniform video.", input, StringComparison.Ordinal);
        Assert.Contains("Video duration: 30 seconds", input, StringComparison.Ordinal);
        Assert.Contains("Scene count: 7", input, StringComparison.Ordinal);
        Assert.Contains("Return only final TodoX prompt JSON.", input, StringComparison.Ordinal);
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
