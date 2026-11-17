using System.Text;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// Scene 04 of job 246d162f stayed "Đang tạo ảnh" for a day: each 79AI poll used one retry attempt, the job ran out
// at 100 and was left "rendering"; 79AI then cancelled the task and nothing read it again.
public sealed class SceneImageStuckRecoveryTests
{
    private static string Read(params string[] parts) => File.ReadAllText(
        Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()), Encoding.UTF8);

    [Fact]
    public void PendingProviderPollsDoNotUseTheRetryBudgetAndTimeOutToTheNextModel()
    {
        var handler = Read("Services", "Render", "SceneImageRenderWorkItemHandler.cs");
        Assert.DoesNotContain("ScheduleRetryAsync", handler);
        Assert.Contains("enforceReconciliationLimit: false, enforceProviderPollTimeout: false", handler);
        Assert.Contains("FailAndFallbackAsync(job, input, scene, versionId, ProviderTimeoutCode", handler);
        Assert.Contains("RVideoImageModelPolicy.GetNext(input.ModelAttemptIndex)", handler);
    }

    [Fact]
    public void PollElapsedReadsTheFirstPollTime()
    {
        var now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, SceneImageRenderWorkItemHandler.PollElapsed("{}", now));
        Assert.Equal(TimeSpan.Zero, SceneImageRenderWorkItemHandler.PollElapsed(null, now));
        Assert.Equal(TimeSpan.FromMinutes(61),
            SceneImageRenderWorkItemHandler.PollElapsed("{\"providerPollStartedAt\":\"2026-11-16T08:59:00+00:00\"}", now));
    }

    [Fact]
    public void SweeperRequeuesAbandonedJobsAndReleasesOrphanVersions()
    {
        Assert.Contains("'providerPoll', true", SceneImageStuckSweeper.RequeueSql);
        Assert.Contains("< @maxRequeues", SceneImageStuckSweeper.RequeueSql);
        Assert.Contains("SET status='failed'", SceneImageStuckSweeper.ReleaseSql);
        Assert.Contains("NOT EXISTS", SceneImageStuckSweeper.ReleaseSql);
        Assert.DoesNotContain("{ActiveStatuses}", SceneImageStuckSweeper.ReleaseSql);
        Assert.Contains("AddHostedService<TodoX.Web.Services.VideoRender.SceneImageStuckSweeper>", Read("Program.cs"));
        Assert.True(SceneImageStuckSweeper.OrphanMinutes > SceneImageRenderWorkItemHandler.DefaultPollTimeoutMinutes);
    }

    [Fact]
    public void AuditLineExplainsWaitingAndFailures()
    {
        var now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var waiting = SceneImageAuditText.Describe(new SceneImageAudit(1, "pending_reconciliation", "provider_pending", null,
            "google_image_gen_banana_2", "30d0779d5fa0378d", now.AddMinutes(-12), now), now);
        Assert.Equal("Đang tạo ảnh bằng Nano Banana 2 · đã chờ 79AI 12 phút.", waiting!.Text);
        Assert.Equal(SceneImageAuditText.Tone.Info, waiting.Tone);

        var late = SceneImageAuditText.Describe(new SceneImageAudit(1, "submitted", null, null,
            "google_image_gen_banana_2", "x", now.AddMinutes(-90), now), now);
        Assert.Equal(SceneImageAuditText.Tone.Warning, late!.Tone);

        var failed = SceneImageAuditText.Describe(new SceneImageAudit(1, "failed", SceneImageRenderWorkItemHandler.ProviderTimeoutCode,
            "79AI không trả ảnh sau 60 phút (task x).", "google_image_gen_banana_2", "x", now, now), now);
        Assert.Equal("Lần tạo ảnh gần nhất (Nano Banana 2) lỗi: 79AI không trả ảnh sau 60 phút (task x).", failed!.Text);
        Assert.Equal(SceneImageAuditText.Tone.Error, failed.Tone);

        Assert.Null(SceneImageAuditText.Describe(new SceneImageAudit(1, "completed", null, null, null, null, now, now), now));
        Assert.Contains("SceneImageAuditText.Describe(_imageAudits.GetValueOrDefault(a.Id)",
            Read("Components", "Pages", "RVideo", "RVideoV2Preview.razor"));
    }
}
