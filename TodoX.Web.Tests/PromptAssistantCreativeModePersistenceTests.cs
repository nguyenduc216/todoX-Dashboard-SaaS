using System.Reflection;
using TodoX.Web.Models;
using TodoX.Web.Services.PromptAssistant;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAssistantCreativeModePersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitModeIsRoundTrippedInSettingsContract(bool mode)
    {
        var request = new RVideoJobSettingsRequest { CreativeMode = mode };
        var saved = new RVideoJobSettingsDto { ProjectId = 1, CreativeMode = request.CreativeMode!.Value };
        Assert.Equal(mode, saved.CreativeMode);
    }

    [Fact]
    public void ProjectsHaveIndependentValuesAndLegacyRequestDoesNotOverride()
    {
        var a = new RVideoJobSettingsDto { ProjectId = 1, CreativeMode = true };
        var b = new RVideoJobSettingsDto { ProjectId = 2, CreativeMode = false };
        Assert.True(a.CreativeMode);
        Assert.False(b.CreativeMode);
        Assert.Null(new RVideoJobSettingsRequest().CreativeMode);
        Assert.Equal(a.CreativeMode, new RVideoJobSettingsRequest { CreativeMode = a.CreativeMode }.CreativeMode);
    }

    [Fact]
    public void RepositoryPreservesExistingModeForUnrelatedSave()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TodoX.Web", "Services", "VideoRender", "RVideoJobSettingsRepository.cs"));
        Assert.Contains("creative_mode=COALESCE(@CreativeMode, video_render.rvideo_job_settings.creative_mode)", sql);
        Assert.Contains("COALESCE(@CreativeMode, false)", sql);
        Assert.Contains("tenant_id = EXCLUDED.tenant_id", sql);
    }

    [Fact]
    public void CreativeBriefDoesNotLockNarrationOrRequireCta()
    {
        var build = typeof(PromptAssistantEndpoints).GetMethod("BuildAgentInput", BindingFlags.Static | BindingFlags.NonPublic)!;
        var input = (string)build.Invoke(null, [new PromptAssistantGenerateRequest(Guid.NewGuid(), "A fictional stage loses its star", Duration: 30, CreativeMode: true)])!;
        Assert.Contains("creative brief, not as narration to reproduce verbatim", input);
        Assert.Contains("A call to action is optional", input);
        Assert.Contains("preserve that wording and do not invent additional spoken lines", input);
        Assert.Contains("Never return empty scenes", input);
        Assert.DoesNotContain("Distribute the source wording verbatim", input);
    }
}