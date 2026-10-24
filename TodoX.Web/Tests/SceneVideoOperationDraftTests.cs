using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SceneVideoOperationDraftTests
{
    public static SceneVideoSupersedeAcknowledgement Ack() => new(1, 2, Guid.NewGuid(), Guid.NewGuid(),
        "old-request", 0, new string('a', 32), new string('b', 32), Guid.NewGuid(), true, "User requests a separate operation; outcome unknown.");
    public static CurrentUserSession Actor() => new() { UserId = Guid.NewGuid(), IsAuthenticated = true };

    [Fact]
    public void RequiresAuthenticatedActorAndExplicitRiskConsent()
    {
        var a = Ack();
        Assert.Throws<UnauthorizedAccessException>(() => SceneVideoOperationDraftService.Validate(a, new()));
        Assert.Throws<ArgumentException>(() => SceneVideoOperationDraftService.Validate(a with { RiskAcknowledged = false }, Actor()));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void RejectsFabricatedOrMalformedSnapshot(string hash)
        => Assert.Throws<ArgumentException>(() => SceneVideoOperationDraftService.Validate(Ack() with { OldJobSnapshotHash = hash }, Actor()));

    [Fact]
    public void RejectsMissingIdempotencyAndOverflowGeneration()
    {
        Assert.Throws<ArgumentException>(() => SceneVideoOperationDraftService.Validate(Ack() with { IdempotencyKey = Guid.Empty }, Actor()));
        Assert.Throws<ArgumentException>(() => SceneVideoOperationDraftService.Validate(Ack() with { ExpectedGeneration = long.MaxValue }, Actor()));
    }

    [Fact]
    public void IdempotencyFingerprintBindsActorSnapshotGenerationAndInput()
    {
        var a = Ack(); var actor = Guid.NewGuid();
        var hash = SceneVideoOperationDraftService.RequestHash(a, actor, "{}");
        Assert.Equal(hash, SceneVideoOperationDraftService.RequestHash(a, actor, "{}"));
        Assert.NotEqual(hash, SceneVideoOperationDraftService.RequestHash(a, Guid.NewGuid(), "{}"));
        Assert.NotEqual(hash, SceneVideoOperationDraftService.RequestHash(a with { ExpectedGeneration = 1 }, actor, "{}"));
        Assert.NotEqual(hash, SceneVideoOperationDraftService.RequestHash(a, actor, "{\"changed\":true}"));
    }

    [Fact]
    public void PreparedIsNeverAuthorizationToSubmit()
        => Assert.False(new SceneVideoOperationDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "new", false).SafeRerenderReady);

    [Fact]
    public void QueuedVersionFromSameOperationCanBeReused()
    {
        var job = Guid.NewGuid();
        var version = new SceneVideoVersionDto { Id = Guid.NewGuid(), ProjectId = 1, SceneId = 2, RenderJobId = job, LogicalRequestId = "same", Status = "queued" };
        SceneVideoOperationOwnership.RequireSameOperation(version, Request(job));
        Assert.Equal(job, version.RenderJobId);
    }

    [Theory]
    [InlineData("job")]
    [InlineData("project")]
    [InlineData("scene")]
    [InlineData("logical")]
    [InlineData("unowned")]
    public void LogicalHitCannotTransferOwnerOrCrossScene(string mismatch)
    {
        var job = Guid.NewGuid();
        var version = new SceneVideoVersionDto { Id = Guid.NewGuid(), ProjectId = 1, SceneId = 2, RenderJobId = job, LogicalRequestId = "same" };
        if (mismatch == "job") version.RenderJobId = Guid.NewGuid();
        if (mismatch == "project") version.ProjectId = 3;
        if (mismatch == "scene") version.SceneId = 4;
        if (mismatch == "logical") version.LogicalRequestId = "other";
        if (mismatch == "unowned") version.RenderJobId = null;
        var owner = version.RenderJobId;
        Assert.Throws<RenderJobPendingReconciliationException>(() => SceneVideoOperationOwnership.RequireSameOperation(version, Request(job)));
        Assert.Equal(owner, version.RenderJobId);
    }

    [Fact]
    public void PreparedVersionIsCompatibleWithTheCanonicalWorkerCandidate()
    {
        var policy=RVideoVideoModelPolicy.GetInitial();
        var input=new SceneVideoRenderWorkItemInput {ProviderCode=policy.ProviderCode,CapabilityCode="image_to_video",
            ProviderCapabilityId=11,ModelName=policy.Model,RequestedModelCode=policy.Model,RequestedMode=policy.Mode,
            DurationSeconds=8,Resolution="720p"};
        var version=new SceneVideoVersionDto {ProviderCode=input.ProviderCode,ProviderCapabilityId=11,ModelName=policy.Model,
            RenderConfigJson=System.Text.Json.JsonSerializer.Serialize(SceneVideoOperationDraftService.PreparedRenderConfig(input,1,Guid.NewGuid()))};
        var method=typeof(SceneVideoWorkerHandler).GetMethod("IsCompatibleVersion",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(true,method.Invoke(null,[version,input,policy,8,"720p"]));
        Assert.Equal(false,method.Invoke(null,[version,input,policy,6,"720p"]));
    }

    [Fact]
    public void BillingReferenceIsStablePerNewOperationAndNeverReusesHistoricalCharge()
    {
        var oldJob=Guid.NewGuid(); var newJob=Guid.NewGuid(); var version=Guid.NewGuid().ToString("N");
        var first=PointBillingReference.ForOperation(newJob,"rvideo_scene_video",version,PointBillingIntent.UserRerender,newJob);
        Assert.Equal(first,PointBillingReference.ForOperation(newJob,"rvideo_scene_video",version,PointBillingIntent.UserRerender,newJob));
        Assert.NotEqual(first,PointBillingReference.ForOperation(oldJob,"rvideo_scene_video",version,PointBillingIntent.UserRerender,oldJob));
    }

    [Fact]
    public void MergeCannotConsumeDifferentSelectionEvenIfItChangesBackBeforeCommit()
    {
        var selected=Guid.NewGuid();
        var snapshot=System.Text.Json.JsonSerializer.Serialize(new {selected=new Dictionary<string,string>{{"2",selected.ToString()}}});
        VideoRenderMergeHandler.ValidateConsumedSelection(snapshot,[(2L,(Guid?)selected)]);
        Assert.Throws<RenderJobDeferredException>(()=>VideoRenderMergeHandler.ValidateConsumedSelection(snapshot,[(2L,(Guid?)Guid.NewGuid())]));
    }

    [Fact]
    public void V2CompositeModelKeyRetainsModelAndModeWhileLegacyCodeRemainsSupported()
    {
        var policy=RVideoVideoModelPolicy.GetInitial();
        var meta=new ScenePromptMetadata();
        meta.Extra["video_model"]=RVideoSceneVideoModelOptionsService.BuildKey(policy.ProviderCode,policy.Model,policy.Mode);
        Assert.Equal((policy.Model,policy.Mode),RVideoSceneVideoRenderAction.ResolveRequestedSelection(meta));
        var persisted=ScenePromptMetadata.Parse(meta.Serialize());
        Assert.Equal((policy.Model,policy.Mode),RVideoSceneVideoRenderAction.ResolveRequestedSelection(persisted));
        meta.Extra["video_model"]=policy.Model;
        Assert.Equal((policy.Model,(string?)null),RVideoSceneVideoRenderAction.ResolveRequestedSelection(meta));
        meta.Extra["video_model"]="other-provider|model|mode";
        Assert.Throws<ArgumentException>(()=>RVideoSceneVideoRenderAction.ResolveRequestedSelection(meta));
    }

    private static SceneVideoVersionCreateRequest Request(Guid job)
        => new(1, 2, null, null, null, job, "same", "image", "video", new { }, new { });
}
