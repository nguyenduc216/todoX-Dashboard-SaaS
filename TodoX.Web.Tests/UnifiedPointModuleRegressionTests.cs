using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class UnifiedPointModuleRegressionTests
{
    [Fact]
    public void RVideoParentBillingState_RequiresMatchingOperationAndChargeReference()
    {
        var billingOperationId = Guid.NewGuid();
        var otherOperationId = Guid.NewGuid();
        var chargeReferenceId = Guid.NewGuid();

        var events = new[]
        {
            new VideoProjectEventDto
            {
                EventType = "RVIDEO_PARENT_BILLED",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                DataJson = $$"""
                {
                  "billingOperationId":"{{otherOperationId}}",
                  "parentRenderJobId":"{{Guid.NewGuid()}}",
                  "chargeReferenceId":"{{Guid.NewGuid()}}"
                }
                """
            },
            new VideoProjectEventDto
            {
                EventType = "RVIDEO_PARENT_BILLED",
                CreatedAt = DateTime.UtcNow,
                DataJson = $$"""
                {
                  "billingOperationId":"{{billingOperationId}}",
                  "parentRenderJobId":"{{Guid.NewGuid()}}",
                  "chargeReferenceId":"{{chargeReferenceId}}"
                }
                """
            }
        };

        Assert.True(RVideoParentBillingState.HasCurrentOperationParentCharge(events, billingOperationId));
        Assert.False(RVideoParentBillingState.HasCurrentOperationParentCharge(events, Guid.NewGuid()));
    }

    [Fact]
    public void RVideoParentBillingState_RequiresVoiceChargeForAudioSkip()
    {
        var billingOperationId = Guid.NewGuid();
        var billedWithoutVoice = new[]
        {
            new VideoProjectEventDto
            {
                EventType = "RVIDEO_PARENT_BILLED",
                CreatedAt = DateTime.UtcNow,
                DataJson = $$"""
                {
                  "billingOperationId":"{{billingOperationId}}",
                  "parentRenderJobId":"{{Guid.NewGuid()}}",
                  "chargeReferenceId":"{{Guid.NewGuid()}}",
                  "voiceCount":0,
                  "voicePoints":0
                }
                """
            }
        };

        var billedWithVoice = new[]
        {
            new VideoProjectEventDto
            {
                EventType = "RVIDEO_PARENT_BILLED",
                CreatedAt = DateTime.UtcNow,
                DataJson = $$"""
                {
                  "billingOperationId":"{{billingOperationId}}",
                  "parentRenderJobId":"{{Guid.NewGuid()}}",
                  "chargeReferenceId":"{{Guid.NewGuid()}}",
                  "voiceCount":3,
                  "voicePoints":1500
                }
                """
            }
        };

        Assert.False(RVideoParentBillingState.HasCurrentOperationParentVoiceCharge(billedWithoutVoice, billingOperationId));
        Assert.True(RVideoParentBillingState.HasCurrentOperationParentVoiceCharge(billedWithVoice, billingOperationId));
    }

    [Fact]
    public void RVideoParentBillingState_FallsBackToCoreJobId()
    {
        var project = new VideoProjectDto
        {
            CoreJobId = Guid.NewGuid()
        };

        Assert.Equal(project.CoreJobId, RVideoParentBillingState.ResolveBillingOperationId(project, Guid.NewGuid()));
    }

    [Fact]
    public void PointBalanceNotifier_CarriesCustomerIdentity()
    {
        var notifier = new PointBalanceChangeNotifier(LoggerFactory.Create(builder => { }).CreateLogger<PointBalanceChangeNotifier>());
        var observed = Guid.Empty;

        notifier.Changed += customerId => observed = customerId;

        var customerId = Guid.NewGuid();
        notifier.NotifyChanged(customerId);

        Assert.Equal(customerId, observed);
    }

    [Fact]
    public void RenderVideoJobs_ShowsInitialEstimateBeforeStart()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Components", "Pages", "RenderVideoJobs.razor"));

        Assert.Contains("Dự kiến sử dụng:", razor);
        Assert.Contains("CanStartInitialRender", razor);
        Assert.Contains("RefreshInitialEstimateAsync", razor);
        Assert.Contains("Không đủ điểm để thực hiện video này.", razor);
        Assert.Contains("FormatEstimateLine", razor);
        Assert.Contains("FormatPoints", razor);
        Assert.Contains("ValueChanged=\"OnVoiceModeChangedAsync\"", razor);
        Assert.Contains("await RefreshInitialEstimateAsync();", razor);
    }

    [Fact]
    public void WalletService_NotifiesAffectedCustomerAfterCommit()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "WalletService.cs"));

        Assert.Contains("tx.Commit();", source);
        Assert.Contains("_balanceChanges.NotifyChanged(customerId);", source);
        Assert.Contains("_balanceChanges.NotifyChanged(customerId.Value);", source);
        Assert.Contains("RedeemVoucherAsync", source);
    }

    [Fact]
    public void SceneImageBatchAndVideoHandlersUseScopedParentBillingIdentity()
    {
        var imageHandler = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "Render", "SceneImageBatchRenderHandler.cs"));
        var videoHandler = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "VideoRender", "SceneVideoRenderHandler.cs"));
        var audioHandler = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs"));

        Assert.Contains("billingOperationId", imageHandler);
        Assert.Contains("billingOperationId", videoHandler);
        Assert.Contains("billingOperationId", audioHandler);
        Assert.Contains("ResolveBillingOperationId", imageHandler);
        Assert.Contains("ResolveBillingOperationId", videoHandler);
        Assert.Contains("HasCurrentOperationParentVoiceCharge", audioHandler);
        Assert.Contains("billingScenes = project.Scenes", imageHandler);
        Assert.Contains(".Where(x => input.SceneIds is null || input.SceneIds.Contains(x.Id))", imageHandler);
        Assert.DoesNotContain("billingScenes = project.Scenes.OrderBy(x => x.SceneIndex)\r\n            .Where(x => input.SceneIds", imageHandler);
        Assert.DoesNotContain("project.Events.Any(x => x.EventType == \"RVIDEO_PARENT_BILLED\")", videoHandler);
        Assert.DoesNotContain("project.Events.Any(x => x.EventType == \"RVIDEO_PARENT_BILLED\")", audioHandler);
    }

    private static string RepoRoot
        => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
