using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PromptAgentAuditTests
{
    private static PromptAgentAuditCheck Check(PromptAgentAuditReport report, string field) => report.Checks.Single(c => c.Field == field);

    [Fact]
    public void Agent_following_the_contract_passes()
    {
        var report = PromptAgentAudit.Grade("""{"duration":12,"scenes":[{"duration_seconds":6,"image_prompt":"a","motion_prompt":"m","voice":"v","tts_rate":1.0},{"duration_seconds":6,"image_prompt":"b","motion_prompt":"m2","voice":"v2","tts_rate":1.0}]}""");
        Assert.Equal(PromptAgentAuditStatuses.Pass, report.Status);
        Assert.Equal(2, report.SceneCount);
        Assert.Equal(12, report.TotalSeconds);
        Assert.All(report.Checks, c => Assert.Equal(PromptAgentAuditCheckStatuses.Ok, c.Status));
    }

    [Fact]
    public void Chibi_agent_shape_is_usable_only_through_todox_fixes()
    {
        // Shape of the 79AI "Người que 2d chibi" agent: fractional lengths, no motion_prompt.
        var report = PromptAgentAudit.Grade("""{"scenes":[{"duration_seconds":5.3,"image_prompt":"a","voice":"v","tts_rate":2.0,"visual_core":"x"},{"duration_seconds":3.2,"image_prompt":"b","voice":"v2","tts_rate":2.0}]}""");
        Assert.Equal(PromptAgentAuditStatuses.Fixed, report.Status);
        Assert.Equal(PromptAgentAuditCheckStatuses.Fixed, Check(report, "duration_seconds").Status);
        Assert.Contains("2/2", Check(report, "motion_prompt").Detail);
        Assert.Equal(PromptAgentAuditCheckStatuses.Fixed, Check(report, "motion_prompt").Status);
        Assert.Equal(PromptAgentAuditCheckStatuses.Ok, Check(report, "voice").Status);
        Assert.Equal(9, report.TotalSeconds);
    }

    [Fact]
    public void Missing_content_todox_cannot_fill_fails()
    {
        var report = PromptAgentAudit.Grade("""{"scenes":[{"duration_seconds":6,"motion_prompt":"m","tts_rate":1.0}]}""");
        Assert.Equal(PromptAgentAuditStatuses.Fail, report.Status);
        Assert.Equal(PromptAgentAuditCheckStatuses.Missing, Check(report, "image_prompt").Status);
        Assert.Equal(PromptAgentAuditCheckStatuses.Missing, Check(report, "voice").Status);
        Assert.Equal(PromptAgentAuditStatuses.Fail, PromptAgentAudit.Grade("""{"title":"no scenes"}""").Status);
        Assert.Equal(PromptAgentAuditStatuses.Error, PromptAgentAudit.Grade("not json").Status);
    }

    [Fact]
    public void Report_round_trips_and_detects_a_changed_agent()
    {
        var report = PromptAgentAudit.Grade("""{"scenes":[{"duration_seconds":6,"image_prompt":"a","motion_prompt":"m","voice":"v","tts_rate":1.0}]}""")
            with { AgentId = 7, AgentIdBase = "base", ModelCode = "GPT" };
        var copy = PromptAgentAuditReport.FromJson(report.ToJson())!;
        Assert.Equal(report.Status, copy.Status);
        Assert.Equal(report.Checks.Count, copy.Checks.Count);
        Assert.False(copy.IsStaleFor(new ServicePromptAssistantDto { GommoAgentId = 7, GommoAgentIdBase = "base", ModelCode = "GPT" }));
        Assert.True(copy.IsStaleFor(new ServicePromptAssistantDto { GommoAgentId = 8, GommoAgentIdBase = "base", ModelCode = "GPT" }));
        Assert.Null(PromptAgentAuditReport.FromJson(null));
    }

    [Fact]
    public void Audit_uses_the_same_agent_input_as_real_jobs_and_is_shown_before_launch()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var service = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Services", "PromptAssistant", "PromptAgentAuditService.cs"));
        Assert.Contains("PromptAssistantEndpoints.BuildAgentInput(", service);
        var dialog = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Components", "Dialogs", "ServicePromptAssistantDialog.razor"));
        Assert.Contains("Kiểm tra agent", dialog);
        Assert.DoesNotContain("`r`n", dialog);
        var servicesPage = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Components", "Pages", "Services.razor"));
        Assert.Contains("PromptAgentAuditStatuses.Label(audit?.Status)", servicesPage);
    }
}
